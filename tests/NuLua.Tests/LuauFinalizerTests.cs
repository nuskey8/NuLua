using NuLua.Luau;

namespace NuLua.Tests;

[NotInParallel]
public class LuauFinalizerTests
{
    bool originalFlag;

    [Before(Test)]
    public void EnableFinalizers()
    {
        originalFlag = LuauFeatureFlags.GetBoolean(LuauFeatureFlags.LuauCoroutineFinally);
        LuauFeatureFlags.SetBoolean(LuauFeatureFlags.LuauCoroutineFinally, true);
    }

    [After(Test)]
    public void RestoreFlag() =>
        LuauFeatureFlags.SetBoolean(LuauFeatureFlags.LuauCoroutineFinally, originalFlag);

    static LuauState CreateState()
    {
        var state = LuauState.Create();
        state.OpenLibraries();
        state.RegisterFunction(
            "pause",
            async (s, args, ct) =>
            {
                await Task.Yield();
                return 0;
            }
        );
        return state;
    }

    [Test]
    public async Task SuccessRunsYieldingFinalizersInReverseOrderAndPreservesResults()
    {
        using var state = CreateState();
        var results = await state.DoStringAsync(
            """
            order = ''
            local co = coroutine.running()
            coroutine.finally(co, function(status, a, b)
                pause()
                coroutine.yield()
                order = order .. 'A'
                finalStatus, finalA, finalB = status, a, b
            end)
            coroutine.finally(co, function() order = order .. 'B' end)
            return 42, 'ok'
            """
        );
        await Assert.That(results.Length).IsEqualTo(2);
        await Assert.That(results[0].Read<double>()).IsEqualTo(42);
        await Assert.That(results[1].Read<string>()).IsEqualTo("ok");
        await Assert.That(state["order"].Read<string>()).IsEqualTo("BA");
        await Assert.That(state["finalStatus"].Read<string>()).IsEqualTo("finished");
        await Assert.That(state["finalA"].Read<double>()).IsEqualTo(42);
        await Assert.That(state["finalB"].Read<string>()).IsEqualTo("ok");
        await Assert.That(state.GetTop()).IsEqualTo(0);
    }

    [Test]
    public async Task LuaErrorRunsFinalizersAndPreservesException()
    {
        using var state = CreateState();
        await Assert
            .That(async () =>
                await state.DoStringAsync(
                    """
                    coroutine.finally(coroutine.running(), function(status, message)
                        pause()
                        finalStatus, finalMessage = status, message
                    end)
                    error('body failed', 0)
                    """
                )
            )
            .Throws<LuaException>();
        await Assert.That(state["finalStatus"].Read<string>()).IsEqualTo("error");
        await Assert.That(state["finalMessage"].Read<string>()).IsEqualTo("body failed");
        await Assert.That(state.GetTop()).IsEqualTo(0);
    }

    [Test]
    public async Task CancellationAwaitsCleanupWithUncancelledTokenAndStateCanBeReused()
    {
        using var state = CreateState();
        using var cts = new CancellationTokenSource();
        bool cleanupTokenWasCancelled = true;
        state.RegisterFunction(
            "cancel",
            async (s, args, ct) =>
            {
                cts.Cancel();
                await Task.Delay(1, ct);
                return 0;
            }
        );
        state.RegisterFunction(
            "cleanup",
            async (s, args, ct) =>
            {
                await Task.Yield();
                cleanupTokenWasCancelled = ct.IsCancellationRequested;
                return 0;
            }
        );
        await Assert
            .That(async () =>
                await state.DoStringAsync(
                    """
                    coroutine.finally(coroutine.running(), function(status)
                        cleanup()
                        finalStatus = status
                        cleaned = (cleaned or 0) + 1
                    end)
                    cancel()
                    """,
                    default,
                    cts.Token
                )
            )
            .Throws<OperationCanceledException>();
        await Assert.That(cleanupTokenWasCancelled).IsFalse();
        await Assert.That(state["finalStatus"].Read<string>()).IsEqualTo("cancelled");
        await Assert.That(state["cleaned"].Read<double>()).IsEqualTo(1);
        var results = await state.DoStringAsync("return 42");
        await Assert.That(results[0].Read<double>()).IsEqualTo(42);
        await Assert.That(state.GetTop()).IsEqualTo(0);
    }

    [Test]
    public async Task ManagedCallbackFailureClosesCoroutineAndPreservesException()
    {
        using var state = CreateState();
        var expected = new InvalidOperationException("callback failed");
        state.RegisterFunction(
            "fail",
            async (s, args, ct) =>
            {
                await Task.Yield();
                throw expected;
            }
        );
        Exception? caught = null;
        try
        {
            await state.DoStringAsync(
                """
                coroutine.finally(coroutine.running(), function(status)
                    pause()
                    finalStatus = status
                end)
                fail()
                """
            );
        }
        catch (Exception ex)
        {
            caught = ex;
        }
        await Assert.That(ReferenceEquals(caught, expected)).IsTrue();
        await Assert.That(state["finalStatus"].Read<string>()).IsEqualTo("cancelled");
    }

    [Test]
    public async Task CleanupFailurePropagates()
    {
        using var state = CreateState();
        await Assert
            .That(async () =>
                await state.DoStringAsync(
                    """
                    coroutine.finally(coroutine.running(), function() error('cleanup failed', 0) end)
                    return 42
                    """
                )
            )
            .Throws<LuaException>();
    }

    [Test]
    public async Task BodyAndCleanupFailuresAreBothReported()
    {
        using var state = CreateState();
        AggregateException? caught = null;
        try
        {
            await state.DoStringAsync(
                """
                coroutine.finally(coroutine.running(), function() error('cleanup failed', 0) end)
                error('body failed', 0)
                """
            );
        }
        catch (AggregateException ex)
        {
            caught = ex;
        }
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.InnerExceptions[0].Message).IsEqualTo("body failed");
        await Assert.That(caught.InnerExceptions[1].Message).IsEqualTo("cleanup failed");
    }

    [Test]
    public async Task ManualYieldDoesNotFinalizeUntilCompletion()
    {
        using var state = CreateState();
        using var co = state.CreateThread();
        state.Pop(1);
        co.LoadString(
            """
            coroutine.finally(coroutine.running(), function() cleaned = true end)
            coroutine.yield(42)
            return 43
            """,
            "yield-test"
        );
        await ((ILuaState)co).ResumeAsync(0, default);
        await Assert.That(state["cleaned"].Type).IsEqualTo(LuaValueType.Nil);
        co.SetTop(0);
        await ((ILuaState)co).ResumeAsync(0, default);
        await Assert.That(state["cleaned"].Read<bool>()).IsTrue();
        await Assert.That(co.ToNumber(-1)).IsEqualTo(43);
    }
}
