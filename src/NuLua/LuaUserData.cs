using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NuLua;

public sealed class LuaUserData(ILuaState state, LuaReference reference) : ILuaObject
{
    readonly ILuaState state = state;

    public readonly struct UserValues(LuaUserData userData)
    {
        public LuaValue this[int index]
        {
            get
            {
                userData.state.PushValue(userData.Reference);
                if (!userData.state.TryGetUserValue(-1, index, out _))
                {
                    userData.state.Pop(1);
                    ThrowIndexOutOfRange(index);
                }
                var value = userData.state.Pop();
                return value;
            }
            set
            {
                userData.state.PushValue(userData.Reference);
                userData.state.Push(value);
                if (!userData.state.TrySetUserValue(-2, index, out _))
                {
                    userData.state.Pop(2);
                    ThrowIndexOutOfRange(index);
                }
                userData.state.Pop(1);
            }
        }

        static void ThrowIndexOutOfRange(int index)
        {
            throw new InvalidOperationException($"User value index {index} is out of range.");
        }
    }

    public LuaReference Reference => reference;
    public UserValues UserValue => new(this);
    public int Size
    {
        get
        {
            state.PushValue(Reference);
            var len = state.RawLen(-1);
            state.Pop(1);
            return len;
        }
    }

    public bool TryRead<T>([NotNullWhen(true)] out T? result)
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            result = default;
            return false;
        }

        if (Size != Unsafe.SizeOf<T>())
        {
            result = default;
            return false;
        }
        result = UnsafeRead<T>()!;
        return true;
    }

    public T Read<T>()
    {
        if (!TryRead<T>(out var result))
        {
            throw new InvalidOperationException($"Cannot convert user data to {typeof(T).Name}");
        }
        return result;
    }

    public unsafe T UnsafeRead<T>()
    {
        state.PushValue(Reference);
        var ptr = state.ToUserDataPointer(-1);
        var result = Unsafe.Read<T>((void*)ptr);
        state.Pop(1);
        return result;
    }

    public bool TryWrite<T>(in T value)
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            return false;
        }

        if (Size != Unsafe.SizeOf<T>())
        {
            return false;
        }
        UnsafeWrite(value);
        return true;
    }

    public void Write<T>(in T value)
    {
        if (!TryWrite(value))
        {
            throw new InvalidOperationException($"Cannot convert {typeof(T).Name} to user data");
        }
    }

    public unsafe void UnsafeWrite<T>(in T value)
    {
        state.PushValue(Reference);
        var ptr = state.ToUserDataPointer(-1);
        Unsafe.Write((void*)ptr, value);
        state.Pop(1);
    }

    public void Dispose()
    {
        state.Unref(Reference);
    }
}
