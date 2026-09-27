namespace WhoCarried.Core;

/// <summary>Block taken off a creature without a hit (Expose): only what it actually had.</summary>
public static class BlockStrip
{
    public static int Removed(decimal amount, int block) => amount <= 0m || block <= 0 ? 0 : block - (int)Math.Max(block - amount, 0m);

    /// <summary>
    /// Older games name no remover: only credit a live source that matches the content making the call.
    /// A known ownerless effect stays ownerless; the current player's card is never a guess for an enemy's call.
    /// </summary>
    public static SourceCandidate? LegacySource(SourceRef? caller, SourceCandidate? effect, SourceCandidate? card)
    {
        if (caller == null) return null;
        if (effect?.Source.Key == caller.Key) return effect;
        return card?.Source.Key == caller.Key ? card : null;
    }
}
