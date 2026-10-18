using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using NuLua.Internal;
using NuLua.Interop.Luau;

namespace NuLua.Luau;

public sealed unsafe partial class LuauState
{
    // Luau does not export LUA_OK / LUA_YIELD / LUA_ERR* / LUA_T* through bindgen,
    // so we mirror the values declared in submodules/luau/VM/include/lua.h here.
    const int LUA_OK = 0;
    const int LUA_YIELD = 1;
    const uint LUA_ERRRUN = 2;
    const uint LUA_ERRSYNTAX = 3;
    const uint LUA_ERRMEM = 4;
    const uint LUA_ERRERR = 5;

    const uint LUA_TNIL = 0;
    const uint LUA_TBOOLEAN = 1;
    const uint LUA_TLIGHTUSERDATA = 2;
    const uint LUA_TNUMBER = 3;
    const uint LUA_TINTEGER = 4;
    const uint LUA_TVECTOR = 5;
    const uint LUA_TSTRING = 6;
    const uint LUA_TTABLE = 7;
    const uint LUA_TFUNCTION = 8;
    const uint LUA_TUSERDATA = 9;
    const uint LUA_TTHREAD = 10;
    const uint LUA_TBUFFER = 11;
    const uint LUA_TCLASS = 12;
    const uint LUA_TOBJECT = 13;

    public static LuauState CreateSandbox()
    {
        var state = Create();
        state.Sandbox();
        return state;
    }

    /// <summary>
    /// Freezes this VM's shared globals and libraries after host initialization.
    /// Run scripts in <see cref="CreateSandboxThread"/> to give each worker writable,
    /// isolated globals. This operation is permanent and may only be called on the main state.
    /// Existing threads and loaded chunks are not isolated retroactively.
    /// </summary>
    public void Sandbox()
    {
        CheckDisposed();
        if (NativeMethods.lua_mainthread(ptr) != ptr)
        {
            throw new InvalidOperationException("Only the main state can freeze shared globals.");
        }
        NativeMethods.luaL_sandbox(ptr);
    }

    public void NewSandboxThread()
    {
        NewThread();
        var thread = ToThread(-1);
        NativeMethods.luaL_sandboxthread(thread.ptr);
    }

    public LuauState CreateSandboxThread()
    {
        NewThread();
        var thread = ToThread(-1);
        NativeMethods.luaL_sandboxthread(thread.ptr);
        return thread;
    }

    static LuauState GetMainState(lua_State* L)
    {
        var state = ptrToState[(nint)L];
        while (state.from != null)
        {
            state = state.from;
        }
        return state;
    }

    void CheckResult(int code)
    {
        if (code is not LUA_OK and not LUA_YIELD)
        {
            nuint len;
            var message = NativeMethods.lua_tolstring(ptr, -1, &len);
            var messageStr =
                message == null ? string.Empty : new string((sbyte*)message, 0, (int)len);
            throw new LuaException((uint)code, messageStr);
        }
    }

    public void OpenLibraries()
    {
        CheckDisposed();
        NativeMethods.luaL_openlibs(ptr);
    }

    void OpenSingleLibrary(NativeMethods.lua_pushcclosurek_fn__delegate opener)
    {
        NativeMethods.lua_pushcclosurek(ptr, opener, null, 0, null!);
        NativeMethods.lua_call(ptr, 0, 0);
    }

    public void OpenBaseLibrary()
    {
        CheckDisposed();
        OpenSingleLibrary(NativeMethods.luaopen_base);
    }

    public void OpenCoroutineLibrary()
    {
        CheckDisposed();
        OpenSingleLibrary(NativeMethods.luaopen_coroutine);
    }

    public void OpenTableLibrary()
    {
        CheckDisposed();
        OpenSingleLibrary(NativeMethods.luaopen_table);
    }

    public void OpenStringLibrary()
    {
        CheckDisposed();
        OpenSingleLibrary(NativeMethods.luaopen_string);
    }

    public void OpenMathLibrary()
    {
        CheckDisposed();
        OpenSingleLibrary(NativeMethods.luaopen_math);
    }

    public void OpenOsLibrary()
    {
        CheckDisposed();
        OpenSingleLibrary(NativeMethods.luaopen_os);
    }

    public void OpenDebugLibrary()
    {
        CheckDisposed();
        OpenSingleLibrary(NativeMethods.luaopen_debug);
    }

    public void OpenBit32Library()
    {
        CheckDisposed();
        OpenSingleLibrary(NativeMethods.luaopen_bit32);
    }

    public void OpenBufferLibrary()
    {
        CheckDisposed();
        OpenSingleLibrary(NativeMethods.luaopen_buffer);
    }

    public void OpenUtf8Library()
    {
        CheckDisposed();
        OpenSingleLibrary(NativeMethods.luaopen_utf8);
    }

    public void OpenClassLibrary()
    {
        CheckDisposed();
        OpenSingleLibrary(NativeMethods.luaopen_class);
    }

    public void OpenVectorLibrary()
    {
        CheckDisposed();
        OpenSingleLibrary(NativeMethods.luaopen_vector);
    }

    public void OpenIntegerLibrary()
    {
        CheckDisposed();
        OpenSingleLibrary(NativeMethods.luaopen_integer);
    }

    public void LoadString(ReadOnlySpan<byte> utf8Code, ReadOnlySpan<byte> utf8ChunkName)
    {
        using var chunkName = new CString(utf8ChunkName);
        LoadStringCore(utf8Code, chunkName);
    }

    public void LoadString(ReadOnlySpan<char> code, ReadOnlySpan<char> chunkName)
    {
        var codeBuffer = ArrayPool<byte>.Shared.Rent(code.Length * 3);
        try
        {
            var codeBytes = Encoding.UTF8.GetBytes(code, codeBuffer);
            using var chunkNameBytes = new CString(chunkName);
            LoadStringCore(codeBuffer.AsSpan(0, codeBytes), chunkNameBytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(codeBuffer);
        }
    }

    void LoadStringCore(ReadOnlySpan<byte> utf8Code, CString chunkName)
    {
        CheckDisposed();
        nuint bytecodeSize;
        byte* bytecode;
        fixed (byte* codePtr = utf8Code)
        {
            bytecode = NativeMethods.luau_compile(
                codePtr,
                (nuint)utf8Code.Length,
                null,
                &bytecodeSize
            );
        }
        if (bytecode == null)
        {
            throw new LuaException(LUA_ERRMEM, "luau_compile returned null.");
        }
        try
        {
            var result = NativeMethods.luau_load(ptr, chunkName.Pointer, bytecode, bytecodeSize, 0);
            CheckResult(result);
        }
        finally
        {
            NativeMethods.luau_free(bytecode);
        }
    }

    public void LoadBuffer(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> utf8ChunkName)
    {
        using var chunkName = new CString(utf8ChunkName);
        LoadBufferCore(buffer, chunkName);
    }

    public void LoadBuffer(ReadOnlySpan<byte> buffer, ReadOnlySpan<char> chunkName)
    {
        using var chunkNameBytes = new CString(chunkName);
        LoadBuffer(buffer, chunkNameBytes.AsSpan());
    }

    void LoadBufferCore(ReadOnlySpan<byte> buffer, CString chunkName)
    {
        CheckDisposed();
        fixed (byte* bufferPtr = buffer)
        {
            var result = NativeMethods.luau_load(
                ptr,
                chunkName.Pointer,
                bufferPtr,
                (nuint)buffer.Length,
                0
            );
            CheckResult(result);
        }
    }

    public bool TryDump(int index, bool strip, Span<byte> buffer, out int bytesWritten)
    {
        // Luau has no lua_dump equivalent
        _ = index;
        _ = strip;
        _ = buffer;
        bytesWritten = 0;
        throw new NotSupportedException("Dumping functions is not supported on Luau.");
    }

    public int GetAbsIndex(int index)
    {
        CheckDisposed();
        return NativeMethods.lua_absindex(ptr, index);
    }

    public void Copy(int fromIndex, int toIndex)
    {
        CheckDisposed();
        // Luau lacks lua_copy; emulate via push-then-replace.
        var absTo = NativeMethods.lua_absindex(ptr, toIndex);
        NativeMethods.lua_pushvalue(ptr, fromIndex);
        NativeMethods.lua_replace(ptr, absTo);
    }

    public void Rotate(int index, int n)
    {
        CheckDisposed();
        // Luau lacks lua_rotate; fall back to insert/remove patterns matching Lua 5.1.
        if (n == 1)
        {
            NativeMethods.lua_insert(ptr, index);
        }
        else if (n == -1)
        {
            NativeMethods.lua_pushvalue(ptr, index);
            NativeMethods.lua_remove(ptr, index < 0 ? index - 1 : index);
        }
        else
        {
            throw new NotSupportedException("lua_rotate with |n| > 1 is not supported on Luau.");
        }
    }

    public void PushInteger(long value)
    {
        CheckDisposed();
        NativeMethods.lua_pushinteger(ptr, (int)value);
    }

    public void PushLightUserData(nint data)
    {
        CheckDisposed();
        NativeMethods.lua_pushlightuserdatatagged(ptr, (void*)data, 0);
    }

    public void PushVector(Vector3 value)
    {
        CheckDisposed();
        NativeMethods.lua_pushvector(ptr, value.X, value.Y, value.Z);
    }

    public Vector3 ToVector(int index)
    {
        var span = ToVectorSpan(index);
        return new Vector3(span[0], span[1], span[2]);
    }

    public Span<float> ToVectorSpan(int index)
    {
        CheckDisposed();
        var p = NativeMethods.lua_tovector(ptr, index);
        if (p == null)
        {
            throw new InvalidOperationException("Value at the specified index is not a vector.");
        }
        return new Span<float>(p, 3);
    }

    public LuauBuffer NewBuffer(int size)
    {
        CheckDisposed();
        _ = NativeMethods.lua_newbuffer(ptr, (nuint)size);
        return new LuauBuffer(this, this.Ref());
    }

    public LuauBuffer ToBuffer(int index)
    {
        CheckDisposed();
        if (GetType(index) != LuaValueType.Buffer)
        {
            throw new InvalidOperationException("Value at the specified index is not a buffer.");
        }
        NativeMethods.lua_pushvalue(ptr, index);
        return new LuauBuffer(this, this.Ref());
    }

    public Span<byte> ToBufferSpan(int index)
    {
        CheckDisposed();
        nuint len;
        var p = NativeMethods.lua_tobuffer(ptr, index, &len);
        if (p == null)
        {
            throw new InvalidOperationException("Value at the specified index is not a buffer.");
        }
        return new Span<byte>((byte*)p, (int)len);
    }

    public LuauClass ToClass(int index)
    {
        CheckDisposed();
        if (GetType(index) != LuaValueType.Class)
        {
            throw new InvalidOperationException("Value at the specified index is not a class.");
        }
        NativeMethods.lua_pushvalue(ptr, index);
        return new LuauClass(this, this.Ref());
    }

    public LuauObject ToObject(int index)
    {
        CheckDisposed();
        if (GetType(index) != LuaValueType.Object)
        {
            throw new InvalidOperationException("Value at the specified index is not an object.");
        }
        NativeMethods.lua_pushvalue(ptr, index);
        return new LuauObject(this, this.Ref());
    }

    public LuaValue ToLuaValue(int index)
    {
        CheckDisposed();
        var type = GetType(index);
        switch (type)
        {
            case LuaValueType.Nil:
                return LuaValue.Nil;
            case LuaValueType.Boolean:
                return ToBoolean(index);
            case LuaValueType.Number:
                return ToNumber(index);
            case LuaValueType.String:
                return ToString(index);
            case LuaValueType.Vector:
                return LuaValue.FromVector(ToVector(index));
            case LuaValueType.Buffer:
                return LuaValue.FromBuffer(ToBuffer(index));
            case LuaValueType.Class:
                return LuaValue.FromClass(ToClass(index));
            case LuaValueType.Object:
                return LuaValue.FromObject(ToObject(index));
            case LuaValueType.Table:
            {
                PushValue(index);
                var reference = this.Ref();
                return new LuaTable(this, reference);
            }
            case LuaValueType.Function:
            {
                PushValue(index);
                var reference = this.Ref();
                return new LuaFunction(this, reference);
            }
            case LuaValueType.Thread:
            {
                PushValue(index);
                var reference = this.Ref();
                PushValue(reference);
                var thread = ToThread(-1);
                this.Pop(1);
                return LuaValue.FromThread(thread);
            }
            case LuaValueType.UserData:
            {
                PushValue(index);
                var reference = this.Ref();
                return new LuaUserData(this, reference);
            }
            default:
                throw new NotSupportedException($"Unsupported Lua value type: {type}");
        }
    }

    public void Push(LuaValue value)
    {
        CheckDisposed();
        switch (value.Type)
        {
            case LuaValueType.Vector:
                PushVector(value.UnsafeRead<Vector3>());
                return;
            case LuaValueType.Buffer:
            case LuaValueType.Class:
            case LuaValueType.Object:
                PushValue(value.UnsafeRead<ILuaObject>().Reference);
                return;
            default:
                ((ILuaState)this).Push(value);
                return;
        }
    }

    public void PushValue(LuaReference reference)
    {
        CheckDisposed();
        NativeMethods.lua_rawgeti(ptr, reference.TableIndex, reference.Id);
    }

    public double ToNumber(int index)
    {
        CheckDisposed();
        return NativeMethods.lua_tonumberx(ptr, index, null);
    }

    public long ToInteger(int index)
    {
        CheckDisposed();
        return NativeMethods.lua_tointegerx(ptr, index, null);
    }

    public string ToString(int index)
    {
        CheckDisposed();
        nuint len;
        var strPtr = NativeMethods.lua_tolstring(ptr, index, &len);
        if (strPtr == null)
        {
            return string.Empty;
        }
        return new string((sbyte*)strPtr, 0, (int)len);
    }

    public void GetGlobal(ReadOnlySpan<char> name)
    {
        CheckDisposed();
        using var nameBytes = new CString(name);
        CheckResult(NativeMethods.nulua_pgetglobal(ptr, nameBytes.Pointer));
    }

    public void SetGlobal(ReadOnlySpan<char> name)
    {
        CheckDisposed();
        using var nameBytes = new CString(name);
        CheckResult(NativeMethods.nulua_psetglobal(ptr, nameBytes.Pointer));
    }

    public void GetTable(int index)
    {
        CheckDisposed();
        CheckResult(NativeMethods.nulua_pgettable(ptr, index));
    }

    public void GetField(int index, ReadOnlySpan<char> name)
    {
        CheckDisposed();
        using var nameBytes = new CString(name);
        CheckResult(NativeMethods.nulua_pgetfield(ptr, index, nameBytes.Pointer));
    }

    public void SetField(int index, ReadOnlySpan<char> name)
    {
        CheckDisposed();
        using var nameBytes = new CString(name);
        CheckResult(NativeMethods.nulua_psetfield(ptr, index, nameBytes.Pointer));
    }

    public void GetI(int index, long n)
    {
        CheckDisposed();
        var absIndex = NativeMethods.lua_absindex(ptr, index);
        NativeMethods.lua_pushinteger(ptr, (int)n);
        CheckResult(NativeMethods.nulua_pgettable(ptr, absIndex));
    }

    public void SetI(int index, long n)
    {
        CheckDisposed();
        var absIndex = NativeMethods.lua_absindex(ptr, index);
        NativeMethods.lua_pushinteger(ptr, (int)n);
        NativeMethods.lua_insert(ptr, -2);
        CheckResult(NativeMethods.nulua_psettable(ptr, absIndex));
    }

    public void NewUserData(int size, int userValueCount)
    {
        CheckDisposed();
        if (userValueCount > 1)
        {
            throw new NotSupportedException("Multiple user values are not supported on Luau.");
        }
        NativeMethods.lua_newuserdatatagged(ptr, (nuint)size, 0);
    }

    public bool TryGetUserValue(int index, int userValueIndex, out LuaValueType type)
    {
        CheckDisposed();
        if (userValueIndex != 1)
        {
            type = default;
            return false;
        }
        NativeMethods.lua_getfenv(ptr, index);
        type = CodeToType((uint)NativeMethods.lua_type(ptr, -1));
        return true;
    }

    public bool TrySetUserValue(int index, int userValueIndex, out LuaValueType type)
    {
        CheckDisposed();
        if (userValueIndex != 1)
        {
            type = default;
            return false;
        }
        type = CodeToType((uint)NativeMethods.lua_type(ptr, -1));
        var result = NativeMethods.lua_setfenv(ptr, index);
        if (result == 0)
        {
            throw new InvalidOperationException(
                "lua_setfenv failed; value at the specified index does not accept an environment."
            );
        }
        return true;
    }

    public void NewFunction(LuaFunc<LuauState> func, int upvalueCount)
    {
        static int Fn(lua_State* L)
        {
            var state = GetOrCreate(L, default);
            var main = GetMainState(L);
            var funcIndex = NativeMethods.lua_tointegerx(
                L,
                NativeMethods.LUA_GLOBALSINDEX - 1,
                null
            );
            var func = main.funcs[funcIndex];

            var numArgs = NativeMethods.lua_gettop(L);
            return func(state, new LuaFuncArguments(state, numArgs));
        }

        CheckDisposed();

        var top = NativeMethods.lua_gettop(ptr);
        if ((uint)upvalueCount > (uint)top || upvalueCount >= byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(upvalueCount));
        }

        var funcIndex = funcs.Count;
        funcs.Add(func);
        NativeMethods.lua_pushinteger(ptr, funcIndex);
        NativeMethods.lua_insert(ptr, -upvalueCount - 1);
        NativeMethods.lua_pushcclosurek(ptr, Fn, null, upvalueCount + 1, null!);
    }

    public void NewFunction(AsyncLuaFunc<LuauState> func, int upvalueCount)
    {
        static int AsyncCFn(lua_State* L)
        {
            var state = GetOrCreate(L, default);
            var main = GetMainState(L);

            if (NativeMethods.lua_isyieldable(L) == 0)
            {
                ReadOnlySpan<byte> errMsg =
                    "attempt to call async function from a non-yieldable context\0"u8;
                return NativeMethods.luau_error(
                    L,
                    (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(errMsg))
                );
            }

            var funcIndex = NativeMethods.lua_tointegerx(
                L,
                NativeMethods.LUA_GLOBALSINDEX - 1,
                null
            );
            var func = main.asyncFuncs[funcIndex];

            var numArgs = NativeMethods.lua_gettop(L);
            var snapshot = numArgs == 0 ? [] : new LuaValue[numArgs];
            for (int i = 0; i < numArgs; i++)
            {
                snapshot[i] = state.ToLuaValue(i + 1);
            }
            var args = new LuaFuncArguments(snapshot, numArgs);
            var ct = state.asyncCancellationToken;

            var task = func(state, args, ct);

            if (task.IsCompletedSuccessfully)
            {
                return task.Result;
            }

            state.pendingAsyncTask = task;
            state.hasPendingTask = true;
            NativeMethods.lua_settop(L, 0);
            // Luau does not support continuation-aware yields through lua_pushcclosurek
            // (the cont parameter is only invoked for protected-call continuations).
            // The async driver pushes the awaited results back onto the stack and
            // re-resumes the coroutine; those results become the yield expression's
            // value, which is exactly what we want.
            return NativeMethods.lua_yield(L, 0);
        }

        CheckDisposed();

        var top = NativeMethods.lua_gettop(ptr);
        if ((uint)upvalueCount > (uint)top || upvalueCount >= byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(upvalueCount));
        }

        var funcIndex = asyncFuncs.Count;
        asyncFuncs.Add(func);
        NativeMethods.lua_pushinteger(ptr, funcIndex);
        NativeMethods.lua_insert(ptr, -upvalueCount - 1);
        NativeMethods.lua_pushcclosurek(ptr, AsyncCFn, null, upvalueCount + 1, null!);
    }

    static readonly byte[]?[] arithBytecodeCache = new byte[(int)LuaArithmeticOperator.Shr + 1][];

    public void Arith(LuaArithmeticOperator op)
    {
        CheckDisposed();
        if (TryArithBit32(op))
        {
            return;
        }

        // Luau does not expose lua_arith; emulate by calling a per-operator
        // Lua snippet so that metamethods participate. The Luau-compiled
        // bytecode is cached after the first call.
        ReadOnlySpan<byte> source;
        int argCount;
        switch (op)
        {
            case LuaArithmeticOperator.Add:
                source = "local a,b=...;return a+b"u8;
                argCount = 2;
                break;
            case LuaArithmeticOperator.Sub:
                source = "local a,b=...;return a-b"u8;
                argCount = 2;
                break;
            case LuaArithmeticOperator.Mul:
                source = "local a,b=...;return a*b"u8;
                argCount = 2;
                break;
            case LuaArithmeticOperator.Div:
                source = "local a,b=...;return a/b"u8;
                argCount = 2;
                break;
            case LuaArithmeticOperator.IDiv:
                source = "local a,b=...;return a//b"u8;
                argCount = 2;
                break;
            case LuaArithmeticOperator.Mod:
                source = "local a,b=...;return a%b"u8;
                argCount = 2;
                break;
            case LuaArithmeticOperator.Pow:
                source = "local a,b=...;return a^b"u8;
                argCount = 2;
                break;
            case LuaArithmeticOperator.Unm:
                source = "local a=...;return -a"u8;
                argCount = 1;
                break;
            default:
                throw new NotSupportedException($"Arith operator '{op}' is not supported on Luau.");
        }

        var bytecode = arithBytecodeCache[(int)op];
        if (bytecode == null)
        {
            nuint bytecodeSize;
            byte* bytecodePtr;
            fixed (byte* codePtr = source)
            {
                bytecodePtr = NativeMethods.luau_compile(
                    codePtr,
                    (nuint)source.Length,
                    null,
                    &bytecodeSize
                );
            }
            if (bytecodePtr == null)
            {
                throw new LuaException(LUA_ERRMEM, "luau_compile returned null.");
            }
            try
            {
                bytecode = new ReadOnlySpan<byte>(bytecodePtr, (int)bytecodeSize).ToArray();
            }
            finally
            {
                NativeMethods.luau_free(bytecodePtr);
            }
            arithBytecodeCache[(int)op] = bytecode;
        }

        using var chunkName = new CString("arith"u8);
        fixed (byte* bcPtr = bytecode)
        {
            var loadResult = NativeMethods.luau_load(
                ptr,
                chunkName.Pointer,
                bcPtr,
                (nuint)bytecode.Length,
                0
            );
            CheckResult(loadResult);
        }

        // The chunk is on top; insert it below the operands so the call
        // pattern becomes [..., func, a, (b)].
        NativeMethods.lua_insert(ptr, -1 - argCount);

        var callResult = NativeMethods.lua_pcall(ptr, argCount, 1, 0);
        CheckResult(callResult);
    }

    bool TryArithBit32(LuaArithmeticOperator op)
    {
        static int NormalizeShiftCount(long value) => (int)(value & 31);

        switch (op)
        {
            case LuaArithmeticOperator.BNot:
            {
                var a = (uint)ToInteger(-1);
                NativeMethods.lua_settop(ptr, NativeMethods.lua_gettop(ptr) - 1);
                PushInteger((long)(uint)~a);
                return true;
            }
            case LuaArithmeticOperator.BAnd:
            {
                var b = (uint)ToInteger(-1);
                var a = (uint)ToInteger(-2);
                NativeMethods.lua_settop(ptr, NativeMethods.lua_gettop(ptr) - 2);
                PushInteger((long)(a & b));
                return true;
            }
            case LuaArithmeticOperator.BOr:
            {
                var b = (uint)ToInteger(-1);
                var a = (uint)ToInteger(-2);
                NativeMethods.lua_settop(ptr, NativeMethods.lua_gettop(ptr) - 2);
                PushInteger((long)(a | b));
                return true;
            }
            case LuaArithmeticOperator.BXor:
            {
                var b = (uint)ToInteger(-1);
                var a = (uint)ToInteger(-2);
                NativeMethods.lua_settop(ptr, NativeMethods.lua_gettop(ptr) - 2);
                PushInteger((long)(a ^ b));
                return true;
            }
            case LuaArithmeticOperator.Shl:
            {
                var b = NormalizeShiftCount(ToInteger(-1));
                var a = (uint)ToInteger(-2);
                NativeMethods.lua_settop(ptr, NativeMethods.lua_gettop(ptr) - 2);
                PushInteger((long)(a << b));
                return true;
            }
            case LuaArithmeticOperator.Shr:
            {
                var b = NormalizeShiftCount(ToInteger(-1));
                var a = (uint)ToInteger(-2);
                NativeMethods.lua_settop(ptr, NativeMethods.lua_gettop(ptr) - 2);
                PushInteger((long)(a >> b));
                return true;
            }
            default:
                return false;
        }
    }

    public void Compare(LuaComparisonOperator op)
    {
        CheckDisposed();
        var nativeOp = op switch
        {
            LuaComparisonOperator.Equal => 0,
            LuaComparisonOperator.Less => 1,
            LuaComparisonOperator.LessOrEqual => 2,
            _ => throw new NotSupportedException($"Unsupported Lua comparison operator: {op}"),
        };
        CheckResult(NativeMethods.nulua_pcompare(ptr, nativeOp));
    }

    public void Len(int index)
    {
        CheckDisposed();
        CheckResult(NativeMethods.nulua_plen(ptr, index));
    }

    public void Call(int argCount, int returnCount)
    {
        CheckDisposed();
        var result = NativeMethods.lua_pcall(ptr, argCount, returnCount, 0);
        CheckResult(result);
    }

    public void Next(int index)
    {
        CheckDisposed();
        int hasNext;
        CheckResult(NativeMethods.nulua_pnext(ptr, index, &hasNext));
    }

    public LuaValueType RawGet(int index)
    {
        CheckDisposed();
        NativeMethods.lua_rawget(ptr, index);
        var t = (uint)NativeMethods.lua_type(ptr, -1);
        return CodeToType(t);
    }

    public int RawLen(int index)
    {
        CheckDisposed();
        return NativeMethods.lua_objlen(ptr, index);
    }

    public void Resume(int argCount)
    {
        CheckDisposed();
        var result = NativeMethods.lua_resume(ptr, from == null ? null : from.ptr, argCount);
        CheckResult(result);
    }

    internal int RunResumeStep(int argCount)
    {
        var status = NativeMethods.lua_resume(ptr, from == null ? null : from.ptr, argCount);
        if (status != LUA_OK && status != LUA_YIELD)
        {
            nuint len;
            var message = NativeMethods.lua_tolstring(ptr, -1, &len);
            var messageStr =
                message == null ? string.Empty : new string((sbyte*)message, 0, (int)len);
            throw new LuaException((uint)status, messageStr);
        }
        return status;
    }

    public LuaReference Ref(int index)
    {
        CheckDisposed();
        // Luau's `lua_ref` behaves differently than in standard Lua.
        // ref: https://github.com/luau-lang/luau/issues/247
        var refId = NativeMethods.lua_ref(
            ptr,
            index == NativeMethods.LUA_REGISTRYINDEX ? -1 : index
        );
        if (refId == NativeMethods.LUA_NOREF)
        {
            throw new LuaException(LUA_ERRMEM, "Failed to create reference.");
        }
        if (index == NativeMethods.LUA_REGISTRYINDEX)
        {
            this.Pop(1);
        }
        return new LuaReference(refId, NativeMethods.LUA_REGISTRYINDEX);
    }

    public void Unref(LuaReference reference)
    {
        CheckDisposed();
        NativeMethods.lua_unref(ptr, reference.Id);
    }

    static LuaValueType CodeToType(uint code)
    {
        return code switch
        {
            LUA_TNIL => LuaValueType.Nil,
            LUA_TBOOLEAN => LuaValueType.Boolean,
            LUA_TLIGHTUSERDATA => LuaValueType.LightUserData,
            LUA_TNUMBER => LuaValueType.Number,
            LUA_TINTEGER => LuaValueType.Number,
            LUA_TSTRING => LuaValueType.String,
            LUA_TTABLE => LuaValueType.Table,
            LUA_TFUNCTION => LuaValueType.Function,
            LUA_TUSERDATA => LuaValueType.UserData,
            LUA_TTHREAD => LuaValueType.Thread,
            LUA_TVECTOR => LuaValueType.Vector,
            LUA_TBUFFER => LuaValueType.Buffer,
            LUA_TCLASS => LuaValueType.Class,
            LUA_TOBJECT => LuaValueType.Object,
            _ => throw new NotSupportedException($"Unsupported Lua type code: {code}"),
        };
    }
}
