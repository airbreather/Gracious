using System.Runtime.InteropServices;

namespace Gracious;

[StructLayout(LayoutKind.Sequential, Pack = 0)]
internal record struct PacketHeader
{
    public PacketType PacketType;

    public int PacketSizeBytes;

    public long PacketStartTimestamp;
}
