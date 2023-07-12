using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Gracious;

internal static class SpanAndStreamExtensions
{
    public static async ValueTask<bool> ReadNoneOrAllAsync(this Stream s, Memory<byte> buf, CancellationToken cancellationToken = default)
    {
        int rd = await s.ReadAsync(buf, cancellationToken);
        if (rd == 0)
        {
            return buf.IsEmpty;
        }

        if (rd < buf.Length)
        {
            await s.ReadExactlyAsync(buf[rd..], cancellationToken);
        }

        return true;
    }

    public static async ValueTask<T?> ReadOptionalAsync<T>(this Stream s, Memory<byte> buf, CancellationToken cancellationToken = default)
        where T : unmanaged
    {
        if (buf.Length < Unsafe.SizeOf<T>())
        {
            throw new ArgumentException("must be large enough to hold one instance of the object.", nameof(buf));
        }

        return (await s.ReadNoneOrAllAsync(buf[..Unsafe.SizeOf<T>()], cancellationToken))
            ? Unsafe.As<byte, T>(ref buf.Span[0])
            : default(T?);
    }

    public static async ValueTask<T> ReadAsync<T>(this Stream s, Memory<byte> buf, CancellationToken cancellationToken = default)
        where T : unmanaged
    {
        if (buf.Length < Unsafe.SizeOf<T>())
        {
            throw new ArgumentException("must be large enough to hold one instance of the object.", nameof(buf));
        }

        await s.ReadExactlyAsync(buf[..Unsafe.SizeOf<T>()], cancellationToken);
        return Unsafe.As<byte, T>(ref buf.Span[0]);
    }

    public static async ValueTask WriteAsync<T>(this Stream s, T val, Memory<byte> buf, CancellationToken cancellationToken = default)
        where T : unmanaged
    {
        if (buf.Length < Unsafe.SizeOf<T>())
        {
            throw new ArgumentException("must be large enough to hold one instance of the object.", nameof(buf));
        }

        Unsafe.As<byte, T>(ref buf.Span[0]) = val;
        await s.WriteAsync(buf[..Unsafe.SizeOf<T>()], cancellationToken);
    }

    public static ref readonly T Read<T>(this ref ReadOnlySpan<byte> buf)
        where T : unmanaged
    {
        if (buf.Length < Unsafe.SizeOf<T>())
        {
            throw new ArgumentException("must be large enough to hold one instance of the object.", nameof(buf));
        }

        ref readonly T result = ref Unsafe.As<byte, T>(ref MemoryMarshal.GetReference(buf));
        buf = buf[Unsafe.SizeOf<T>()..];
        return ref result;
    }

    public static void Write<T>(this ref Span<byte> buf, in T val)
        where T : unmanaged
    {
        if (buf.Length < Unsafe.SizeOf<T>())
        {
            throw new ArgumentException("must be large enough to hold one instance of the object.", nameof(buf));
        }

        Unsafe.As<byte, T>(ref MemoryMarshal.GetReference(buf)) = val;
        buf = buf[Unsafe.SizeOf<T>()..];
    }

    public static void Write<T>(this ref Span<T> buf, ReadOnlySpan<T> data)
    {
        if (buf.Length < data.Length)
        {
            throw new ArgumentException("must be large enough to hold the full data.", nameof(buf));
        }

        if (!data.TryCopyTo(buf))
        {
            throw new ArgumentException("must be large enough to hold the full data.", nameof(buf));
        }

        buf = buf[data.Length..];
    }

    public static void WriteCompleteUTF8(this ref Span<byte> buf, ReadOnlySpan<char> data)
    {
        buf = buf[Encoding.UTF8.GetBytes(data, buf)..];
    }
}
