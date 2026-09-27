using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>
/// Some mods put a layer between block and HP: an armour a hit spends before it reaches the creature. The game settles
/// every such layer in one place (see <see cref="ModifyHpLostPatch"/>), so what they ate is measured there, for any
/// mod, without knowing what the layer is. It counts as block: protection a hit spent, rather than HP anyone lost.
/// </summary>
internal static class AbsorbLayers
{
    private static AbsorbLedger Ledger => Fight.Now.Absorbed;

    /// <summary>The game worked out an HP loss: <paramref name="before"/> went in, <paramref name="after"/> came out.</summary>
    /// <param name="modifiers">The models the game says changed it; a layer that keeps quiet isn't in it.</param>
    public static void Measured(Creature target, decimal before, decimal after, IEnumerable<AbstractModel>? modifiers)
    {
        decimal eaten = before - after;
        if (eaten <= 0m) return;
        List<AbstractModel> acted = Safe(modifiers);
        if (!AbsorbLedger.CountsOn(target.IsEnemy, acted.Any(IsGameContent)))
        {
            Tracker.Note($"caps on {target.Monster?.Id.Entry ?? "?"} ate {(int)eaten} hp ({string.Join(", ", acted.Select(m => m.Id.Entry))}), not counted");
            return;
        }
        Ledger.Add(target, eaten, Layer(acted));
    }

    /// <summary>What this creature's layers ate on the hit that just landed, and what they were called. Clears it.</summary>
    public static (int Amount, IReadOnlyList<string> Layers) Take(Creature target) => Ledger.Take(target);

    /// <summary>A hit on this creature is starting: anything still counted for it is stale.</summary>
    public static void Starting(Creature target) => Ledger.Clear(target);

    private static readonly System.Reflection.Assembly Game = typeof(AbstractModel).Assembly;

    /// <summary>The game's own content, not a mod's.</summary>
    private static bool IsGameContent(AbstractModel model) => model.GetType().Assembly == Game;

    private static List<AbstractModel> Safe(IEnumerable<AbstractModel>? modifiers)
    {
        try { return modifiers?.ToList() ?? new List<AbstractModel>(); }
        catch (Exception) { return new List<AbstractModel>(); }
    }

    private static string? Layer(IReadOnlyList<AbstractModel> acted)
    {
        try { return acted.LastOrDefault()?.Id.Entry; }
        catch (Exception) { return null; }
    }
}
