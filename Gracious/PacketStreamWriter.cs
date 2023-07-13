/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using DSharpPlus.EventArgs;
using DSharpPlus.VoiceNext;
using DSharpPlus.VoiceNext.EventArgs;

namespace Gracious;

internal sealed class PacketStreamWriter
{
    private readonly SemaphoreSlimWrapper _sem = new(1, 1);

    private readonly FileStream _rawFile;

    private readonly ArraySegment<byte> _buf65536;

    private readonly ArraySegment<byte> _packetHeaderBuf;

    public PacketStreamWriter(FileStream rawFile, VoiceNextConnection connection)
    {
        byte[] sliceSource = new byte[65536 + Unsafe.SizeOf<PacketHeader>()];
        _buf65536 = new(sliceSource, 0, 65536);
        _packetHeaderBuf = new(sliceSource, 65536, Unsafe.SizeOf<PacketHeader>());

        _rawFile = rawFile;
        Connection = connection;
    }

    public VoiceNextConnection Connection { get; }

    public void Start()
    {
        long ts = Stopwatch.GetTimestamp();
        using var _ = _sem.Wait();

        int packetLength = StartOfStreamPacketData.WriteToBuffer(Stopwatch.Frequency, _buf65536);
        WritePacket(PacketType.StartOfStream, ts, _buf65536[..packetLength]);

        Connection.UserSpeaking += OnUserSpeakingAsync;
        Connection.VoiceReceived += OnVoiceReceivedAsync;
    }

    public void Stop()
    {
        long ts = Stopwatch.GetTimestamp();
        using var _ = _sem.Wait();

        WritePacket(PacketType.EndOfRecording, ts);

        Connection.UserSpeaking -= OnUserSpeakingAsync;
        Connection.VoiceReceived -= OnVoiceReceivedAsync;
        _rawFile.Dispose();
    }

    public void MarkTransmitBegin(string pcmFilePath)
    {
        long ts = Stopwatch.GetTimestamp();
        using var _ = _sem.Wait();

        int packetLength = StartOfSendMusicPacketMetadata.WriteToBuffer(pcmFilePath, _buf65536);
        WritePacket(PacketType.StartOfSendMusic, ts, _buf65536[..packetLength]);
    }

    public void MarkTransmitEnd()
    {
        long ts = Stopwatch.GetTimestamp();
        using var _ = _sem.Wait();

        WritePacket(PacketType.EndOfSendMusic, ts);
    }

    private Task OnUserSpeakingAsync(VoiceNextConnection conn, UserSpeakingEventArgs args)
    {
        long ts = Stopwatch.GetTimestamp();
        using var _ = _sem.Wait();

        int packetLength = UserSpeakingPacketData.WriteToBuffer(args, _buf65536);
        WritePacket(PacketType.UserSpeaking, ts, _buf65536[..packetLength]);
        return Task.CompletedTask;
    }

    private Task OnVoiceReceivedAsync(VoiceNextConnection conn, VoiceReceiveEventArgs args)
    {
        long ts = Stopwatch.GetTimestamp();
        using var _ = _sem.Wait();

        int packetLength = VoiceReceivedPacketMetadata.WriteToBuffer(args, _buf65536);
        WritePacket(PacketType.VoiceReceived, ts, _buf65536[..packetLength], args.PcmData.Span);
        return Task.CompletedTask;
    }

    private void WritePacket(PacketType packetType, long timestamp, ReadOnlySpan<byte> data1 = default, ReadOnlySpan<byte> data2 = default)
    {
        Unsafe.As<byte, PacketHeader>(ref MemoryMarshal.GetReference(_packetHeaderBuf.AsSpan())) = new()
        {
            PacketType = packetType,
            PacketStartTimestamp = timestamp,
            PacketSizeBytes = data1.Length + data2.Length,
        };

        _rawFile.Write(_packetHeaderBuf);
        if (!data1.IsEmpty)
        {
            _rawFile.Write(data1);
        }

        if (!data2.IsEmpty)
        {
            _rawFile.Write(data2);
        }
    }
}
