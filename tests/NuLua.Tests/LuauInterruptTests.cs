using NuLua.Luau;

namespace NuLua.Tests;

public class LuauInterruptTests
{
    [Test]
    public async Task CallbackFailurePropagatesOriginalExceptionAndStateCanBeReused()
    {
        using var state = LuauState.Create();
        var expected = new TimeoutException("deadline");
        state.SetInterrupt(() => throw expected);
        await Assert.That(() => state.DoString("while true do end")).Throws<TimeoutException>();
        state.SetInterrupt(null);
        await Assert.That(state.DoString("return 42")[0].Read<double>()).IsEqualTo(42);
    }

    [Test]
    public async Task CallbackIsSharedByExistingAndScriptCreatedCoroutines()
    {
        using var state = LuauState.Create();
        state.OpenLibraries();
        using var child = state.CreateSandboxThread();
        var calls = 0;
        child.SetInterrupt(() => calls++);
        state.DoString(
            "local c = coroutine.create(function() for i=1,100 do end end); assert(coroutine.resume(c))"
        );
        await Assert.That(calls > 0).IsTrue();
        child.SetInterrupt(() => throw new TimeoutException());
        await Assert.That(() => child.DoString("while true do end")).Throws<TimeoutException>();
        state.SetInterrupt(null);
        child.DoString("return 1");
    }

    [Test]
    public async Task ScriptPcallCannotSwallowManagedFailureAtHostBoundary()
    {
        using var state = LuauState.Create();
        state.OpenLibraries();
        var armed = false;
        state.RegisterFunction(
            "arm",
            (_, _) =>
            {
                armed = true;
                return 0;
            }
        );
        state.SetInterrupt(() =>
        {
            if (armed)
                throw new TimeoutException();
        });
        await Assert
            .That(() => state.DoString("pcall(function() arm(); while true do end end)"))
            .Throws<TimeoutException>();
        state.SetInterrupt(null);
        state.DoString("return 1");
    }

    [Test]
    public async Task ReentrantVmAccessBecomesManagedFailure()
    {
        using var state = LuauState.Create();
        state.SetInterrupt(() => state.Dispose());
        await Assert
            .That(() => state.DoString("while true do end"))
            .Throws<InvalidOperationException>();
        state.SetInterrupt(null);
        state.DoString("return 1");
    }
}
