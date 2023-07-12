using System.Runtime.InteropServices;

using DSharpPlus.Entities;

namespace Gracious;

internal sealed class SplitPcmOutputs : IAsyncDisposable
{
    private readonly Dictionary<uint, string> _ssrcMap;

    private readonly Dictionary<string, PcmOutput> _outputs = new();

    private readonly string _botUsername;

    private readonly string _outputDirectory;

    private readonly long _startTimestamp;

    private readonly long _ticksPerSecond;

    public SplitPcmOutputs(DiscordUser botUser, string outputDirectory, long startTimestamp, long ticksPerSecond, Dictionary<uint, string> ssrcMap)
    {
        _botUsername = $"{botUser.Username}#{botUser.Discriminator}";
        _outputDirectory = outputDirectory;
        _startTimestamp = startTimestamp;
        _ticksPerSecond = ticksPerSecond;
        _ssrcMap = ssrcMap;
    }

    public void Next(in Packet packet)
    {
        switch (packet.Type)
        {
            case PacketType.VoiceReceived:
            case PacketType.StartOfSendMusic:
                GetOutput(in packet).Next(in packet);
                break;
        }

        PcmOutput GetOutput(in Packet packet)
        {
            string finalName;
            int sampleRate, channelCount;
            switch (packet.Type)
            {
                case PacketType.VoiceReceived:
                    {
                        VoiceReceivedPacketMetadata.ReadFromBuffer(packet.Payload.Span, out uint ssrc, out sampleRate, out channelCount);
                        finalName = CollectionsMarshal.GetValueRefOrAddDefault(_ssrcMap, ssrc, out _) ??= $"(unknown user)#{ssrc}";
                        break;
                    }

                case PacketType.StartOfSendMusic:
                    {
                        finalName = _botUsername;
                        sampleRate = 48000;
                        channelCount = 2;
                        break;
                    }

                default:
                    throw new ArgumentException("Don't know how to get output for this packet.", nameof(packet));
            }

            ref PcmOutput? output = ref CollectionsMarshal.GetValueRefOrAddDefault(_outputs, finalName, out _);
            output ??= new(sampleRate, channelCount, _startTimestamp, _ticksPerSecond, Files.CreateAsync(Path.Combine(_outputDirectory, $"{finalName}.flac")));
            return output;
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception>? exceptionsToThrow = null;
        foreach (string key in _outputs.Keys.ToArray())
        {
            if (!_outputs.Remove(key, out PcmOutput? output))
            {
                // wat
                continue;
            }

            try
            {
                await output.DisposeAsync();
            }
            catch (Exception ex)
            {
                (exceptionsToThrow ??= new()).Add(ex);
            }
        }

        if (exceptionsToThrow is List<Exception> exceptions)
        {
            throw new AggregateException(exceptions);
        }
    }
}
