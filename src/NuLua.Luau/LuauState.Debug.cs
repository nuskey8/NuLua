using System.Runtime.InteropServices;
using System.Text;
using NuLua.Interop.Luau;

namespace NuLua.Luau;

public sealed unsafe partial class LuauState : ILuauDebug
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void LuauDebugCallbackDelegate(lua_State* L, lua_Debug* ar);

    LuauDebugCallbackDelegate? debugBreakDelegate;
    LuauDebugCallbackDelegate? debugStepDelegate;
    LuauDebugCallbackDelegate? debugInterruptDelegate;

    LuaHook<LuauState>? debugBreakHook;
    LuaHook<LuauState>? debugStepHook;
    LuaHook<LuauState>? debugInterruptHook;

    int ILuaDebug.GetStackDepth()
    {
        CheckDisposed();
        return NativeMethods.lua_stackdepth(ptr);
    }

    bool ILuaDebug.TryGetStackInfo(int level, LuaDebugInfoFields fields, out LuaDebugInfo info)
    {
        CheckDisposed();

        scoped Span<byte> buffer = stackalloc byte[16];
        int len = 0;
        if ((fields & LuaDebugInfoFields.Name) != 0)
            buffer[len++] = (byte)'n';
        if ((fields & LuaDebugInfoFields.Source) != 0)
            buffer[len++] = (byte)'s';
        if ((fields & LuaDebugInfoFields.CurrentLine) != 0)
            buffer[len++] = (byte)'l';
        if ((fields & LuaDebugInfoFields.Upvalues) != 0)
        {
            buffer[len++] = (byte)'u';
            buffer[len++] = (byte)'a';
        }
        if ((fields & LuaDebugInfoFields.Function) != 0)
            buffer[len++] = (byte)'f';
        buffer[len] = 0;
        var what = buffer[..(len + 1)];

        lua_Debug ar = default;
        fixed (byte* whatPtr = what)
        {
            if (NativeMethods.lua_getinfo(ptr, level, whatPtr, &ar) == 0)
            {
                info = default;
                return false;
            }
        }
        info = ReadDebugInfo(&ar);
        return true;
    }

    string? ILuaDebug.GetLocal(int level, int n)
    {
        CheckDisposed();
        var name = NativeMethods.lua_getlocal(ptr, level, n);
        return name == null ? null : Utf8NullTerminated(name);
    }

    string? ILuaDebug.SetLocal(int level, int n)
    {
        CheckDisposed();
        var name = NativeMethods.lua_setlocal(ptr, level, n);
        return name == null ? null : Utf8NullTerminated(name);
    }

    string? ILuaDebug.GetUpvalue(int funcIndex, int n)
    {
        CheckDisposed();
        var name = NativeMethods.lua_getupvalue(ptr, funcIndex, n);
        return name == null ? null : Utf8NullTerminated(name);
    }

    string? ILuaDebug.SetUpvalue(int funcIndex, int n)
    {
        CheckDisposed();
        var name = NativeMethods.lua_setupvalue(ptr, funcIndex, n);
        return name == null ? null : Utf8NullTerminated(name);
    }

    nint ILuaDebug.UpvalueId(int funcIndex, int n) =>
        throw new NotSupportedException("Luau does not support UpvalueId()");

    void ILuaDebug.UpvalueJoin(int fIdx1, int n1, int fIdx2, int n2) =>
        throw new NotSupportedException("Luau does not support UpvalueJoin()");

    void ILuaDebug<LuauState>.SetHook(LuaHook<LuauState>? hook, LuaHookMask mask, int count) =>
        throw new NotSupportedException(
            "Luau does not support SetHook(). Use the Luauc debug callbacks instead."
        );

    void ILuaDebug.SetHook(LuaHook? hook, LuaHookMask mask, int count) =>
        throw new NotSupportedException(
            "Luau does not support SetHook(). Use the Luauc debug callbacks instead."
        );

    LuaHook<LuauState>? ILuaDebug<LuauState>.GetHook() => null;

    LuaHook? ILuaDebug.GetHook() => null;

    LuaHookMask ILuaDebug.GetHookMask() => LuaHookMask.None;

    int ILuaDebug.GetHookCount() => 0;

    int ILuauDebug.GetArgument(int level, int n)
    {
        CheckDisposed();
        return NativeMethods.lua_getargument(ptr, level, n);
    }

    void ILuauDebug.SetSingleStep(bool enabled)
    {
        CheckDisposed();
        NativeMethods.lua_singlestep(ptr, enabled ? 1 : 0);
    }

    int ILuauDebug.SetBreakpoint(int funcIndex, int line, bool enabled)
    {
        CheckDisposed();
        return NativeMethods.lua_breakpoint(ptr, funcIndex, line, enabled ? 1 : 0);
    }

    string ILuauDebug.GetDebugTrace()
    {
        CheckDisposed();
        var p = NativeMethods.lua_debugtrace(ptr);
        return Utf8NullTerminated(p) ?? string.Empty;
    }

    void ILuauDebug.GetCoverage(int funcIndex, Action<LuauCoverageEntry> visit)
    {
        CheckDisposed();
        if (visit == null)
            throw new ArgumentNullException(nameof(visit));

        var handle = GCHandle.Alloc(visit);
        try
        {
            NativeMethods.lua_getcoverage(
                ptr,
                funcIndex,
                (void*)GCHandle.ToIntPtr(handle),
                CoverageCallback
            );
        }
        finally
        {
            handle.Free();
        }
    }

    static void CoverageCallback(
        void* context,
        byte* function,
        int linedefined,
        int depth,
        int* hits,
        nuint size
    )
    {
        var handle = GCHandle.FromIntPtr((nint)context);
        if (handle.Target is not Action<LuauCoverageEntry> visit)
        {
            return;
        }

        var hitArray = new int[(int)size];
        for (int i = 0; i < hitArray.Length; i++)
        {
            hitArray[i] = hits[i];
        }

        visit(
            new LuauCoverageEntry
            {
                Function = Utf8NullTerminated(function),
                LineDefined = linedefined,
                Depth = depth,
                Hits = hitArray,
            }
        );
    }

    void ILuauDebug.SetDebugBreakCallback(LuaHook<LuauState>? callback)
    {
        CheckDisposed();
        debugBreakHook = callback;
        debugBreakDelegate = callback == null ? null : DebugBreakEntry;
        var callbacks = NativeMethods.lua_callbacks(ptr);
        callbacks->debugbreak =
            callback == null
                ? null
                : (void*)Marshal.GetFunctionPointerForDelegate(debugBreakDelegate!);
    }

    void ILuauDebug.SetDebugStepCallback(LuaHook<LuauState>? callback)
    {
        CheckDisposed();
        debugStepHook = callback;
        debugStepDelegate = callback == null ? null : DebugStepEntry;
        var callbacks = NativeMethods.lua_callbacks(ptr);
        callbacks->debugstep =
            callback == null
                ? null
                : (void*)Marshal.GetFunctionPointerForDelegate(debugStepDelegate!);
    }

    void ILuauDebug.SetDebugInterruptCallback(LuaHook<LuauState>? callback)
    {
        CheckDisposed();
        debugInterruptHook = callback;
        debugInterruptDelegate = callback == null ? null : DebugInterruptEntry;
        var callbacks = NativeMethods.lua_callbacks(ptr);
        callbacks->debuginterrupt =
            callback == null
                ? null
                : (void*)Marshal.GetFunctionPointerForDelegate(debugInterruptDelegate!);
    }

    static void DebugBreakEntry(lua_State* L, lua_Debug* ar)
    {
        if (!ptrToState.TryGetValue((nint)L, out var state))
            return;
        var hook = state.debugBreakHook;
        hook?.Invoke(state, LuaHookEvent.Line, ar->currentline);
    }

    static void DebugStepEntry(lua_State* L, lua_Debug* ar)
    {
        if (!ptrToState.TryGetValue((nint)L, out var state))
            return;
        var hook = state.debugStepHook;
        hook?.Invoke(state, LuaHookEvent.Line, ar->currentline);
    }

    static void DebugInterruptEntry(lua_State* L, lua_Debug* ar)
    {
        if (!ptrToState.TryGetValue((nint)L, out var state))
            return;
        var hook = state.debugInterruptHook;
        hook?.Invoke(state, LuaHookEvent.Line, ar->currentline);
    }

    static LuaDebugInfo ReadDebugInfo(lua_Debug* ar)
    {
        return new LuaDebugInfo
        {
            Name = Utf8NullTerminated(ar->name),
            NameWhat = null,
            What = Utf8NullTerminated(ar->what),
            Source = Utf8NullTerminated(ar->source),
            ShortSource = Utf8NullTerminated(ar->short_src),
            CurrentLine = ar->currentline,
            LineDefined = ar->linedefined,
            LastLineDefined = -1,
            Upvalues = ar->nupvals,
            Parameters = ar->nparams,
            IsVararg = ar->isvararg != 0,
            IsTailCall = false,
            FirstTransferred = 0,
            TransferredCount = 0,
        };
    }

    static string? Utf8NullTerminated(byte* p)
    {
        if (p == null)
            return null;
        int n = 0;
        while (p[n] != 0)
            n++;
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(p, n));
    }
}

public readonly struct LuauCoverageEntry
{
    public string? Function { get; init; }
    public int LineDefined { get; init; }
    public int Depth { get; init; }
    public int[] Hits { get; init; }
}
