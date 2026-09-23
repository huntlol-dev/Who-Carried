namespace WhoCarried.Core;

/// <summary>
/// Who gets the credit for help the game hands over without always naming a giver (energy, draws, block from no card).
/// The most direct answer wins; when the game's answers disagree or run out, nobody does, rather than a guess.
/// </summary>
public static class SupportCredit
{
    /// <param name="named">A giver the game named itself (the block card's owner, the creator, the applier).</param>
    /// <param name="turnEffect">The player behind the content whose turn hook is running (a power's applier, a relic's owner).</param>
    /// <param name="midEffect">Players with a card or potion taking effect right now; null when this game can't say.</param>
    /// <param name="actionOwner">The running action's player; only asked when <paramref name="midEffect"/> is null.</param>
    public static ulong? Giver(ulong? named, ulong? turnEffect, IReadOnlyCollection<ulong>? midEffect, ulong? actionOwner)
    {
        if (named != null) return named;
        if (turnEffect != null) return turnEffect;
        if (midEffect == null) return actionOwner;
        return midEffect.Count == 1 ? midEffect.First() : null;
    }
}
