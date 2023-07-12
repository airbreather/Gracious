using System.Buffers;
using System.Collections.Concurrent;

using Serilog;

using ZstdNet;

namespace Gracious;

internal sealed class PcmOutput : IAsyncDisposable
{
    private readonly BlockingCollection<Packet> _packets = new();

    private readonly FfmpegProcessWrapper _ffmpeg;

    private readonly Task _writingTask;

    public PcmOutput(int sampleRate, int channelCount, long streamStartTimestamp, long ticksPerSecond, Stream outputStream)
    {
        _ffmpeg = FfmpegProcessWrapper.Create(FfmpegArgs(channelCount: channelCount, sampleRate: sampleRate), outputStream) ?? throw new InvalidOperationException("ffmpeg wrapper should not return null for non-empty arg list");
        _ffmpeg.Start();
        _writingTask = Task.Run(async () =>
        {
            await using var __ = outputStream;

            double samplesPerTick = sampleRate / (double)ticksPerSecond;

            // any time we received a payload within 0.5 seconds of receiving another payload for the
            // same SSRC, it was almost certainly intended to be part of the same payload, so don't add
            // any silence between them.
            int silenceSamplesThreshold = sampleRate / 2;

            int bytesPerSample = channelCount * sizeof(short);
            int bytesPerSecond = bytesPerSample * sampleRate;

            using IMemoryOwner<byte> sharedBufOwner = MemoryPool<byte>.Shared.Rent(bytesPerSecond);
            Memory<byte> sharedBuf = sharedBufOwner.Memory;
            Memory<byte> oneSecondBuf = sharedBuf[..bytesPerSecond];

            long samplesWritten = 0;
            foreach (Packet packet in _packets.GetConsumingEnumerable())
            {
                long packetStartSample = (long)Math.Round((packet.StartTimestamp - streamStartTimestamp) * samplesPerTick);
                long silenceSamples = packetStartSample - samplesWritten;
                if (silenceSamples > silenceSamplesThreshold)
                {
                    oneSecondBuf.Span.Clear();
                    while (silenceSamples > sampleRate)
                    {
                        await _ffmpeg.WriteToStdinAsync(oneSecondBuf);
                        silenceSamples -= sampleRate;
                        samplesWritten += sampleRate;
                    }

                    await _ffmpeg.WriteToStdinAsync(oneSecondBuf[..(bytesPerSample * (int)silenceSamples)]);
                    samplesWritten += silenceSamples;
                }

                switch (packet.Type)
                {
                    case PacketType.EndOfRecording:
                        break;

                    case PacketType.VoiceReceived:
                        {
                            int innerPayloadStart = VoiceReceivedPacketMetadata.ReadFromBuffer(packet.Payload.Span, out _, out int sampleRate2, out int channelCount2);
                            if ((sampleRate, channelCount) != (sampleRate2, channelCount2))
                            {
                                goto default;
                            }

                            ReadOnlyMemory<byte> innerPayload = packet.Payload[innerPayloadStart..];
                            int sampleCount = Math.DivRem(innerPayload.Length, bytesPerSample, out int rem);
                            if (rem != 0)
                            {
                                throw new InvalidDataException("VoiceReceived packets must contain a whole number of PCM samples.");
                            }

                            await _ffmpeg.WriteToStdinAsync(innerPayload);
                            samplesWritten += sampleCount;
                            break;
                        }

                    case PacketType.StartOfSendMusic:
                        {
                            StartOfSendMusicPacketMetadata.ReadFromBuffer(packet.Payload.Span, out string pcmFilePath);
                            DecompressionStream musicStream;
                            try
                            {
                                musicStream = Files.OpenCompressedForFullAsyncRead(pcmFilePath);
                            }
                            catch (IOException)
                            {
                                Log.Warning("File {pcmFilePath} cannot be opened (anymore).  The result will not have our beautiful lovely music.", pcmFilePath);
                                break;
                            }

                            await using (musicStream)
                            {
                                int musicBytesWritten = 0;
                                int rd;
                                while ((rd = await musicStream.ReadAsync(sharedBuf)) != 0)
                                {
                                    await _ffmpeg.WriteToStdinAsync(sharedBuf[..rd]);
                                    musicBytesWritten += rd;
                                }

                                int musicSamplesWritten = Math.DivRem(musicBytesWritten, bytesPerSample, out int rem);
                                if (rem != 0)
                                {
                                    throw new InvalidDataException("Music files must contain a whole number of PCM samples.");
                                }

                                samplesWritten += musicSamplesWritten;
                            }

                            break;
                        }

                    default:
                        throw new InvalidOperationException("I was given a packet that I shouldn't have been.");
                }
            }

            await _ffmpeg.End();
        });
    }

    public void Next(in Packet packet)
    {
        if (!_packets.TryAdd(packet))
        {
            throw new InvalidOperationException("Tried to accept a packet after we finished...");
        }

        if (packet.Type == PacketType.EndOfRecording)
        {
            _packets.CompleteAdding();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_packets.IsAddingCompleted)
        {
            _packets.CompleteAdding();
        }

        await _writingTask;
        _ffmpeg.Dispose();
        _packets.Dispose();
    }

    private static string[] FfmpegArgs(int channelCount, int sampleRate)
    {
        return new[]
        {
            "-ac", $"{channelCount}",
            "-ar", $"{sampleRate}",
            "-f", "s16le",
            "-i", "pipe:0",
            "-ac", "2",
            "-ar", "48000",
            "-f", "flac",
        };
    }
}
