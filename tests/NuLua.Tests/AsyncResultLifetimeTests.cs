using NuLua.Luau;

namespace NuLua.Tests;

public class AsyncResultLifetimeTests
{
    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task ReturnedReferencesRemainUsableAfterAwait(LuaStateCase lua)
    {
        using var state = lua.CreateState();
        state.OpenLibraries();
        using var pause = lua.CreateAsyncFunction(
            state,
            async (s, args, ct) =>
            {
                await Task.Yield();
                return 0;
            }
        );
        state["pause"] = pause;
        using var input = state.CreateUserData(sizeof(int));
        input.Write(123);
        state["input"] = input;
        state.PushString("sentinel");

        var results = await state.DoStringAsync(
            """
            pause()
            return { answer = 42 }, function() return 43 end,
                input, coroutine.create(function() return 44 end)
            """
        );

        using var table = results[0].Read<LuaTable>();
        using var function = results[1].Read<LuaFunction>();
        using var userData = results[2].Read<LuaUserData>();
        using var thread = results[3].Read<ILuaState>();
        await Assert.That(table["answer"].Read<int>()).IsEqualTo(42);
        await Assert.That(function.Invoke()[0].Read<int>()).IsEqualTo(43);
        await Assert.That(userData.Read<int>()).IsEqualTo(123);
        await Assert.That(thread.Resume()[0].Read<int>()).IsEqualTo(44);
        await Assert.That(state.GetTop()).IsEqualTo(1);
        await Assert.That(state.ToString(-1)).IsEqualTo("sentinel");
    }

    [Test]
    public async Task ReturnedLuauBufferRemainsUsable()
    {
        using var state = LuauState.Create();
        state.OpenLibraries();

        var results = await state.DoStringAsync("return buffer.fromstring('hello')");

        using var buffer = results[0].Read<LuauBuffer>();
        await Assert.That(buffer.Length).IsEqualTo(5);
        await Assert.That(buffer[0]).IsEqualTo((byte)'h');
        buffer[0] = (byte)'H';
        await Assert.That(buffer.ToArray()[0]).IsEqualTo((byte)'H');
        await Assert.That(state.GetTop()).IsEqualTo(0);
    }
}
