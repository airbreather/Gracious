using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gracious;

internal static class RandomUInt32
{
    public static uint Next()
    {
        Unsafe.SkipInit(out uint result);
        Random.Shared.NextBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref result, 1)));
        return result;
    }
}
