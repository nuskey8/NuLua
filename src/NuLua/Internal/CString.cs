using System.Runtime.InteropServices;
using System.Text;

namespace NuLua.Internal;

public unsafe struct CString : IDisposable
{
    byte* ptr;
    readonly int byteCount;

    public CString(ReadOnlySpan<char> str)
    {
        var byteCount = Encoding.UTF8.GetByteCount(str);
        ptr = (byte*)Marshal.AllocHGlobal(byteCount + 1);
        fixed (char* strPtr = str)
        {
            Encoding.UTF8.GetBytes(strPtr, str.Length, ptr, byteCount);
        }
        ptr[byteCount] = 0;
        this.byteCount = byteCount;
    }

    public CString(ReadOnlySpan<byte> utf8Bytes)
    {
        ptr = (byte*)Marshal.AllocHGlobal(utf8Bytes.Length + 1);
        utf8Bytes.CopyTo(new Span<byte>(ptr, utf8Bytes.Length));
        ptr[utf8Bytes.Length] = 0;
        byteCount = utf8Bytes.Length;
    }

    public byte* Pointer => ptr;

    public readonly ReadOnlySpan<byte> AsSpan()
    {
        if (ptr == null)
            return [];
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
}
