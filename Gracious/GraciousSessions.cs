using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;

using Microsoft.Extensions.Options;

namespace Gracious;

internal sealed class GraciousSessions
{
    private readonly SemaphoreSlimWrapper _sem = new(1, 1);

    private readonly Dictionary<ulong, GraciousSession> _sessionsByGuildId = new();

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
