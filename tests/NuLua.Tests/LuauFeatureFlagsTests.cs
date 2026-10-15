using NuLua.Luau;
using BindingFlags = System.Reflection.BindingFlags;

namespace NuLua.Tests;

public class LuauFeatureFlagHooks
{
    [Before(Assembly)]
    public static void EnableConst()
    {
        LuauFeatureFlags.SetBoolean(LuauFeatureFlags.LuauConst2, true);
    }
}

public class LuauFeatureFlagsTests
{
    delegate ReadOnlySpan<byte> FlagNameGetter();

    [Test]
    public async Task UnknownFlagThrows()
    {
        await Assert
            .That(() => LuauFeatureFlags.SetBoolean("MissingFlag"u8, true))
            .Throws<NotSupportedException>();
        await Assert
            .That(() => LuauFeatureFlags.GetBoolean("MissingFlag"u8))
            .Throws<NotSupportedException>();
        await Assert
            .That(() => LuauFeatureFlags.SetInteger("MissingFlag"u8, 0))
            .Throws<NotSupportedException>();
        await Assert
            .That(() => LuauFeatureFlags.GetInteger("MissingFlag"u8))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task InvalidNamesThrow()
    {
        await Assert.That(() => LuauFeatureFlags.GetBoolean([])).Throws<NotSupportedException>();
        await Assert
            .That(() => LuauFeatureFlags.SetBoolean("LuauConst2\0ignored"u8, true))
            .Throws<ArgumentException>();
        await Assert.That(() => LuauFeatureFlags.GetInteger([])).Throws<NotSupportedException>();
        await Assert
            .That(() => LuauFeatureFlags.SetInteger("LuauRecursionLimit\0ignored"u8, 1))
            .Throws<ArgumentException>();
    }

    [Test]
    [NotInParallel]
    public async Task AllDefinedFlagsCanBeReadAndChanged()
    {
        foreach (
            var property in typeof(LuauFeatureFlags).GetProperties(
                BindingFlags.Public | BindingFlags.Static
            )
        )
        {
            var flag = property.GetMethod!.CreateDelegate<FlagNameGetter>()().ToArray();
            bool original;
            try
            {
                original = LuauFeatureFlags.GetBoolean(flag);
            }
            catch (NotSupportedException)
            {
                var integer = LuauFeatureFlags.GetInteger(flag);
                try
                {
                    LuauFeatureFlags.SetInteger(flag, integer + 1);
                    await Assert.That(LuauFeatureFlags.GetInteger(flag)).IsEqualTo(integer + 1);
                }
                finally
                {
                    LuauFeatureFlags.SetInteger(flag, integer);
                }
                continue;
            }

            try
            {
                LuauFeatureFlags.SetBoolean(flag, !original);
                await Assert.That(LuauFeatureFlags.GetBoolean(flag)).IsEqualTo(!original);
            }
            finally
            {
                LuauFeatureFlags.SetBoolean(flag, original);
            }
        }
    }

    [Test]
    public async Task CustomUtf8NameCanBePassed()
    {
        await Assert.That(LuauFeatureFlags.GetBoolean("LuauConst2"u8)).IsTrue();
        await Assert.That(LuauFeatureFlags.LuauConst2.SequenceEqual("LuauConst2"u8)).IsTrue();
    }
}
