/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
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
