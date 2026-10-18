using System.Text;
using NuLua.Luau;

namespace NuLua.Tests;

public class Utf8StringTests
{
    const string Text = "こんにちは café Привет 😀";

    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task NativeStringReadsPreserveUtf8AndEmbeddedNul(LuaStateCase lua)
    {
        using var state = lua.CreateState();
        state.PushString(Encoding.UTF8.GetBytes(Text + "\0after"));

        await Assert.That(state.ToString(-1)).IsEqualTo(Text + "\0after");
        await Assert.That(state.ToLuaValue(-1).Read<string>()).IsEqualTo(Text + "\0after");
        await Assert.That(state.GetTop()).IsEqualTo(1);
    }

    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task ScriptResultsPreserveUtf8(LuaStateCase lua)
    {
        using var state = lua.CreateState();

        var result = state.DoString("return 'こんにちは café Привет 😀', ''");

        await Assert.That(result[0].Read<string>()).IsEqualTo(Text);
        await Assert.That(result[1].Read<string>()).IsEqualTo(string.Empty);
        await Assert.That(state.GetTop()).IsEqualTo(0);
    }

    [Test]
    public async Task LuauCallbackArgumentsPreserveUtf8()
    {
        using var state = LuauState.Create();
        string? received = null;
        using var receive = state.CreateFunction(
            (s, args) =>
            {
                received = args[0].Read<string>();
                return 0;
            }
        );
        state["receive"] = receive;

        state.DoString("receive('こんにちは café Привет 😀')");

        await Assert.That(received).IsEqualTo(Text);
    }

    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task RuntimeErrorMessagesPreserveUtf8(LuaStateCase lua)
    {
        using var state = lua.CreateState();
        state.OpenLibraries();
        LuaException? caught = null;
        try
        {
            state.DoString("error('こんにちは café Привет 😀')");
        }
        catch (LuaException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains(Text);
    }

    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task AsyncRuntimeErrorMessagesPreserveUtf8(LuaStateCase lua)
    {
        using var state = lua.CreateState();
        state.OpenLibraries();
        LuaException? caught = null;
        try
        {
            await state.DoStringAsync("error('こんにちは café Привет 😀')");
        }
        catch (LuaException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains(Text);
    }
}
