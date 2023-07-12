using DSharpPlus.VoiceNext;

using ZstdNet;

namespace Gracious;

internal sealed class MusicSender
{
    private readonly SemaphoreSlimWrapper _sem = new(1, 1);

    private readonly CancellationTokenSource _cts = new();

    private readonly Memory<byte> _buf;

    private readonly VoiceTransmitSink _sink;

    public MusicSender(VoiceNextConnection connection)
    {
        _sink = connection.GetTransmitSink();
        _buf = new byte[_sink.SampleLength * 10];
    }

    public SemaphoreSlimWrapper.Ticket? Authorize()
    {
        _cts.Token.ThrowIfCancellationRequested();
        return _sem.Wait(0);
    }

    public async ValueTask SendAsync(SemaphoreSlimWrapper.Ticket ticket, string pcmFilePath, CancellationToken cancellationToken = default)
    {
        if (!ticket.IsActiveFor(_sem))
        {
            throw new ArgumentException("Needs to be a ticket that we gave you.", nameof(ticket));
        }

        using var _ = ticket;
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken).Token;
        }
        else
        {
            cancellationToken = _cts.Token;
        }

        await using DecompressionStream stream = Files.OpenCompressedForFullAsyncRead(pcmFilePath);

        Memory<byte> bufRead;
        while (!(bufRead = _buf[..await stream.ReadAsync(_buf, cancellationToken)]).IsEmpty)
        {
            await _sink.WriteAsync(bufRead, cancellationToken);
        }

        await _sink.FlushAsync(cancellationToken);
    }

    public void Stop()
    {
        _cts.Cancel();
    }
}
