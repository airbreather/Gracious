/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
namespace Gracious;

internal static class PacketReader
{
    public static async IAsyncEnumerable<Packet> ReadAsync(FileStream stream)
    {
        byte[] buf = new byte[4096];
        while (true)
        {
            PacketHeader? headerOrNull;
            try
            {
                headerOrNull = await stream.ReadOptionalAsync<PacketHeader>(buf);
            }
            catch (EndOfStreamException)
            {
                // the application was terminated abruptly while writing this packet.
                break;
            }

            if (headerOrNull is not PacketHeader header)
            {
                // the application successfully wrote all the packets that it intended to write, and
                // we've yielded the last of them, so we can stop now.
                break;
            }

            Memory<byte> payload = GrowIfNeededThenSlice(ref buf, header.PacketSizeBytes);
            try
            {
                await stream.ReadExactlyAsync(payload);
            }
            catch (EndOfStreamException)
            {
                // the application was terminated abruptly while writing this packet.
                break;
            }

            yield return new(header.PacketType, header.PacketStartTimestamp, payload.ToArray());
        }
    }

    private static Memory<byte> GrowIfNeededThenSlice(ref byte[] buf, int len)
    {
        if (len > buf.Length)
        {
            EnsureMinLength(ref buf, len);
        }

        return buf.AsMemory(..len);
    }

    private static void EnsureMinLength(ref byte[] buf, int minLen)
    {
        long newLen = buf.Length;
        while (minLen > newLen)
        {
            newLen = Math.Min(int.MaxValue, newLen + newLen);
        }

        buf = new byte[newLen];
    }
}
