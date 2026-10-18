using System.Diagnostics.CodeAnalysis;

namespace NuLua;

public interface ILuaState : ILuaObject
{
    ILuaState? From { get; }
    int RegistryIndex { get; }
    int UpvalueIndexBase { get; }

    LuaValue this[ReadOnlySpan<char> name] { get; set; }

    nint AsPointer();

    void OpenLibraries();

    int GetTop();
    void SetTop(int index);
    int GetAbsIndex(int index);
    LuaValueType GetType(int index);
    bool CheckStack(int n);

    void Copy(int fromIndex, int toIndex);
    void Rotate(int index, int n);
    void XMove(ILuaState target, int count);

    void PushNil();
    void PushBoolean(bool value);
    void PushInteger(long value);
    void PushNumber(double value);
    void PushString(ReadOnlySpan<byte> utf8Str);
    void PushLightUserData(nint data);
    bool PushThread();
    void PushValue(int index);
    void PushValue(LuaReference reference);

    bool ToBoolean(int index);
    double ToNumber(int index);
    long ToInteger(int index);
    string ToString(int index);
    nint ToUserDataPointer(int index);
    ILuaState ToThread(int index);
    LuaValue ToLuaValue(int index);

    void GetGlobal(ReadOnlySpan<char> name);
    void SetGlobal(ReadOnlySpan<char> name);

    void NewTable(int initialArraySize = 0, int initialRecordsSize = 0);
    void GetTable(int index);
    void SetTable(int index);
    void GetField(int index, ReadOnlySpan<char> name);
    void SetField(int index, ReadOnlySpan<char> name);
    void GetI(int index, long n);
    void SetI(int index, long n);
    void Next(int index);

    void NewUserData(int size, int userValueCount);
    bool TryGetUserValue(int index, int userValueIndex, out LuaValueType type);
    bool TrySetUserValue(int index, int userValueIndex, out LuaValueType type);

    void NewThread();

    void Arith(LuaArithmeticOperator op);
    void Compare(LuaComparisonOperator op);
    void Concat(int count);
    void Len(int index);
    void Call(int argCount, int returnCount);
    void Resume(int argCount);
    ValueTask ResumeAsync(int argCount, CancellationToken cancellationToken);

    bool RawEqual(int index1, int index2);
    LuaValueType RawGet(int index);
    int RawLen(int index);
    void RawSet(int index);

    bool TryGetMetatable(int index, [NotNullWhen(true)] out LuaTable? metatable);
    void SetMetatable(int index, LuaTable? metatable);
    bool NewMetatable(ReadOnlySpan<byte> utf8Name);

    LuaReference Ref(int index);
    void Unref(LuaReference reference);

    void LoadString(ReadOnlySpan<char> chunk, ReadOnlySpan<char> chunkName);
    void LoadString(ReadOnlySpan<byte> utf8Chunk, ReadOnlySpan<byte> utf8ChunkName);
    void LoadBuffer(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> utf8ChunkName);
    void LoadBuffer(ReadOnlySpan<byte> buffer, ReadOnlySpan<char> chunkName);
    bool TryDump(int index, bool strip, Span<byte> buffer, out int bytesWritten);

    ILuaDebug Debug { get; }
    ILuaGarbageCollection GarbageCollection { get; }
    ILuaMemoryControl Memory { get; }
}

public interface ILuaGarbageCollection
{
    void Stop();
    void Restart();
    void Collect();
    int Step(int stepSize);
    long GetByteCount();
    bool IsRunning();
}

public interface ILuaDebug
{
    int GetStackDepth();
    bool TryGetStackInfo(int level, LuaDebugInfoFields fields, out LuaDebugInfo info);
    string? GetLocal(int level, int n);
    string? SetLocal(int level, int n);
    string? GetUpvalue(int funcIndex, int n);
    string? SetUpvalue(int funcIndex, int n);
    nint UpvalueId(int funcIndex, int n);
    void UpvalueJoin(int fIdx1, int n1, int fIdx2, int n2);
    void SetHook(LuaHook? hook, LuaHookMask mask, int count);
    LuaHook? GetHook();
    LuaHookMask GetHookMask();
    int GetHookCount();
}

public interface ILuaDebug<T> : ILuaDebug
    where T : ILuaState<T>
{
    void SetHook(LuaHook<T>? hook, LuaHookMask mask, int count);
    new LuaHook<T>? GetHook();
}

public interface ILuaState<T> : ILuaState
    where T : ILuaState<T>
{
    new T? From { get; }
    new ILuaDebug<T> Debug { get; }

    void XMove(T target, int count);
    new T ToThread(int index);
    void NewFunction(LuaFunc<T> function, int upvalueCount);
    void NewFunction(AsyncLuaFunc<T> function, int upvalueCount);
}
