using System.Runtime.InteropServices;
using System.Text;

namespace NuLua.Internal;

public unsafe struct CString : IDisposable
{
    byte* ptr;
    readonly int byteCount;

    public CString(ReadOnlySpan<char> str)
        : this(str, true) { }

    CString(ReadOnlySpan<char> str, bool validate)
    {
        if (validate)
        {
            ValidateNonNull(str);
        }

        var byteCount = Encoding.UTF8.GetByteCount(str);
        ptr = (byte*)Marshal.AllocHGlobal(byteCount + 1);
        Encoding.UTF8.GetBytes(str, new Span<byte>(ptr, byteCount));
        ptr[byteCount] = 0;
        this.byteCount = byteCount;
    }

    public CString(ReadOnlySpan<byte> utf8Bytes)
        : this(utf8Bytes, true) { }

    CString(ReadOnlySpan<byte> utf8Bytes, bool validate)
    {
        if (validate)
        {
            ValidateNonNull(utf8Bytes);
        }

        ptr = (byte*)Marshal.AllocHGlobal(utf8Bytes.Length + 1);
        utf8Bytes.CopyTo(new Span<byte>(ptr, utf8Bytes.Length));
        ptr[utf8Bytes.Length] = 0;
        byteCount = utf8Bytes.Length;
    }

    public static CString CreateUnchecked(ReadOnlySpan<char> str) => new(str, false);

    public static CString CreateUnchecked(ReadOnlySpan<byte> utf8Bytes) => new(utf8Bytes, false);

    public byte* Pointer => ptr;

    public readonly ReadOnlySpan<byte> AsSpan()
    {
        if (ptr == null) return [];
        return new ReadOnlySpan<byte>(ptr, byteCount);
    }

    public override readonly string ToString()
    {
        if (ptr == null)
            return string.Empty;
        return Encoding.UTF8.GetString(ptr, byteCount);
    }

    public void Dispose()
    {
        if (ptr != null)
        {
            Marshal.FreeHGlobal((IntPtr)ptr);
            ptr = null;
        }
    }

    static void ValidateNonNull(ReadOnlySpan<byte> utf8Bytes)
    {
        if (utf8Bytes.IndexOf((byte)0) >= 0)
        {
            throw new ArgumentException("CString must not contain NUL bytes.", nameof(utf8Bytes));
        }
    }

    static void ValidateNonNull(ReadOnlySpan<char> str)
    {
        if (str.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("CString must not contain NUL characters.", nameof(str));
        }
    }
}
