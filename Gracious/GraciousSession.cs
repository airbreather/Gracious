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
