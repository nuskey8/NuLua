using System.Runtime.ExceptionServices;
using NuLua.Interop.Luau;

namespace NuLua.Luau;

public sealed unsafe partial class LuauState
{
    static readonly NativeMethods.luau_setinterrupt_callback_delegate interruptTrampoline =
        OnInterrupt;
    Action? interruptCallback;
    ExceptionDispatchInfo? interruptException;
    bool inInterrupt;

    /// <summary>
    /// Installs a VM-wide periodic execution callback shared by all coroutines.
    /// Throw to interrupt execution; null removes the registration. The callback
    /// must not access the VM. GC safepoints do not invoke it. A caught script error
    /// still propagates the original managed exception at the next host boundary.
    /// This does not preempt host callbacks or guarantee termination of hostile pcall loops.
    /// </summary>
    public void SetInterrupt(Action? callback)
    {
        CheckDisposed();
        var main = ptrToState[(nint)NativeMethods.lua_mainthread(ptr)];
        try
        {
            NativeMethods.luau_setinterrupt(ptr, callback == null ? null! : interruptTrampoline);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new NotSupportedException(
                "This Luau native binary must be rebuilt to support execution interrupts.",
                exception
            );
        }
        main.interruptCallback = callback;
    }

    static int OnInterrupt(void* state)
    {
        LuauState? main = null;
        try
        {
            main = ptrToState[(nint)NativeMethods.lua_mainthread((lua_State*)state)];
            if (main.interruptException != null)
                return 1;
            main.inInterrupt = true;
            main.interruptCallback?.Invoke();
            return 0;
        }
        catch (Exception exception)
        {
            if (main != null)
                main.interruptException ??= ExceptionDispatchInfo.Capture(exception);
            return 1;
        }
        finally
        {
            if (main != null)
                main.inInterrupt = false;
        }
    }

    void CheckInterruptAccess()
    {
        if (
            ptr != null
            && ptrToState.TryGetValue((nint)NativeMethods.lua_mainthread(ptr), out var main)
            && main.inInterrupt
        )
            throw new InvalidOperationException(
                "The VM cannot be accessed from its interrupt callback."
            );
    }

    void ThrowPendingInterrupt()
    {
        var main = ptrToState[(nint)NativeMethods.lua_mainthread(ptr)];
        var exception = main.interruptException;
        main.interruptException = null;
        exception?.Throw();
    }

    void ClearInterrupt()
    {
        // Old native binaries remain usable when this feature is not installed.
        if (interruptCallback != null)
            NativeMethods.luau_setinterrupt(ptr, null!);
        interruptCallback = null;
        interruptException = null;
    }
}
