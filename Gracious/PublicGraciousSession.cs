using DSharpPlus.VoiceNext;

namespace Gracious;

internal sealed class PublicGraciousSession
{
    private readonly PacketStreamWriter _rawStream;

    private readonly MusicSender _musicSender;

    private int _state;

    public PublicGraciousSession(FileStream rawStream, VoiceNextConnection connection)
    {
        _rawStream = new(rawStream, connection);
        _musicSender = new(connection);
    }

    public Dictionary<string, int> TransmitFilePaths { get; } = new Dictionary<string, int>();

    public void Start()
    {
        switch (Interlocked.CompareExchange(ref _state, 1, 0))
        {
            case 0:
                break;

            case 1:
                throw new InvalidOperationException("Already running.");

            case 2:
                throw new NotSupportedException("Restarting a stream after stopping it is not supported.");

            default:
                throw new InvalidOperationException("Unknown state...");
        }

        _rawStream.Start();
    }

    public void PlayMusic(string pcmFilePath, Func<Task> onComplete)
    {
        if (_musicSender.Authorize() is not SemaphoreSlimWrapper.Ticket ticket)
        {
            throw new AlreadyPlayingMusicException();
        }

        Task.Run(async () =>
        {
            await _musicSender.SendAsync(ticket, pcmFilePath);
            _rawStream.MarkTransmitEnd();
            await onComplete();
        });
        _rawStream.MarkTransmitBegin(pcmFilePath);
    }

    public void Stop()
    {
        switch (Interlocked.CompareExchange(ref _state, 2, 1))
        {
            case 0:
                throw new InvalidOperationException("Can't stop before starting.");

            case 1:
                break;

            case 2:
                throw new InvalidOperationException("Can only stop once.");

            default:
                throw new InvalidOperationException("Unknown state...");
        }

        _rawStream.Stop();
        _musicSender.Stop();
    }
}
