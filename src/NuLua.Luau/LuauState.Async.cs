using System.Runtime.ExceptionServices;
using NuLua.Interop.Luau;

namespace NuLua.Luau;

public sealed unsafe partial class LuauState
{
    internal bool HasPendingFinalizers() =>
        LuauFeatureFlags.GetBoolean(LuauFeatureFlags.LuauCoroutineFinally)
        && NativeMethods.lua_hasfinalizers(ptr) != 0;

    internal bool HasLuaError => NativeMethods.lua_status(ptr) > LUA_YIELD;

    internal bool IsYielded => NativeMethods.lua_status(ptr) == LUA_YIELD;

    internal LuauState CreateFinalizerRunner(bool close)
    {
        // Luau cannot finalize a coroutine paused at a debugger break (LUA_BREAK).
        if (NativeMethods.lua_status(ptr) == 6)
            throw new InvalidOperationException(
                "Cannot finalize a coroutine paused at a debugger break."
            );

        var main = GetMainState(ptr);
        var runner = main.CreateThread();
        main.Pop(1);
        try
        {
            NativeMethods.lua_pushfinalizerfunction(runner.ptr, ptr, close ? 1 : 0);
            return runner;
        }
        catch
        {
            runner.Dispose();
            throw;
        }
    }
}

internal static class LuauAsyncDriver
{
    public static async ValueTask ResumeAsync(
        LuauState state,
        int initialArgCount,
        CancellationToken cancellationToken
    )
    {
        state.SetAsyncCancellationToken(cancellationToken);
        try
        {
            Exception? failure = null;
            bool completed = false;
            try
            {
                completed = await RunAsync(state, initialArgCount, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            if ((completed || failure != null) && state.HasPendingFinalizers())
            {
                var luaError = state.HasLuaError;
                try
                {
                    using var runner = state.CreateFinalizerRunner(!completed && !luaError);
                    do
                    {
                        await ResumeAsync(runner, 0, CancellationToken.None).ConfigureAwait(false);
                        if (runner.IsYielded)
                            runner.SetTop(0);
                    } while (runner.IsYielded);

                    if (completed)
                        runner.XMove(state, runner.GetTop());
                }
                catch (Exception cleanupError)
                {
                    // A successfully finalized Lua error is rethrown by the VM.
                    // Keep the original managed exception and its stack trace.
                    var originalLuaError =
                        luaError
                        && failure is LuaException original
                        && cleanupError is LuaException propagated
                        && original.Code == propagated.Code
                        && original.Message == propagated.Message;
                    if (!originalLuaError)
                    {
                        if (failure == null)
                        {
                            failure = cleanupError;
                        }
                        else
                        {
                            failure = new AggregateException(failure, cleanupError);
                        }
                    }
                }
            }

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally
        {
            state.ResetAsyncState();
        }
    }

    static async ValueTask<bool> RunAsync(
        LuauState state,
        int argCount,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.RunResumeStep(argCount) == 0)
                return true;
            if (!state.TryTakePendingAsyncTask(out var task))
                return false;
            argCount = await task.ConfigureAwait(false);
        }
    }
}
