using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>
/// Which piece of content is running when something happens that nothing else names. This watches the game's
/// turn-boundary hooks (<see cref="EffectScopes.IsTurnBoundaryHook"/>): turn starts and ends, the energy reset, block
/// clearing and before-hand-draw steps of a player's turn, the start of a fight, and orbs' own turn-start and turn-end
/// triggers. As a piece of content's hook runs, the live instance is the running effect (<see cref="EffectScopes"/>).
/// Nothing names a mod: the hooks are found through the game's own model list and base classes. Installed once, when
/// the first run starts, so every mod's content is registered by then.
/// <list type="bullet">
/// <item>Always: the kill command pins the effect that started it, so a creature killed outright at the end of a turn
/// (Zone the Spire's Hallowed, a mod's Doom) is credited to what judged it (<see cref="Tracker.OnDirectKill"/>).</item>
/// <item>Always: a debuff turning part of itself into another as it acts (Hallowed into Doom) hands its owners the new
/// stacks (<see cref="Running"/>, read by <see cref="Tracker.OnPowerChanged"/>).</item>
/// <item>Experimental, off unless settings.json has "experimentalEffectSources": true: each damage command pins it too,
/// so damage with no dealer, no card and nothing on the action stack (a modded power ticking at the start of a turn)
/// has something to credit.</item>
/// </list>
/// </summary>
internal static class EffectSources
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Where the watched hooks are declared; their own do-nothing versions aren't watched.</summary>
    private static readonly Type[] HookBases = { typeof(AbstractModel), typeof(OrbModel) };

    private static readonly EffectScopes Scopes = new();
    private static bool _tried;

    /// <summary>The experimental part: damage commands pin their effect.</summary>
    public static bool Enabled { get; private set; }

    /// <summary>The live effect that started the damage command running here; null when off or none was seen.</summary>
    public static AbstractModel? DamageSource => Enabled ? Scopes.DamageSource as AbstractModel : null;

    /// <summary>The content whose turn-boundary hook is running here; null if none is.</summary>
    public static AbstractModel? Running => Scopes.Effect as AbstractModel;

    public static void NewFight() => Scopes.NewFight();

    public static void InstallOnce(string dataDir)
    {
        if (_tried) return;
        _tried = true;
        (Settings settings, _) = Settings.Load(dataDir);

        var clock = Stopwatch.StartNew();
        var harmony = new Harmony("whocarried.effects");
        List<MethodInfo> hooks = TurnBoundaryHooks(Models.Types());
        int watched = Patch(harmony, hooks, nameof(EnterEffect), nameof(LeaveEffect));
        // The one-creature overload hands its creature to this one.
        MethodInfo? kill = AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Kill),
            new[] { typeof(IReadOnlyCollection<Creature>), typeof(bool) });
        bool kills = kill != null && Patch(harmony, new[] { kill }, nameof(EnterKill), nameof(LeaveKill)) > 0;
        string damage = "damage commands off (experimental)";
        if (settings.ExperimentalEffectSources)
        {
            List<MethodInfo> commands = typeof(CreatureCmd).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == nameof(CreatureCmd.Damage)).ToList();
            int pinned = Patch(harmony, commands, nameof(EnterDamage), nameof(LeaveDamage));
            // Without the damage command, nothing would ever read a watched hook for damage.
            Enabled = pinned > 0;
            damage = $"{pinned}/{commands.Count} damage commands{(Enabled ? "" : " (OFF: none patched)")}";
        }
        Tracker.Note($"effect sources: watching {watched}/{hooks.Count} turn-boundary hooks, kill command {(kills ? "on" : "OFF")}, " +
                     $"{damage}, in {clock.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// Every turn-boundary hook some content overrides, where it's declared: an override a mod's shared base class makes
    /// once is one method, whichever content inherits it. The hooks are the ones every model has, and the ones only orbs
    /// have (their turn-start and turn-end triggers).
    /// </summary>
    private static List<MethodInfo> TurnBoundaryHooks(IEnumerable<Type> types)
    {
        var found = new HashSet<MethodInfo>();
        foreach (Type type in types)
        {
            if (type.IsAbstract || !typeof(AbstractModel).IsAssignableFrom(type)) continue;
            try
            {
                foreach (MethodInfo method in type.GetMethods(Instance))
                {
                    if (method.IsAbstract || !method.IsVirtual || method.ContainsGenericParameters) continue;
                    if (method.ReturnType != typeof(Task) || HookBases.Contains(method.DeclaringType)) continue;
                    if (!EffectScopes.IsTurnBoundaryHook(method.Name) || !HookBases.Contains(method.GetBaseDefinition().DeclaringType)) continue;
                    found.Add(Declared(method));
                }
            }
            catch (Exception e)
            {
                Tracker.Note($"effect sources: can't look at {type.FullName}: {e.Message}");
            }
        }
        return found.ToList();
    }

    /// <summary>Harmony won't patch an inherited method reflected through a subclass: go back to where it's declared.</summary>
    private static MethodInfo Declared(MethodInfo method) =>
        method.DeclaringType!.GetMethods(Instance | BindingFlags.DeclaredOnly).Single(d => d.MetadataToken == method.MetadataToken);

    private static int Patch(Harmony harmony, IEnumerable<MethodInfo> methods, string prefix, string finalizer)
    {
        var enter = new HarmonyMethod(typeof(EffectSources).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static));
        var leave = new HarmonyMethod(typeof(EffectSources).GetMethod(finalizer, BindingFlags.NonPublic | BindingFlags.Static));
        int patched = 0, failed = 0;
        foreach (MethodInfo method in methods)
        {
            try
            {
                harmony.Patch(method, prefix: enter, finalizer: leave);
                patched++;
            }
            catch (Exception e)
            {
                if (++failed <= 5) Tracker.Note($"effect sources: can't watch {method.DeclaringType?.FullName}.{method.Name}: {e.Message}");
            }
        }
        return patched;
    }

    private static void EnterEffect(AbstractModel __instance, out EffectScopes.Frame? __state) => __state = Scopes.EnterEffect(__instance);

    private static void LeaveEffect(EffectScopes.Frame? __state, Task? __result) => Scopes.LeaveEffect(__state, __result);

    private static void EnterDamage(out EffectScopes.Frame? __state) => __state = Scopes.EnterDamage();

    private static void LeaveDamage(EffectScopes.Frame? __state, Task? __result) => Scopes.LeaveDamage(__state, __result);

    /// <summary>Before any creature dies: its HP is still there to count.</summary>
    private static void EnterKill(IReadOnlyCollection<Creature> creatures, out EffectScopes.Frame? __state)
    {
        __state = Scopes.EnterKill(out object? effect);
        try { Tracker.OnDirectKill(creatures, effect as AbstractModel); }
        catch (Exception e) { Tracker.LogError("direct kill", e); }
    }

    private static void LeaveKill(EffectScopes.Frame? __state, Task? __result) => Scopes.LeaveKill(__state, __result);

    /// <summary>Tells two instances of the same content apart in the log.</summary>
    public static string InstanceId(AbstractModel effect) => $"{effect.Id.Entry} #{RuntimeHelpers.GetHashCode(effect):x}";
}
