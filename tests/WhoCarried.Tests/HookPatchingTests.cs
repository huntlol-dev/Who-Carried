using System.Reflection;
using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>
/// Which content hooks are safe to watch with a patch. The shapes are a mod's: one generic base class doing the work
/// for several runes, each rune naming the card it looks for.
/// </summary>
public static class HookPatchingTests
{
    public abstract class Model
    {
        public virtual Task AfterPlayerTurnStartLate() => Task.CompletedTask;
    }

    public sealed class DemonForm;

    public abstract class FormRune<TCard> : Model where TCard : class
    {
        public override Task AfterPlayerTurnStartLate() => Task.CompletedTask;
    }

    public sealed class DemonRune : FormRune<DemonForm>;

    public sealed class OwnTurnRune : FormRune<DemonForm>
    {
        public override Task AfterPlayerTurnStartLate() => Task.CompletedTask;
    }

    public sealed class PlainRelic : Model
    {
        public override Task AfterPlayerTurnStartLate() => Task.CompletedTask;
    }

    public abstract class Pack<T>
    {
        public sealed class Inner : Model
        {
            public override Task AfterPlayerTurnStartLate() => Task.CompletedTask;
        }
    }

    private static MethodInfo Hook(Type type)
    {
        MethodInfo inherited = type.GetMethod(nameof(Model.AfterPlayerTurnStartLate))!;
        // Where it's declared, as the patchers hand it to Harmony.
        return inherited.DeclaringType!.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Single(m => m.MetadataToken == inherited.MetadataToken);
    }

    [Test]
    public static void AHookDeclaredInAGenericBaseIsLeftAlone()
    {
        MethodInfo hook = Hook(typeof(DemonRune));
        Check.True(!hook.ContainsGenericParameters, "the old check lets it through: FormRune<DemonForm> is fully named");
        Check.Equal(false, HookPatching.CanWatch(hook), "shared by every FormRune<…>");
    }

    [Test]
    public static void AnOverrideInAPlainClassIsWatched()
    {
        Check.Equal(true, HookPatching.CanWatch(Hook(typeof(PlainRelic))), "a plain relic");
        Check.Equal(true, HookPatching.CanWatch(Hook(typeof(OwnTurnRune))), "a plain rune overriding its generic base");
    }

    [Test]
    public static void AClassInsideAGenericClassIsLeftAlone() =>
        Check.Equal(false, HookPatching.CanWatch(Hook(typeof(Pack<DemonForm>.Inner))), "shares its outer class's type");
}
