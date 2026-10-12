using System.Numerics;
using NuLua.Luau;

namespace NuLua.Tests;

public class LuauStateTests
{
    [Test]
    public async Task ConstBindingsAndFunctionsCanBeExecuted()
    {
        using var state = LuauState.Create();
        var results = state.DoString(
            """
            const answer: number = 42
            const function getAnswer() return answer end
            local const = 10
            return getAnswer(), const
            """
        );

        await Assert.That(results[0].Read<double>()).IsEqualTo(42);
        await Assert.That(results[1].Read<double>()).IsEqualTo(10);
    }

    [Test]
    public async Task StandaloneCompilerSupportsConstAndRejectsReassignment()
    {
        var bytecode = LuauCompiler.Compile("const answer = 42; return answer"u8);
        using var state = LuauState.Create();
        state.LoadBuffer(bytecode, "const-test");
        state.Call(0, 1);
        await Assert.That(state.ToNumber(-1)).IsEqualTo(42);

        // Compile errors are encoded with a zero byte followed by the diagnostic.
        // Check that directly without relying on LoadBuffer's error handling.
        var invalid = LuauCompiler.Compile("const answer = 42; answer = 0"u8);
        await Assert.That(invalid[0]).IsEqualTo((byte)0);
        await Assert
            .That(System.Text.Encoding.UTF8.GetString(invalid.AsSpan(1)))
            .Contains("Assigned expression must be a variable or a field");
    }

    [Test]
    public async Task PushVectorAndToVectorRoundTripsComponents()
    {
        using var state = LuauState.Create();

        var input = new Vector3(1.5f, -2.25f, 3.75f);
        state.PushVector(input);

        await Assert.That(state.GetType(-1)).IsEqualTo(LuaValueType.Vector);
        var output = state.ToVector(-1);
        await Assert.That(output).IsEqualTo(input);
    }

    [Test]
    public async Task ToVectorThrowsWhenValueIsNotAVector()
    {
        using var state = LuauState.Create();
        state.PushNumber(1.0);
        await Assert.That(() => state.ToVector(-1)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task GetTypeReportsVectorAndBufferForLuauValues()
    {
        using var state = LuauState.Create();

        state.PushVector(new Vector3(1f, 2f, 3f));
        await Assert.That(state.GetType(-1)).IsEqualTo(LuaValueType.Vector);

        using var buffer = state.NewBuffer(4);
        state.PushValue(buffer.Reference);
        await Assert.That(state.GetType(-1)).IsEqualTo(LuaValueType.Buffer);
        state.Pop(1);
    }

    [Test]
    public async Task NewBufferReturnsBufferOfRequestedSize()
    {
        using var state = LuauState.Create();

        using var buffer = state.NewBuffer(16);

        await Assert.That(buffer.Length).IsEqualTo(16);
    }

    [Test]
    public async Task LuauBufferIndexerWritesAndReadsBytes()
    {
        using var state = LuauState.Create();

        using var buffer = state.NewBuffer(4);
        buffer[0] = 10;
        buffer[1] = 20;
        buffer[2] = 30;
        buffer[3] = 40;

        await Assert.That(buffer[0]).IsEqualTo((byte)10);
        await Assert.That(buffer[3]).IsEqualTo((byte)40);
    }

    [Test]
    public async Task LuauBufferCopyFromAndToArrayRoundTripsBytes()
    {
        using var state = LuauState.Create();

        using var buffer = state.NewBuffer(5);
        buffer.CopyFrom(new byte[] { 1, 2, 3, 4, 5 });

        var copy = buffer.ToArray();
        await Assert.That(copy.Length).IsEqualTo(5);
        await Assert.That(copy[0]).IsEqualTo((byte)1);
        await Assert.That(copy[4]).IsEqualTo((byte)5);
    }

    [Test]
    public async Task ToLuauBufferWrapsExistingBufferOnStack()
    {
        using var state = LuauState.Create();

        using var original = state.NewBuffer(3);
        original[0] = 7;
        original[1] = 8;
        original[2] = 9;

        state.PushValue(original.Reference);
        using var wrapped = state.ToBuffer(-1);
        state.Pop(1);

        await Assert.That(wrapped.Length).IsEqualTo(3);
        await Assert.That(wrapped[0]).IsEqualTo((byte)7);
        await Assert.That(wrapped[2]).IsEqualTo((byte)9);
    }

    [Test]
    public async Task LuauToLuaValueReturnsVectorTypeForVectors()
    {
        using var state = LuauState.Create();

        var input = new Vector3(4f, 5f, 6f);
        state.PushVector(input);

        var value = state.ToLuaValue(-1);
        await Assert.That(value.Type).IsEqualTo(LuaValueType.Vector);
        await Assert.That(value.Read<Vector3>()).IsEqualTo(input);
    }

    [Test]
    public async Task LuauToLuaValueReturnsBufferTypeForBuffers()
    {
        using var state = LuauState.Create();

        using var buffer = state.NewBuffer(2);
        buffer[0] = 100;
        buffer[1] = 200;

        state.PushValue(buffer.Reference);
        var value = state.ToLuaValue(-1);
        state.Pop(1);

        await Assert.That(value.Type).IsEqualTo(LuaValueType.Buffer);
        using var read = value.Read<LuauBuffer>();
        await Assert.That(read.Length).IsEqualTo(2);
        await Assert.That(read[0]).IsEqualTo((byte)100);
    }

    [Test]
    public async Task LuauPushHandlesVectorLuaValue()
    {
        using var state = LuauState.Create();

        var input = new Vector3(11f, 22f, 33f);
        state.Push(LuaValue.FromVector(input));

        await Assert.That(state.GetType(-1)).IsEqualTo(LuaValueType.Vector);
        await Assert.That(state.ToVector(-1)).IsEqualTo(input);
    }

    [Test]
    public async Task GetTypeReportsNonClassForOrdinaryValues()
    {
        // Luau's class/object types are gated behind the FFlag::DebugLuauUserDefinedClasses
        // compiler flag, which we cannot toggle from C#, so we only assert that ordinary
        // values report their own type here.
        using var state = LuauState.Create();

        state.PushNumber(1.0);
        await Assert.That(state.GetType(-1)).IsNotEqualTo(LuaValueType.Class);
        await Assert.That(state.GetType(-1)).IsNotEqualTo(LuaValueType.Object);

        state.PushString("hello");
        await Assert.That(state.GetType(-1)).IsNotEqualTo(LuaValueType.Class);
        await Assert.That(state.GetType(-1)).IsNotEqualTo(LuaValueType.Object);
    }

    [Test]
    public async Task ToClassThrowsWhenValueIsNotAClass()
    {
        using var state = LuauState.Create();
        state.PushNumber(1.0);
        await Assert.That(() => state.ToClass(-1)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ToObjectThrowsWhenValueIsNotAnObject()
    {
        using var state = LuauState.Create();
        state.PushNumber(1.0);
        await Assert.That(() => state.ToObject(-1)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task LuaValueFromClassAndFromObjectSetCorrectType()
    {
        var dummy = new DummyLuaObject();
        await Assert.That(LuaValue.FromClass(dummy).Type).IsEqualTo(LuaValueType.Class);
        await Assert.That(LuaValue.FromObject(dummy).Type).IsEqualTo(LuaValueType.Object);
    }

    sealed class DummyLuaObject : ILuaObject
    {
        public LuaReference Reference => default;

        public void Dispose() { }
    }
}
