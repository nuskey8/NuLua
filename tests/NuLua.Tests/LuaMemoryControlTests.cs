using NuLua.Luau;

namespace NuLua.Tests;

public class LuaMemoryControlTests
{
    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task ReportsMemoryAndExplicitBackendCapability(LuaStateCase lua)
    {
        using var state = lua.CreateState();
        await Assert.That(state.Memory.UsedBytes > 0).IsTrue();
        await Assert.That(() => state.Memory.SetLimit(-1)).Throws<ArgumentOutOfRangeException>();
        if (state is not LuauState)
        {
            await Assert.That(state.Memory.SupportsLimit).IsFalse();
            await Assert.That(() => state.Memory.SetLimit(1)).Throws<NotSupportedException>();
        }
    }

    [Test]
    public async Task LimitsAndAccountingAreSharedAndMutable()
    {
        using var state = LuauState.Create();
        using var child = state.CreateSandboxThread();
        await Assert.That(state.Memory.SupportsLimit).IsTrue();
        await Assert.That(state.Memory.UsedBytes).IsEqualTo(child.Memory.UsedBytes);
        await Assert.That(state.Memory.SetLimit(1)).IsEqualTo(0);
        await Assert.That(child.Memory.SetLimit(2)).IsEqualTo(1);
        await Assert.That(state.Memory.SetLimit(0)).IsEqualTo(2);
        var before = state.Memory.UsedBytes;
        using (var buffer = child.NewBuffer(1024 * 1024))
            await Assert.That(state.Memory.UsedBytes > before).IsTrue();
        state.GarbageCollection.Collect();
        await Assert.That(state.Memory.UsedBytes < before + 1024 * 1024).IsTrue();
    }

    [Test]
    public async Task AllocationFailureInExecutionAndHostCallbackIsRecoverable()
    {
        using var state = LuauState.Create();
        state.OpenLibraries();
        state.RegisterFunction(
            "allocate",
            (s, _) =>
            {
                using var buffer = s.NewBuffer(4 * 1024 * 1024);
                return 0;
            }
        );
        foreach (var source in new[] { "return buffer.create(4194304)", "allocate()" })
        {
            state.LoadString(source, "budget");
            state.Memory.SetLimit(state.Memory.UsedBytes + 64 * 1024);
            await Assert.That(() => state.Call(0, 0)).Throws<LuaException>();
            state.Memory.SetLimit(0);
            state.SetTop(0);
            await Assert.That(state.DoString("return 42")[0].Read<double>()).IsEqualTo(42);
        }
    }

    [Test]
    public async Task HostSetupLoadingAndReferenceGrowthHaveProtectedBoundaries()
    {
        using var state = LuauState.Create();
        var bytecode = LuauCompiler.Compile(
            System.Text.Encoding.UTF8.GetBytes("return '" + new string('x', 100000) + "'")
        );
        state.Memory.SetLimit(1);
        await Assert.That(() => state.NewBuffer(4 * 1024 * 1024)).Throws<LuaException>();
        state.SetTop(0);
        await Assert.That(() => state.LoadBuffer(bytecode, "budget")).Throws<LuaException>();
        state.Memory.SetLimit(0);
        state.SetTop(0);
        state.NewTable();
        state.Memory.SetLimit(1);
        var references = new List<LuaReference>();
        await Assert
            .That(() =>
            {
                for (var i = 0; i < 10000; i++)
                    references.Add(state.Ref(-1));
            })
            .Throws<LuaException>();
        state.Memory.SetLimit(0);
        foreach (var reference in references)
            state.Unref(reference);
        state.SetTop(0);
        state.OpenLibraries();
        state.DoString("return 1");
    }

    [Test]
    public async Task FailedFunctionRegistrationPreservesCallerUpvalues()
    {
        using var state = LuauState.Create();
        state.PushInteger(7);
        state.Memory.SetLimit(1);
        await Assert
            .That(() =>
            {
                for (var i = 0; i < 10000; i++)
                {
                    state.PushInteger(42);
                    state.NewFunction((_, _) => 0, 1);
                    state.Pop(1);
                }
            })
            .Throws<LuaException>();
        state.Memory.SetLimit(0);
        await Assert.That(state.GetTop()).IsEqualTo(2);
        await Assert.That(state.ToInteger(-1)).IsEqualTo(42);
        await Assert.That(state.ToInteger(-2)).IsEqualTo(7);
    }

    [Test]
    public async Task DebugCallbacksAndLimitsHaveAnExplicitCompatibilityBoundary()
    {
        using var state = LuauState.Create();
        var debug = (ILuauDebug)state.Debug;
        debug.SetDebugStepCallback((_, _, _) => { });
        await Assert.That(() => state.Memory.SetLimit(1)).Throws<NotSupportedException>();
        debug.SetDebugStepCallback(null);
        state.Memory.SetLimit(1);
        await Assert
            .That(() => debug.SetDebugStepCallback((_, _, _) => { }))
            .Throws<NotSupportedException>();
        state.Memory.SetLimit(0);
        await Assert.That(() => state.LoadString("local =", "invalid")).Throws<LuaException>();
    }

    [Test]
    public async Task DisposingAtTheLimitDoesNotRequireAnAllocationAllowance()
    {
        var state = LuauState.Create();
        state.OpenLibraries();
        state.Memory.SetLimit(1);
        state.Dispose();
        await Assert.That(() => state.Memory.SetLimit(0)).Throws<ObjectDisposedException>();
    }
}
