using DSharpPlus.VoiceNext.EventArgs;

namespace Gracious;

internal static class VoiceReceivedPacketMetadata
{
    public static int ReadFromBuffer(ReadOnlySpan<byte> buf, out uint ssrc, out int sampleRate, out int channelCount)
    {
        ReadOnlySpan<byte> remaining = buf;

        ssrc = remaining.Read<uint>();
        sampleRate = remaining.Read<int>();
        channelCount = remaining.Read<int>();

        return buf.Length - remaining.Length;
    }

    public static int WriteToBuffer(VoiceReceiveEventArgs args, Span<byte> buf)
    {
        Span<byte> remaining = buf;

        remaining.Write(args.SSRC);
        remaining.Write(args.AudioFormat.SampleRate);
        remaining.Write(args.AudioFormat.ChannelCount);

        return buf.Length - remaining.Length;
    }
}
