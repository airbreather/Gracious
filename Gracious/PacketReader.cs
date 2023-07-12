namespace Gracious;

internal static class PacketReader
{
    public static async IAsyncEnumerable<Packet> ReadAsync(FileStream stream)
    {
        byte[] buf = new byte[4096];
        while (await stream.ReadOptionalAsync<PacketHeader>(buf) is PacketHeader header)
        {
            if (header.PacketSizeBytes > buf.Length)
            {
                long newLen = buf.Length;
                while (header.PacketSizeBytes > newLen)
                {
                    newLen = Math.Min(int.MaxValue, newLen + newLen);
                }

                buf = new byte[newLen];
            }

            Memory<byte> payload = buf.AsMemory(..header.PacketSizeBytes);
            await stream.ReadExactlyAsync(payload);
            yield return new(header.PacketType, header.PacketStartTimestamp, payload.ToArray());
        }
    }
}
