using System.Text;

using DSharpPlus.EventArgs;

namespace Gracious;

internal static class UserSpeakingPacketData
{
    public static void ReadFromBuffer(ReadOnlySpan<byte> buf, out uint ssrc, out string usernameWithDiscriminator)
    {
        ssrc = buf.Read<uint>();
        usernameWithDiscriminator = Encoding.UTF8.GetString(buf);
    }

    public static int WriteToBuffer(UserSpeakingEventArgs args, scoped Span<byte> buf)
    {
        Span<byte> remaining = buf;

        remaining.Write(args.SSRC);

        remaining.WriteCompleteUTF8(args.User.Username);
        remaining.Write((byte)'#');
        remaining.WriteCompleteUTF8(args.User.Discriminator);

        return buf.Length - remaining.Length;
    }
}
