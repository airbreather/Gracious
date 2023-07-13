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
