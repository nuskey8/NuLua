using NuLua.Internal;
using NuLua.Interop.Luau;

namespace NuLua.Luau;

/// <summary>
/// Configures Luau's feature flags.
/// </summary>
public static unsafe partial class LuauFeatureFlags
{
    /// <summary>
    /// Sets a boolean feature flag.
    /// </summary>
    public static void SetBoolean(ReadOnlySpan<byte> flag, bool enabled)
    {
        using var utf8Name = new CString(flag);
        if (NativeMethods.luau_setbooleanflag(utf8Name.Pointer, enabled ? 1 : 0) == 0)
        {
            ThrowNotSupported(utf8Name.ToString());
        }
    }

    /// <summary>
    /// Reads a boolean feature flag.
    /// </summary>
    public static bool GetBoolean(ReadOnlySpan<byte> flag)
    {
        using var name = new CString(flag);
        int value;
        if (NativeMethods.luau_getbooleanflag(name.Pointer, &value) == 0)
        {
            ThrowNotSupported(name.ToString());
        }
        return value != 0;
    }

    /// <summary>
    /// Sets an integer feature flag.
    /// </summary>
    public static void SetInteger(ReadOnlySpan<byte> flag, int value)
    {
        using var name = new CString(flag);
        if (NativeMethods.luau_setintegerflag(name.Pointer, value) == 0)
        {
            ThrowNotSupported(name.ToString());
        }
    }

    /// <summary>
    /// Reads an integer feature flag.
    /// </summary>
    public static int GetInteger(ReadOnlySpan<byte> flag)
    {
        using var name = new CString(flag);
        int value;
        if (NativeMethods.luau_getintegerflag(name.Pointer, &value) == 0)
        {
            ThrowNotSupported(name.ToString());
        }
        return value;
    }

    static void ThrowNotSupported(string flag)
    {
        throw new NotSupportedException(
            $"The bundled Luau runtime does not support feature flag '{flag}'."
        );
    }
}
