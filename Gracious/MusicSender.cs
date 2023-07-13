/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
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
