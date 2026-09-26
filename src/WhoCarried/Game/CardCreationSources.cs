using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>Observes only card-generation reactions, independently of damage and other support effects.</summary>
internal static class CardCreationSources
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly EffectScopes Scopes = new();
    private static bool _installed;
    public static AbstractModel? Running => Scopes.Effect as AbstractModel;
    public static void NewFight() => Scopes.NewFight();

    public static void InstallOnce()
    {
        if (_installed) return;
        _installed = true;
        Install(Models.Types(), Tracker.Note);
    }

    // Also used by the standalone integration checks against the real game assemblies.
    internal static int Install(IEnumerable<Type> types, Action<string> log)
    {
        var methods = new HashSet<MethodInfo>();
        var generic = new HashSet<MethodInfo>();
        MethodInfo? baseHook = typeof(AbstractModel).GetMethod(nameof(AbstractModel.AfterCardGeneratedForCombat),
            Instance, null, new[] { typeof(CardModel), typeof(Player) }, null);
        if (baseHook == null)
        {
            log("card creation: reaction correction unavailable (base hook missing)");
            return 0;
        }
        int errors = 0;
        foreach (Type type in types)
        {
            if (type.IsAbstract || !typeof(AbstractModel).IsAssignableFrom(type)) continue;
            try
            {
                MethodInfo? hook = type.GetMethod(baseHook.Name, Instance, null,
                    new[] { typeof(CardModel), typeof(Player) }, null);
                if (hook == null || hook.IsAbstract || !hook.IsVirtual || hook.ContainsGenericParameters ||
                    hook.ReturnType != typeof(Task) || hook.DeclaringType == typeof(AbstractModel) ||
                    hook.GetBaseDefinition() != baseHook) continue;
                // Harmony must receive the declaring class's MethodInfo, not an inherited reflection wrapper.
                MethodInfo declared = hook.DeclaringType!.GetMethods(Instance | BindingFlags.DeclaredOnly)
                    .Single(m => m.MetadataToken == hook.MetadataToken);
                // A generic class's hook can't be patched without breaking it (see HookPatching).
                (HookPatching.CanWatch(declared) ? methods : generic).Add(declared);
            }
            catch (Exception e)
            {
                if (++errors <= 5) log($"card creation: cannot inspect {type.FullName}: {e.Message}");
            }
        }
        var harmony = new Harmony("whocarried.card-creation");
        var enter = new HarmonyMethod(typeof(CardCreationSources), nameof(Enter));
        var leave = new HarmonyMethod(typeof(CardCreationSources), nameof(Leave));
        int watched = 0;
        foreach (MethodInfo method in methods)
        {
            try
            {
                harmony.Patch(method, prefix: enter, finalizer: leave);
                watched++;
            }
            catch (Exception e)
            {
                if (++errors <= 5) log($"card creation: cannot watch {method.DeclaringType?.FullName}: {e.Message}");
            }
        }
        log($"card creation: watching {watched}/{methods.Count} reaction hooks" +
            (generic.Count > 0 ? $" ({generic.Count} in generic classes left alone)" : "") +
            (watched == 0 ? "; reaction correction unavailable" : ""));
        return watched;
    }

    public static ulong? ContributorOf(AbstractModel effect)
    {
        try
        {
            return effect switch
            {
                // A recipient is not proof of who supplied a buff.
                PowerModel power => FactsExtractor.PlayerIdOf(power.Applier),
                CardModel card => card.Owner?.NetId,
                RelicModel relic => relic.Owner?.NetId,
                PotionModel potion => potion.Owner?.NetId,
                OrbModel orb => orb.Owner?.NetId,
                _ => null,
            };
        }
        catch (Exception) { return null; }
    }

    private static void Enter(AbstractModel __instance, out EffectScopes.Frame? __state) =>
        __state = Scopes.EnterEffect(__instance);

    private static void Leave(EffectScopes.Frame? __state, Task? __result) => Scopes.LeaveEffect(__state, __result);
}
