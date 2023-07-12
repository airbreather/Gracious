namespace Gracious;

internal static class StartOfStreamPacketData
{
    public static void ReadFromBuffer(ReadOnlySpan<byte> buf, out long ticksPerSecond)
    {
        ticksPerSecond = buf.Read<long>();
    }

    public static int WriteToBuffer(long ticksPerSecond, scoped Span<byte> buf)
    {
        Span<byte> remaining = buf;

        remaining.Write(ticksPerSecond);

        return buf.Length - remaining.Length;
    }
}
