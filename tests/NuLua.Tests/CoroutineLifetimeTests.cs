using System.Runtime.CompilerServices;

namespace NuLua.Tests;

public class CoroutineLifetimeTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference CreateAndDisposeThread(ILuaState state)
    {
        var thread = state.CreateThread();
        state.Pop(1);
        thread.Dispose();
        thread.Dispose();
        return new WeakReference(thread);
    }

    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task DisposedThreadsCanBeCollectedWhileParentIsAlive(LuaStateCase lua)
    {
        using var state = lua.CreateState();
        var references = new WeakReference[10];
        for (int i = 0; i < references.Length; i++)
        {
            references[i] = CreateAndDisposeThread(state);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        foreach (var reference in references)
        {
            await Assert.That(reference.IsAlive).IsFalse();
        }
        await Assert.That(state.DoString("return 42")[0].Read<int>()).IsEqualTo(42);
        GC.KeepAlive(state);
    }

    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task ParentDisposalClosesAllLiveDescendants(LuaStateCase lua)
    {
        using var state = lua.CreateState();
        var first = state.CreateThread();
        var second = state.CreateThread();
        var grandchild = first.CreateThread();
        var disposed = state.CreateThread();
        disposed.Dispose();

        state.Dispose();
        state.Dispose();

        await Assert.That(first.AsPointer()).IsEqualTo(0);
        await Assert.That(second.AsPointer()).IsEqualTo(0);
        await Assert.That(grandchild.AsPointer()).IsEqualTo(0);
        await Assert.That(disposed.AsPointer()).IsEqualTo(0);
    }
}
