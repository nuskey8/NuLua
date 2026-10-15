using NuLua.Internal;

namespace NuLua.Tests;

public class CStringTests
{
    [Test]
    public async Task ConstructorsRejectNul()
    {
        await Assert
            .That(() =>
            {
                using var text = new CString("before\0after".AsSpan());
            })
            .Throws<ArgumentException>();
        await Assert
            .That(() =>
            {
                using var text = new CString("before\0after"u8);
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task UncheckedCreationPreservesNulAndUtf8()
    {
        using var chars = CString.CreateUnchecked("あ\0い".AsSpan());
        using var bytes = CString.CreateUnchecked("あ\0い"u8);

        await Assert.That(chars.ToString()).IsEqualTo("あ\0い");
        await Assert.That(bytes.ToString()).IsEqualTo("あ\0い");
        await Assert.That(chars.AsSpan().SequenceEqual("あ\0い"u8)).IsTrue();
        await Assert.That(bytes.AsSpan().SequenceEqual("あ\0い"u8)).IsTrue();
        await Assert.That(IsTerminated(chars)).IsTrue();
        await Assert.That(IsTerminated(bytes)).IsTrue();
    }

    [Test]
    public async Task ConstructorsAcceptEmptyAndUnicode()
    {
        using var emptyChars = new CString(ReadOnlySpan<char>.Empty);
        using var emptyBytes = new CString(ReadOnlySpan<byte>.Empty);
        using var chars = new CString("あいう".AsSpan());
        using var bytes = new CString("あいう"u8);

        await Assert.That(emptyChars.AsSpan().IsEmpty).IsTrue();
        await Assert.That(emptyBytes.AsSpan().IsEmpty).IsTrue();
        await Assert.That(chars.ToString()).IsEqualTo("あいう");
        await Assert.That(bytes.ToString()).IsEqualTo("あいう");
        await Assert.That(IsTerminated(emptyChars)).IsTrue();
        await Assert.That(IsTerminated(emptyBytes)).IsTrue();
        await Assert.That(IsTerminated(chars)).IsTrue();
        await Assert.That(IsTerminated(bytes)).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(LuaStateCases), nameof(LuaStateCases.All))]
    public async Task PushStringPreservesEmbeddedNul(LuaStateCase lua)
    {
        using var state = lua.CreateState();
        state.PushString("before\0after".AsSpan());
        await Assert.That(state.ToString(-1)).IsEqualTo("before\0after");
    }

    static unsafe bool IsTerminated(CString value) => value.Pointer[value.AsSpan().Length] == 0;
}
