using System.Text;
using NuLua.Lua54;
using NuLua.Luau;

namespace NuLua.Tests;

public class LuaDebugTests
{
    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task StackSourcePreservesUtf8(LuaStateCase lua)
    {
        using var state = lua.CreateState();
        const string chunkName = "@こんにちは.lua";
        LuaDebugInfo? captured = null;
        using var probe = state.CreateProbeFunction(s =>
        {
            if (s.Debug.TryGetStackInfo(1, LuaDebugInfoFields.Source, out var info))
            {
                captured = info;
            }
        });
        state["probe"] = probe;
        state.LoadDebuggable("probe()", chunkName);
        state.Call(0, 0);

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Value.Source).IsEqualTo(chunkName);
        await Assert.That(captured.Value.ShortSource).Contains("こんにちは.lua");
    }

    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task GetUpvalueRetrievesClosureUpvalue(LuaStateCase lua)
    {
        using var state = lua.CreateState();
        state.LoadDebuggable("local x = 'captured'; return function() return x end", "chunk");
        state.Call(0, 1);

        // Stack: [function]
        var name = state.Debug.GetUpvalue(-1, 1);

        await Assert.That(name).IsEqualTo("x");
        await Assert.That(state.GetType(-1)).IsEqualTo(LuaValueType.String);
        await Assert.That(state.ToString(-1)).IsEqualTo("captured");
    }

    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task SetUpvalueOverwritesClosureUpvalue(LuaStateCase lua)
    {
        using var state = lua.CreateState();
        state.LoadDebuggable("local x = 'before'; return function() return x end", "chunk");
        state.Call(0, 1);

        state.PushString("after");
        var name = state.Debug.SetUpvalue(-2, 1);

        await Assert.That(name).IsEqualTo("x");

        // Invoke the function to verify new value
        state.Call(0, 1);
        await Assert.That(state.ToString(-1)).IsEqualTo("after");
    }

    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task GetUpvalueReturnsNullForOutOfRange(LuaStateCase lua)
    {
        using var state = lua.CreateState();
        state.LoadDebuggable("return function() end", "chunk");
        state.Call(0, 1);

        var name = state.Debug.GetUpvalue(-1, 99);

        await Assert.That(name).IsNull();
    }

    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task TryGetStackInfoReturnsFalseForInvalidLevel(LuaStateCase lua)
    {
        using var state = lua.CreateState();

        var ok = state.Debug.TryGetStackInfo(99, LuaDebugInfoFields.Source, out _);

        await Assert.That(ok).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task TryGetStackInfoFromInsideCallback(LuaStateCase lua)
    {
        using var state = lua.CreateState();

        LuaDebugInfo? captured = null;
        var probe = state.CreateProbeFunction(s =>
        {
            if (
                s.Debug.TryGetStackInfo(
                    1,
                    LuaDebugInfoFields.Source | LuaDebugInfoFields.CurrentLine,
                    out var info
                )
            )
            {
                captured = info;
            }
        });

        state["probe"] = LuaValue.FromFunction(probe);
        state.DoString("probe()", []);

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Value.CurrentLine).IsGreaterThan(0);
    }

    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task GetAndSetLocalRoundTrip(LuaStateCase lua)
    {
        using var state = lua.CreateState();

        string? capturedName = null;
        LuaValueType capturedType = LuaValueType.Nil;

        var probe = state.CreateProbeFunction(s =>
        {
            // level 1 is the caller (Lua function) which has a local "x"
            var name = s.Debug.GetLocal(1, 1);
            capturedName = name;
            if (name != null)
            {
                capturedType = s.GetType(-1);
                s.SetTop(s.GetTop() - 1);

                // Overwrite with new value
                s.PushNumber(99);
                _ = s.Debug.SetLocal(1, 1);
            }
        });

        state["probe"] = LuaValue.FromFunction(probe);
        state.LoadDebuggable("local x = 1; probe(); return x", "chunk");
        state.Call(0, 1);
        var resultValue = state.ToNumber(-1);
        state.SetTop(state.GetTop() - 1);

        await Assert.That(capturedName).IsEqualTo("x");
        await Assert.That(capturedType).IsEqualTo(LuaValueType.Number);
        await Assert.That(resultValue).IsEqualTo(99);
    }

    [Test]
    public async Task Lua54LineHookFiresForEachExecutedLine()
    {
        using var state = Lua54State.Create();

        var events = new List<(LuaHookEvent ev, int line)>();
        state.Debug.SetHook((s, ev, line) => events.Add((ev, line)), LuaHookMask.Line, 0);

        state.DoString("local a = 1\nlocal b = 2\nlocal c = a + b\n");
        state.Debug.SetHook(null, LuaHookMask.None, 0);

        await Assert.That(events.Count).IsGreaterThanOrEqualTo(3);
        await Assert.That(events.TrueForAll(e => e.ev == LuaHookEvent.Line)).IsTrue();
    }

    [Test]
    public async Task LuauSingleStepCallbackFires()
    {
        using var state = LuauState.Create();

        int stepCount = 0;
        state.Debug.SetSingleStep(true);
        state.Debug.SetDebugStepCallback((s, ev, line) => stepCount++);

        var bytes = Encoding.UTF8.GetBytes("local a = 1; local b = 2; local c = a + b");
        var bytecode = LuauCompiler.Compile(
            bytes,
            new LuauCompileOptions { DebugLevel = 2, OptimizationLevel = 0 }
        );
        state.LoadBuffer(bytecode, "chunk");
        state.Call(0, 0);

        state.Debug.SetDebugStepCallback(null);
        state.Debug.SetSingleStep(false);

        await Assert.That(stepCount).IsGreaterThan(0);
    }

    [Test]
    public async Task LuauSetHookThrows()
    {
        using var state = LuauState.Create();

        await Assert
            .That(() => state.Debug.SetHook((s, ev, line) => { }, LuaHookMask.Line, 0))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task LuauDebugTraceReturnsString()
    {
        using var state = LuauState.Create();
        var trace = state.Debug.GetDebugTrace();
        await Assert.That(trace).IsNotNull();
    }
}

file static class ProbeExtensions
{
    public static void LoadDebuggable(this ILuaState state, string source, string chunk)
    {
        if (state is LuauState luau)
        {
            var bytes = Encoding.UTF8.GetBytes(source);
            var bytecode = LuauCompiler.Compile(
                bytes,
                new LuauCompileOptions { DebugLevel = 2, OptimizationLevel = 0 }
            );
            luau.LoadBuffer(bytecode, chunk);
        }
        else
        {
            state.LoadString(source, chunk);
        }
    }

    public static LuaFunction CreateProbeFunction(this ILuaState state, Action<ILuaState> probe)
    {
        return state switch
        {
            Lua54State s => s.CreateFunction(
                (st, args) =>
                {
                    probe(st);
                    return 0;
                }
            ),
            NuLua.Lua51.Lua51State s => s.CreateFunction(
                (st, args) =>
                {
                    probe(st);
                    return 0;
                }
            ),
            NuLua.Lua52.Lua52State s => s.CreateFunction(
                (st, args) =>
                {
                    probe(st);
                    return 0;
                }
            ),
            NuLua.Lua53.Lua53State s => s.CreateFunction(
                (st, args) =>
                {
                    probe(st);
                    return 0;
                }
            ),
            NuLua.Lua55.Lua55State s => s.CreateFunction(
                (st, args) =>
                {
                    probe(st);
                    return 0;
                }
            ),
            NuLua.LuaJit.LuaJitState s => s.CreateFunction(
                (st, args) =>
                {
                    probe(st);
                    return 0;
                }
            ),
            LuauState s => s.CreateFunction(
                (st, args) =>
                {
                    probe(st);
                    return 0;
                }
            ),
            _ => throw new NotSupportedException($"Unsupported state type: {state.GetType()}"),
        };
    }
}
