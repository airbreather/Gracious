using System.Runtime.InteropServices;

namespace Gracious;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct Packet(
    PacketType Type,
    long StartTimestamp,
    ReadOnlyMemory<byte> Payload);
