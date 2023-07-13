/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;

namespace Gracious;

internal sealed class GraciousSession
{
    private readonly PublicGraciousSession _publicSession;

    private readonly PrivateGraciousSession? _privateSession;

    public GraciousSession(ulong guildId, uint sessionId, DiscordUser authorizedUser, VoiceNextConnection connection, PublicGraciousSession publicSession, PrivateGraciousSession? privateSession)
    {
        GuildId = guildId;
        SessionId = sessionId;
        AuthorizedUser = authorizedUser;
        Connection = connection;

        _publicSession = publicSession;
        _privateSession = privateSession;
    }

    public ulong GuildId { get; }

    public uint SessionId { get; }

    public DiscordUser AuthorizedUser { get; }

    public VoiceNextConnection Connection { get; }

    public void Start()
    {
        _publicSession.Start();
        _privateSession?.Start();
    }

    public void PlayMusic(string pcmFilePath, Func<Task> onComplete)
    {
        _publicSession.PlayMusic(pcmFilePath, onComplete);
    }

    public void Stop()
    {
        _privateSession?.Stop();
        _publicSession.Stop();
    }
}
