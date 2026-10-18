using NuLua.Luau;

namespace NuLua.Tests;

public class LuauSandboxTests
{
    [Test]
    public async Task FreezeAfterInitializationKeepsHostFunctionsAndIsolatesWorkers()
    {
        using var state = LuauState.Create();
        state.OpenLibraries();
        state.RegisterFunction(
            "lookup",
            (s, _) =>
            {
                s.PushInteger(42);
                return 1;
            }
        );
        state.PushInteger(123);
        state.Sandbox();
        state.Sandbox();
        await Assert.That(state.GetTop()).IsEqualTo(1);
        await Assert.That(state.ToInteger(-1)).IsEqualTo(123);
        state.Pop(1);

        using var first = state.CreateSandboxThread();
        using var second = state.CreateSandboxThread();
        var result = first.DoString("workerValue = lookup(); return workerValue, math.sqrt(16)");
        await Assert.That(result[0].Read<double>()).IsEqualTo(42);
        await Assert.That(result[1].Read<double>()).IsEqualTo(4);
        var other = second.DoString("return workerValue == nil, lookup()");
        await Assert.That(other[0].Read<bool>()).IsTrue();
        await Assert.That(other[1].Read<double>()).IsEqualTo(42);
        await Assert.That(() => first.DoString("math.sqrt = nil")).Throws<LuaException>();
        await Assert
            .That(() => first.DoString("getmetatable('').__index = nil"))
            .Throws<LuaException>();
        await Assert.That(() => state.DoString("shared = 1")).Throws<LuaException>();
    }

    [Test]
    public async Task SandboxRejectsChildAndDisposedStates()
    {
        using var state = LuauState.Create();
        using var child = state.CreateSandboxThread();
        await Assert.That(() => child.Sandbox()).Throws<InvalidOperationException>();
        child.Dispose();
        await Assert.That(() => child.Sandbox()).Throws<ObjectDisposedException>();
        state.Dispose();
        await Assert.That(() => state.Sandbox()).Throws<ObjectDisposedException>();
    }
}
