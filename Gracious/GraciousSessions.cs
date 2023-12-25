/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;

using Microsoft.Extensions.Options;

namespace Gracious;

internal sealed class GraciousSessions
{
    private readonly SemaphoreSlimWrapper _sem = new(1, 1);

    private readonly Dictionary<ulong, GraciousSession> _sessionsByGuildId = [];

    private readonly IOptionsMonitor<GraciousConfiguration> _cfgSnapshot;

    private readonly DiscordClient _discord;

    public GraciousSessions(IOptionsMonitor<GraciousConfiguration> cfgSnapshot, DiscordClient discord)
    {
        _cfgSnapshot = cfgSnapshot;
        _discord = discord;
    }

    public async ValueTask<GraciousSession?> ForGuildIdAsync(ulong guildId)
    {
        using var _ = await _sem.WaitAsync();
        _sessionsByGuildId.TryGetValue(guildId, out GraciousSession? session);
        return session;
    }

    public async ValueTask<GraciousSession> BeginAsync(DiscordUser user, DiscordChannel channel)
    {
        if (channel.GuildId is not ulong guildId)
        {
            throw new ArgumentException("Must be in a guild.", nameof(channel));
        }

        GraciousConfiguration cfg = _cfgSnapshot.CurrentValue;
        uint sessionId = 0;
        DirectoryInfo? emergencyFolder = null;
        for (int i = 0; i < 1_000_000 && emergencyFolder?.Exists != false; i++)
        {
            sessionId = RandomUInt32.Next();
            JankStringTemplateParameters jank = new(sessionId);
            emergencyFolder = new(jank.Resolve(cfg.EmergencyFolder));
        }

        if (emergencyFolder?.Exists != false)
        {
            throw new CouldNotCreateSessionFolderException();
        }

        VoiceNextConnection connection = await channel.ConnectAsync();
        FileStream? rawPublicStream = null;
        try
        {
            emergencyFolder.Create();
            rawPublicStream = Files.CreateAsync(Path.Combine(emergencyFolder.FullName, "raw.dat"));
            PublicGraciousSession publicSession = new(rawPublicStream, connection);
            PrivateGraciousSession? privateSession = null;
            if (_discord.CurrentApplication.Owners.Contains(user))
            {
                privateSession = new(cfg.DesktopRecordingFfmpegArgs, emergencyFolder.FullName);
            }

            GraciousSession session = new(guildId, sessionId, user, connection, publicSession, privateSession);

            using (await _sem.WaitAsync())
            {
                if (!_sessionsByGuildId.TryAdd(guildId, session))
                {
                    throw new SessionAlreadyStartedException();
                }
            }

            session.Start();
            return session;
        }
        catch
        {
            rawPublicStream?.Dispose();
            connection.Dispose();
            throw;
        }
    }

    public async ValueTask<GraciousSession> EndAsync(DiscordUser user, ulong guildId)
    {
        GraciousSession? session;
        using (await _sem.WaitAsync())
        {
            if (!_sessionsByGuildId.TryGetValue(guildId, out session))
            {
                throw new SessionDoesNotExistException();
            }

            if (session.AuthorizedUser.Id != user.Id)
            {
                throw new SessionStartedBySomeoneElseException();
            }

            _sessionsByGuildId.Remove(guildId);
        }

        using (session.Connection)
        {
            session.Stop();
            return session;
        }
    }
}
