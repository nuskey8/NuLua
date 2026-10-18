using System.Runtime.InteropServices;
using NuLua.Interop.Luau;

namespace NuLua.Luau;

public sealed unsafe partial class LuauState
{
    // Values mirror NuLuaOperationCode in the native Luau shim.
    enum MemoryOp
    {
        PushString = 0,
        NewTable = 1,
        NewThread = 2,
        NewUserData = 3,
        NewBuffer = 4,
        RawSet = 5,
        NewMetatable = 6,
        Reference = 7,
        OpenLibraries = 8,
        OpenLibrary = 9,
        Sandbox = 10,
        SandboxThread = 11,
        NewFunction = 12,
        ToString = 13,
        GarbageCollect = 14,
        GetGlobal = 15,
        SetGlobal = 16,
        GetTable = 17,
        SetTable = 18,
        GetField = 19,
        SetField = 20,
        Concat = 21,
        Compare = 22,
        Length = 23,
        Next = 24,
        Reserved = 25,
        Call = 26,
        Resume = 27,
        Breakpoint = 28,
        GetInfo = 29,
        GetLocal = 30,
        SetLocal = 31,
        GetUpvalue = 32,
        SetUpvalue = 33,
        GetArgument = 34,
    }

    bool? tracksMemory;
    int managedCallbackDepth;
    static readonly NativeMethods.lua_pushcclosurek_fn__delegate syncCallback = InvokeSync;
    static readonly NativeMethods.lua_pushcclosurek_fn__delegate asyncCallback = InvokeAsync;

    static int InvokeSync(lua_State* state) => InvokeHost(state, false);

    static int InvokeAsync(lua_State* state) => InvokeHost(state, true);

    static int InvokeHost(lua_State* pointer, bool async)
    {
        var main = ptrToState[(nint)NativeMethods.lua_mainthread(pointer)];
        main.managedCallbackDepth++;
        try
        {
            return async ? InvokeAsyncCore(pointer) : InvokeSyncCore(pointer);
        }
        catch (Exception exception)
        {
            main.interruptException ??=
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
            return -2;
        }
        finally
        {
            main.managedCallbackDepth--;
        }
    }

    static lua_State* CreateNativeState()
    {
        try
        {
            return (lua_State*)NativeMethods.luau_newtrackedstate();
        }
        catch (EntryPointNotFoundException)
        {
            return NativeMethods.luaL_newstate();
        }
    }

    static void CloseNativeState(lua_State* state)
    {
        try
        {
            NativeMethods.luau_closetrackedstate(state);
        }
        catch (EntryPointNotFoundException)
        {
            NativeMethods.lua_close(state);
        }
    }

    bool TracksMemory
    {
        get
        {
            if (tracksMemory.HasValue)
                return tracksMemory.Value;
            nuint used,
                limit;
            try
            {
                tracksMemory = NativeMethods.luau_memoryinfo(ptr, &used, &limit) != 0;
            }
            catch (EntryPointNotFoundException)
            {
                tracksMemory = false;
            }
            return tracksMemory.Value;
        }
    }

    bool ILuaMemoryControl.SupportsLimit
    {
        get
        {
            CheckDisposed();
            return TracksMemory;
        }
    }
    long ILuaMemoryControl.UsedBytes
    {
        get
        {
            CheckDisposed();
            if (!TracksMemory)
                return GarbageCollection.GetByteCount();
            nuint used,
                limit;
            NativeMethods.luau_memoryinfo(ptr, &used, &limit);
            return checked((long)used);
        }
    }

    long ILuaMemoryControl.SetLimit(long bytes)
    {
        CheckDisposed();
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes));
        if (!TracksMemory)
            throw new NotSupportedException("This Luau binary does not support allocator limits.");
        var callbacks = NativeMethods.lua_callbacks(ptr);
        if (
            bytes != 0
            && (
                callbacks->debugbreak != null
                || callbacks->debugstep != null
                || callbacks->debuginterrupt != null
            )
        )
            throw new NotSupportedException(
                "Allocator limits cannot be combined with debug callbacks."
            );
        return checked((long)NativeMethods.luau_setmemorylimit(ptr, checked((nuint)bytes)));
    }

    void CheckDebugBudget(bool installing)
    {
        if (!installing || !TracksMemory)
            return;
        nuint used,
            limit;
        NativeMethods.luau_memoryinfo(ptr, &used, &limit);
        if (limit != 0)
            throw new NotSupportedException(
                "Debug callbacks cannot be combined with allocator limits."
            );
    }

    int ProtectedLoad(byte* name, byte* data, nuint size)
    {
        if (TracksMemory)
            return NativeMethods.luau_pload(ptr, name, data, size);
        return NativeMethods.luau_load(ptr, name, data, size, 0) == 0 ? 0 : (int)LUA_ERRSYNTAX;
    }

    nint MemoryOperation(
        MemoryOp operation,
        int a = 0,
        int b = 0,
        void* data = null,
        nuint size = 0
    )
    {
        if (!TracksMemory)
            return LegacyOperation(operation, a, b, data, size);
        void* pointer;
        int integer;
        CheckResult(
            NativeMethods.luau_memoryoperation(
                ptr,
                (int)operation,
                a,
                b,
                data,
                size,
                &pointer,
                &integer
            )
        );
        return
            operation
                is MemoryOp.NewThread
                    or MemoryOp.NewUserData
                    or MemoryOp.NewBuffer
                    or MemoryOp.ToString
                    or MemoryOp.GetLocal
                    or MemoryOp.SetLocal
                    or MemoryOp.GetUpvalue
                    or MemoryOp.SetUpvalue
            ? (nint)pointer
            : integer;
    }

    nint LegacyOperation(MemoryOp op, int a, int b, void* data, nuint size)
    {
        switch (op)
        {
            case MemoryOp.PushString:
                NativeMethods.lua_pushlstring(ptr, (byte*)data, size);
                break;
            case MemoryOp.NewTable:
                NativeMethods.lua_createtable(ptr, a, b);
                break;
            case MemoryOp.NewThread:
                return (nint)NativeMethods.lua_newthread(ptr);
            case MemoryOp.NewUserData:
                return (nint)NativeMethods.lua_newuserdatatagged(ptr, size, 0);
            case MemoryOp.NewBuffer:
                return (nint)NativeMethods.lua_newbuffer(ptr, size);
            case MemoryOp.RawSet:
                NativeMethods.lua_rawset(ptr, a);
                break;
            case MemoryOp.NewMetatable:
                return NativeMethods.luaL_newmetatable(ptr, (byte*)data);
            case MemoryOp.Reference:
                return NativeMethods.lua_ref(ptr, a);
            case MemoryOp.OpenLibraries:
                NativeMethods.luaL_openlibs(ptr);
                break;
            case MemoryOp.OpenLibrary:
                NativeMethods.lua_pushcclosurek(
                    ptr,
                    Marshal.GetDelegateForFunctionPointer<NativeMethods.lua_pushcclosurek_fn__delegate>(
                        (nint)data
                    ),
                    null,
                    0,
                    null!
                );
                NativeMethods.lua_call(ptr, 0, 0);
                break;
            case MemoryOp.Sandbox:
                NativeMethods.luaL_sandbox(ptr);
                break;
            case MemoryOp.SandboxThread:
                NativeMethods.luaL_sandboxthread(ptr);
                break;
            case MemoryOp.NewFunction:
                NativeMethods.lua_pushcclosurek(
                    ptr,
                    b != 0 ? InvokeAsyncCore : InvokeSyncCore,
                    null,
                    a,
                    null!
                );
                break;
            case MemoryOp.ToString:
                return (nint)NativeMethods.lua_tolstring(ptr, a, &size);
            case MemoryOp.GarbageCollect:
                return NativeMethods.lua_gc(ptr, a, b);
            case MemoryOp.GetGlobal:
                CheckResult(NativeMethods.nulua_pgetglobal(ptr, (byte*)data));
                break;
            case MemoryOp.SetGlobal:
                CheckResult(NativeMethods.nulua_psetglobal(ptr, (byte*)data));
                break;
            case MemoryOp.GetTable:
                CheckResult(NativeMethods.nulua_pgettable(ptr, a));
                break;
            case MemoryOp.SetTable:
                CheckResult(NativeMethods.nulua_psettable(ptr, a));
                break;
            case MemoryOp.GetField:
                CheckResult(NativeMethods.nulua_pgetfield(ptr, a, (byte*)data));
                break;
            case MemoryOp.SetField:
                CheckResult(NativeMethods.nulua_psetfield(ptr, a, (byte*)data));
                break;
            case MemoryOp.Concat:
                CheckResult(NativeMethods.nulua_pconcat(ptr, a));
                break;
            case MemoryOp.Compare:
                CheckResult(NativeMethods.nulua_pcompare(ptr, a));
                break;
            case MemoryOp.Length:
                CheckResult(NativeMethods.nulua_plen(ptr, a));
                break;
            case MemoryOp.Next:
                int hasNext;
                CheckResult(NativeMethods.nulua_pnext(ptr, a, &hasNext));
                return hasNext;
            case MemoryOp.Call:
                CheckResult(NativeMethods.lua_pcall(ptr, a, b, 0));
                return 0;
            case MemoryOp.Resume:
                var result = NativeMethods.lua_resume(ptr, (lua_State*)data, a);
                CheckResult(result);
                return result;
            case MemoryOp.Breakpoint:
                return NativeMethods.lua_breakpoint(ptr, a, b, (int)size);
            case MemoryOp.GetInfo:
                return NativeMethods.lua_getinfo(ptr, a, (byte*)data, (lua_Debug*)size);
            case MemoryOp.GetLocal:
                return (nint)NativeMethods.lua_getlocal(ptr, a, b);
            case MemoryOp.SetLocal:
                return (nint)NativeMethods.lua_setlocal(ptr, a, b);
            case MemoryOp.GetUpvalue:
                return (nint)NativeMethods.lua_getupvalue(ptr, a, b);
            case MemoryOp.SetUpvalue:
                return (nint)NativeMethods.lua_setupvalue(ptr, a, b);
            case MemoryOp.GetArgument:
                return NativeMethods.lua_getargument(ptr, a, b);
            default:
                throw new ArgumentOutOfRangeException(nameof(op));
        }
        return 0;
    }
}
