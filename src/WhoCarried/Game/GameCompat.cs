using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace WhoCarried.Game;

/// <summary>
/// The few game APIs that differ between the public branch (v0.107) and the beta (v0.111), looked up once by name so
/// one build runs on both. Each picks whichever form this game has; nothing here decides by version number. When the
/// public branch catches up, the older forms can go.
/// - Damage maths: the beta added a CardPlay parameter (always null here) to ModifyDamageMultiplicative and
///   Hook.ModifyDamage.
/// - Input names: the beta renamed MegaInput.accept to confirm and the left stick's actions, added right-stick scroll
///   (altUp/altDown, missing on the public branch), and renamed IsUsingController and GetShortcutKey.
/// - The context's model stack: only the beta exposes it; both keep it in the same private field, and on both its top
///   is LastInvolvedModel.
/// - Card effects in progress: CombatManager.IsExecutingCardOrPotionEffect, looked up by name; without it the running
///   action's owner is used.
/// </summary>
internal static class GameCompat
{
    private delegate decimal DamageMultiplierFn(AbstractModel model, Creature? target, decimal amount, ValueProp props,
                                                Creature? dealer, CardModel? cardSource);

    private delegate decimal ModifyDamageFn(IRunState runState, ICombatState? combatState, Creature? target, Creature? dealer,
                                            decimal damage, ValueProp props, CardModel? cardSource, ModifyDamageHookType type,
                                            CardPreviewMode previewMode, out IEnumerable<AbstractModel> modifiers);

    private delegate decimal ModifyDamageWithPlayFn(IRunState runState, ICombatState? combatState, Creature? target,
                                                    Creature? dealer, decimal damage, ValueProp props, CardModel? cardSource,
                                                    CardPlay? cardPlay, ModifyDamageHookType type, CardPreviewMode previewMode,
                                                    out IEnumerable<AbstractModel> modifiers);

    private static readonly Lazy<DamageMultiplierFn> DamageMultiplierImpl = new(FindDamageMultiplier);
    private static readonly Lazy<ModifyDamageFn> ModifyDamageImpl = new(FindModifyDamage);
    private static readonly Lazy<Func<NControllerManager, bool>?> ControllerModeImpl = new(FindControllerMode);
    private static readonly Lazy<Func<NInputManager, StringName, Key>?> HotkeyImpl = new(FindHotkey);
    private static readonly Lazy<Func<Player, bool>?> ExecutingImpl = new(FindExecuting);
    private static readonly FieldInfo? ModelStackField = AccessTools.Field(typeof(PlayerChoiceContext), "_modelStack");
    private static readonly Dictionary<string, string> Forms = new();

    /// <summary>What this debuff multiplies a hit by (Vulnerable 1.5, Weak 0.75…).</summary>
    public static decimal DamageMultiplicative(AbstractModel model, Creature? target, decimal amount, ValueProp props,
                                               Creature? dealer, CardModel? cardSource) =>
        DamageMultiplierImpl.Value(model, target, amount, props, dealer, cardSource);

    /// <summary>The game's own damage calculation for a hit of <paramref name="damage"/>.</summary>
    public static decimal ModifyDamage(IRunState runState, ICombatState? combatState, Creature? target, Creature? dealer,
                                       decimal damage, ValueProp props, CardModel? cardSource, ModifyDamageHookType type,
                                       CardPreviewMode previewMode) =>
        ModifyDamageImpl.Value(runState, combatState, target, dealer, damage, props, cardSource, type, previewMode, out _);

    /// <summary>The models on the context's stack, top first (for the log). Empty if there are none.</summary>
    public static IEnumerable<AbstractModel> ModelStack(PlayerChoiceContext? context) =>
        context != null && ModelStackField?.GetValue(context) is IEnumerable<AbstractModel> stack ? stack : Array.Empty<AbstractModel>();

    /// <summary>Whether a player's card or potion is taking effect right now; null if this game can't say.</summary>
    public static Func<Player, bool>? ExecutingCardOrPotion => ExecutingImpl.Value;

    /// <summary>The player whose action is running (a card played, a potion used); null if none.</summary>
    public static ulong? RunningActionOwner() => RunManager.Instance?.ActionExecutor?.CurrentlyRunningAction?.OwnerId;

    /// <summary>The id of the card or potion the running action is playing or using; null if there's none, or it's neither.</summary>
    public static string? RunningActionSource() => RunManager.Instance?.ActionExecutor?.CurrentlyRunningAction switch
    {
        PlayCardAction play => play.CardModelId.Entry,
        UsePotionAction potion => potion.Player.GetPotionAtSlotIndex((int)potion.PotionIndex)?.Id.Entry,
        _ => null,
    };

    /// <summary>Whether the game is in controller mode (it switches on the first controller press, back on mouse use).</summary>
    public static bool ControllerMode(NControllerManager? controllers) =>
        controllers != null && ControllerModeImpl.Value is { } read && read(controllers);

    /// <summary>The keyboard key the player has bound to a game action, or Key.None.</summary>
    public static Key Hotkey(NInputManager input, StringName action) =>
        HotkeyImpl.Value is { } read ? read(input, action) : Key.None;

    /// <summary>The "confirm" action (Save image on a controller).</summary>
    public static StringName? Confirm => InputName(typeof(MegaInput), "confirm", "accept");

    /// <summary>Right-stick scrolling; the public branch has no such actions (null there).</summary>
    public static StringName? ScrollUp => InputName(typeof(MegaInput), "altUp");

    public static StringName? ScrollDown => InputName(typeof(MegaInput), "altDown");

    public static StringName? StickUp => InputName(typeof(Controller), "lStickUp", "joystickUp");

    public static StringName? StickDown => InputName(typeof(Controller), "lStickDown", "joystickDown");

    public static StringName? StickLeft => InputName(typeof(Controller), "lStickLeft", "joystickLeft");

    public static StringName? StickRight => InputName(typeof(Controller), "lStickRight", "joystickRight");

    /// <summary>Which form of each API this game has, for the log at start-up; lists anything missing.</summary>
    public static string Describe()
    {
        var parts = new List<string>
        {
            Probe("damage multiplier", () => DamageMultiplierImpl.Value),
            Probe("damage calc", () => ModifyDamageImpl.Value),
            Probe("controller mode", () => ControllerModeImpl.Value),
            Probe("hotkeys", () => HotkeyImpl.Value),
            ModelStackField == null ? "model stack: MISSING" : "model stack: ok",
            Probe("card effects", () => ExecutingImpl.Value),
            $"inputs: confirm {Confirm ?? "MISSING"}, stick {StickUp ?? "MISSING"}, scroll {ScrollUp ?? "none"}",
        };
        return string.Join("; ", parts);
    }

    private static string Probe(string name, Func<object?> get)
    {
        try { return get() == null ? $"{name}: MISSING" : $"{name}: {Forms.GetValueOrDefault(name, "ok")}"; }
        catch (Exception e) { return $"{name}: MISSING ({e.Message})"; }
    }

    private static StringName? InputName(Type type, params string[] fields)
    {
        foreach (string field in fields)
            if (AccessTools.Field(type, field)?.GetValue(null) is StringName name) return name;
        return null;
    }

    private static DamageMultiplierFn FindDamageMultiplier()
    {
        const string name = nameof(AbstractModel.ModifyDamageMultiplicative);
        Type[] old = { typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel) };
        if (typeof(AbstractModel).GetMethod(name, old) is MethodInfo five)
        {
            Forms["damage multiplier"] = "without CardPlay";
            return (DamageMultiplierFn)Delegate.CreateDelegate(typeof(DamageMultiplierFn), five);
        }
        MethodInfo six = typeof(AbstractModel).GetMethod(name, old.Append(typeof(CardPlay)).ToArray())
                         ?? throw new MissingMethodException(nameof(AbstractModel), name);
        Forms["damage multiplier"] = "with CardPlay";
        var withPlay = (Func<AbstractModel, Creature?, decimal, ValueProp, Creature?, CardModel?, CardPlay?, decimal>)
            Delegate.CreateDelegate(typeof(Func<AbstractModel, Creature?, decimal, ValueProp, Creature?, CardModel?, CardPlay?, decimal>), six);
        return (model, target, amount, props, dealer, card) => withPlay(model, target, amount, props, dealer, card, null);
    }

    private static ModifyDamageFn FindModifyDamage()
    {
        const string name = nameof(Hook.ModifyDamage);
        Type modifiers = typeof(IEnumerable<AbstractModel>).MakeByRefType();
        Type[] head = { typeof(IRunState), typeof(ICombatState), typeof(Creature), typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(CardModel) };
        Type[] tail = { typeof(ModifyDamageHookType), typeof(CardPreviewMode), modifiers };
        if (typeof(Hook).GetMethod(name, head.Concat(tail).ToArray()) is MethodInfo old)
        {
            Forms["damage calc"] = "without CardPlay";
            return (ModifyDamageFn)Delegate.CreateDelegate(typeof(ModifyDamageFn), old);
        }
        MethodInfo current = typeof(Hook).GetMethod(name, head.Append(typeof(CardPlay)).Concat(tail).ToArray())
                             ?? throw new MissingMethodException(nameof(Hook), name);
        Forms["damage calc"] = "with CardPlay";
        var withPlay = (ModifyDamageWithPlayFn)Delegate.CreateDelegate(typeof(ModifyDamageWithPlayFn), current);
        return (ModifyDamageFn)((IRunState r, ICombatState? cs, Creature? t, Creature? d, decimal dmg, ValueProp p, CardModel? c,
                                 ModifyDamageHookType h, CardPreviewMode m, out IEnumerable<AbstractModel> mods) =>
            withPlay(r, cs, t, d, dmg, p, c, null, h, m, out mods));
    }

    private static Func<NControllerManager, bool>? FindControllerMode()
    {
        PropertyInfo? property = AccessTools.Property(typeof(NControllerManager), "IsUsingDirectionalNavigation")
                                 ?? AccessTools.Property(typeof(NControllerManager), "IsUsingController");
        if (property != null) Forms["controller mode"] = property.Name;
        return property?.GetGetMethod(nonPublic: true) is MethodInfo getter
            ? (Func<NControllerManager, bool>)Delegate.CreateDelegate(typeof(Func<NControllerManager, bool>), getter)
            : null;
    }

    private static Func<NInputManager, StringName, Key>? FindHotkey()
    {
        MethodInfo? method = AccessTools.Method(typeof(NInputManager), "GetCurrentHotkey", new[] { typeof(StringName) })
                             ?? AccessTools.Method(typeof(NInputManager), "GetShortcutKey", new[] { typeof(StringName) });
        if (method != null) Forms["hotkeys"] = method.Name;
        return method != null && method.ReturnType == typeof(Key)
            ? (Func<NInputManager, StringName, Key>)Delegate.CreateDelegate(typeof(Func<NInputManager, StringName, Key>), method)
            : null;
    }

    private static Func<Player, bool>? FindExecuting()
    {
        if (AccessTools.Method(typeof(CombatManager), "IsExecutingCardOrPotionEffect", new[] { typeof(Player) }) is not MethodInfo method ||
            method.ReturnType != typeof(bool))
            return null;
        // A changed signature leaves this game without the check (the running action's owner stands in), not throwing.
        Func<CombatManager, Player, bool> call;
        try { call = (Func<CombatManager, Player, bool>)Delegate.CreateDelegate(typeof(Func<CombatManager, Player, bool>), method); }
        catch (Exception) { return null; }
        return player => CombatManager.Instance is CombatManager combat && call(combat, player);
    }

    /// <summary>Logs which forms this game has (one line), and warns if any is missing.</summary>
    public static void LogAtStart()
    {
        string summary = Describe();
        if (summary.Contains("MISSING")) Log.Warn($"[WhoCarried] game APIs: {summary}");
        else Log.Info($"[WhoCarried] game APIs: {summary}");
    }
}
