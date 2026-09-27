using System.Runtime.CompilerServices;

namespace WhoCarried.Core;

/// <summary>
/// Damage a layer between block and HP ate, per creature, while a hit is being worked out. Mods add such layers (an
/// armour a hit spends before it reaches HP); the game settles them all in one place, so what they took is measured
/// there without knowing what they are. Each creature's total is taken once, by the hit that caused it.
/// Creatures are held by reference: two of the same kind are never the same entry.
/// </summary>
public sealed class AbsorbLedger
{
    private sealed class Entry
    {
        public decimal Amount;
        public List<string>? Layers;
    }

    private readonly Dictionary<object, Entry> _byTarget = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// A layer took <paramref name="amount"/> off what this creature was about to lose. Anything that added to the
    /// hit instead is ignored, so it can't cancel out a layer that did eat some.
    /// </summary>
    /// <param name="layer">What ate it, if the game says; only for the log.</param>
    public void Add(object target, decimal amount, string? layer = null)
    {
        if (amount <= 0m) return;
        if (!_byTarget.TryGetValue(target, out Entry? entry)) _byTarget[target] = entry = new Entry();
        entry.Amount += amount;
        if (string.IsNullOrEmpty(layer)) return;
        entry.Layers ??= new List<string>();
        if (!entry.Layers.Contains(layer)) entry.Layers.Add(layer);
    }

    /// <summary>What this creature's layers ate, in whole HP, and which layers. Clears it: one hit, one total.</summary>
    public (int Amount, IReadOnlyList<string> Layers) Take(object target) =>
        _byTarget.Remove(target, out Entry? entry)
            ? ((int)entry.Amount, (IReadOnlyList<string>?)entry.Layers ?? Array.Empty<string>())
            : (0, Array.Empty<string>());

    /// <summary>Forgets this creature's running total (a new hit on it is starting).</summary>
    public void Clear(object target) => _byTarget.Remove(target);

    /// <summary>Forgets everything (a fight started or ended).</summary>
    public void Clear() => _byTarget.Clear();

    /// <summary>
    /// Whether what a layer took off counts as armour. The game's own content only caps HP loss on enemies (Slippery,
    /// Hardened Shell): that isn't block anyone knocked off. On players every layer counts, as the absorb spec decided.
    /// </summary>
    public static bool CountsOn(bool enemy, bool gameContentActed) => !(enemy && gameContentActed);
}
