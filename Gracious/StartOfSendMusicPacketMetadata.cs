using System.Text;

namespace Gracious;

internal static class StartOfSendMusicPacketMetadata
{
    public static void ReadFromBuffer(ReadOnlySpan<byte> buf, out string pcmFilePath)
    {
        pcmFilePath = Encoding.UTF8.GetString(buf);
    }

    public static int WriteToBuffer(string pcmFilePath, Span<byte> buf)
    {
        Span<byte> remaining = buf;

        remaining.WriteCompleteUTF8(pcmFilePath);

        return buf.Length - remaining.Length;
    }
}
