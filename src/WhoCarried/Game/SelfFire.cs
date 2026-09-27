using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>
/// Things that fire on their own (the Defect's orbs at the end of the turn, and whatever a mod adds that works the
/// same way) deal damage with no card and nothing on the action stack, so the hit arrives without a source. Every
/// kind of damage goes through the game's damage command; when a player's hit starts with nothing to explain it, this
/// looks at which piece of game content made the call, and remembers it for that hit. Nothing here names a mod: the
/// content is found through the game's own model list, with its own name and picture.
/// </summary>
internal static class SelfFire
{
    /// <summary>How long a noted source still counts for its player's next hit.</summary>
    private const ulong WindowMs = 3000;

    private static Dictionary<ulong, (AbstractModel Source, ulong At)> Last => Fight.Now.SelfFired;

    /// <summary>What made this player's last unexplained damage call a moment ago; null if nothing recently.</summary>
    public static SourceCandidate? Recent(ulong? player)
    {
        if (player is not ulong id || !Last.TryGetValue(id, out (AbstractModel Source, ulong At) last)) return null;
        if (Time.GetTicksMsec() - last.At > WindowMs) return null;
        try { return FactsExtractor.Candidate(last.Source); }
        catch (Exception) { return null; }
    }

    /// <summary>A damage command is starting (see <see cref="DamageCommandPatch"/>).</summary>
    public static void OnDamageCommand(PlayerChoiceContext? context, Creature? dealer, bool hasCard)
    {
        if (hasCard || dealer?.Player is not { } player) return;
        if (FactsExtractor.StackTop(context) != null) return; // already explained by what's on the stack
        if (Caller() is AbstractModel source) Last[player.NetId] = (source, Time.GetTicksMsec());
    }

    /// <summary>The nearest game content (an orb, a mod's rune…) up the call stack, as its official copy.</summary>
    internal static AbstractModel? Caller()
    {
        foreach (StackFrame frame in new StackTrace(2, false).GetFrames())
        {
            Type? type = frame.GetMethod()?.DeclaringType;
            // Async methods run inside compiler-made classes nested in the real one.
            while (type != null && type.IsDefined(typeof(CompilerGeneratedAttribute), false)) type = type.DeclaringType;
            if (type == null || type.IsAbstract || !typeof(AbstractModel).IsAssignableFrom(type)) continue;
            if (Models.Of(type) is AbstractModel model) return model;
        }
        return null;
    }
}

/// <summary>
/// The game's list of every piece of content (cards, orbs, powers, and every mod's additions), by type and by id, for
/// finding content's official copy and its picture without knowing about any mod in particular.
/// </summary>
internal static class Models
{
    private static Dictionary<Type, AbstractModel>? _byType;
    private static Dictionary<string, AbstractModel>? _byEntry;

    public static AbstractModel? Of(Type type)
    {
        if (_byType == null || !_byType.ContainsKey(type)) Build();
        return _byType!.GetValueOrDefault(type);
    }

    public static AbstractModel? OfEntry(string entry)
    {
        if (_byEntry == null || !_byEntry.ContainsKey(entry)) Build();
        return _byEntry!.GetValueOrDefault(entry);
    }

    /// <summary>Every kind of content the game has registered, every mod's included (read fresh).</summary>
    public static IReadOnlyCollection<Type> Types()
    {
        Build();
        return _byType!.Keys;
    }

    /// <summary>The model's own picture, from an "Icon" property if it has one (orbs and many mods' content do).</summary>
    public static Texture2D? Icon(string entry)
    {
        try { return OfEntry(entry) is AbstractModel model ? model.GetType().GetProperty("Icon")?.GetValue(model) as Texture2D : null; }
        catch (Exception) { return null; }
    }

    /// <summary>The word for a model's kind, from its base type ("RuneModel" → "Rune"), to drop from readable ids.</summary>
    public static string KindWord(AbstractModel model)
    {
        try
        {
            string name = ModelDb.GetCategoryType(model.GetType()).Name;
            return name.EndsWith("Model", StringComparison.Ordinal) ? name[..^5] : name;
        }
        catch (Exception) { return ""; }
    }

    private static void Build()
    {
        _byType = new Dictionary<Type, AbstractModel>();
        _byEntry = new Dictionary<string, AbstractModel>();
        try
        {
            if (typeof(ModelDb).GetField("_contentById", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) is not System.Collections.IDictionary all) return;
            foreach (object? value in all.Values)
            {
                if (value is not AbstractModel model) continue;
                _byType.TryAdd(model.GetType(), model);
                _byEntry.TryAdd(model.Id.Entry, model);
            }
        }
        catch (Exception e)
        {
            Tracker.LogError("model list", e);
        }
    }
}

/// <summary>Every overload of the game's damage command: note what started a hit that nothing else explains.</summary>
[HarmonyPatch]
internal static class DamageCommandPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
        typeof(CreatureCmd).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == nameof(CreatureCmd.Damage));

    private static void Prefix(MethodBase __originalMethod, object?[] __args)
    {
        try
        {
            ParameterInfo[] parameters = __originalMethod.GetParameters();
            PlayerChoiceContext? context = null;
            Creature? dealer = null;
            bool hasCard = false;
            for (int i = 0; i < parameters.Length && i < __args.Length; i++)
            {
                switch (parameters[i].Name)
                {
                    case "choiceContext": context = __args[i] as PlayerChoiceContext; break;
                    case "dealer": dealer = __args[i] as Creature; break;
                    case "cardSource": hasCard = __args[i] != null; break;
                }
            }
            SelfFire.OnDamageCommand(context, dealer, hasCard);
        }
        catch (Exception e)
        {
            Tracker.LogError("damage command", e);
        }
    }
}
