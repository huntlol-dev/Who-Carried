namespace WhoCarried.Core;

/// <summary>
/// Who gets credit for what an effect does outside a hit, decided from facts the game layer reads: an enemy it kills
/// outright as it acts at the start or end of a turn (<see cref="ForKill"/>), and stacks it hands on as it turns into
/// another debuff (<see cref="ForHandedOn"/>).
/// </summary>
public static class EffectCredit
{
    private static readonly IReadOnlyDictionary<ulong, int> Nobody = new Dictionary<ulong, int>();

    /// <summary>A creature the kill command is about to remove.</summary>
    /// <param name="EffectActing">Some effect's turn hook started the kill (not a card, a move, or another kill).</param>
    /// <param name="CountedAsDoom">Doom's own kill has already counted it.</param>
    /// <param name="EffectOwner">The acting effect's player, if it has one.</param>
    /// <param name="HpInfinite">The creature health bar shows infinite: its HP is not real.</param>
    public sealed record Kill(bool EffectActing, bool IsEnemy, bool IsAlive, int Hp, bool CountedAsDoom, ulong? EffectOwner,
                              bool HpInfinite = false);

    /// <summary>
    /// Who a kill's HP goes to. Null when it isn't counted: no effect acting, a player or pet, a creature already dead or
    /// at 0, one showing infinite HP, or Doom's own kill. Otherwise the enemy's own copy of the effect shares it out by who stacked it; with no
    /// copy, the effect's player gets it all. Empty when no player is behind it: an own copy no player stacked (an
    /// enemy's Fading ending it) credits nobody, not the effect's player.
    /// </summary>
    /// <param name="ownCopy">Splits HP by who stacked the enemy's own copy of the effect; null when it carries none.
    /// Only called for a kill that counts.</param>
    public static IReadOnlyDictionary<ulong, int>? ForKill(Kill kill, Func<int, IReadOnlyDictionary<ulong, int>>? ownCopy)
    {
        if (!kill.EffectActing || !kill.IsEnemy || !kill.IsAlive || kill.Hp <= 0 || kill.CountedAsDoom || kill.HpInfinite) return null;
        if (ownCopy != null) return ownCopy(kill.Hp);
        return kill.EffectOwner is ulong owner ? new Dictionary<ulong, int> { [owner] = kill.Hp } : Nobody;
    }

    /// <summary>Stacks landing on an enemy's debuff.</summary>
    /// <param name="PlayerApplier">A player, or their pet, applied them.</param>
    /// <param name="FromCard">A card applied them.</param>
    /// <param name="AnotherCreatureApplied">A creature other than the enemy itself applied them.</param>
    /// <param name="OtherDebuffActing">Another debuff on the same enemy is acting at the start or end of a turn.</param>
    public sealed record NewStacks(bool PlayerApplier, bool FromCard, bool AnotherCreatureApplied, bool OtherDebuffActing);

    /// <summary>
    /// Stacks another debuff on the same enemy hands on as it acts (Hallowed turning half of itself into Doom, naming the
    /// enemy as the applier), shared by that debuff's owners. Null for any other stacks, and when no player owns the
    /// debuff: then they stay nobody's.
    /// </summary>
    /// <param name="owners">Splits an amount by who owns the acting debuff. Only called for stacks it handed on.</param>
    public static IReadOnlyDictionary<ulong, int>? ForHandedOn(NewStacks stacks, int amount, Func<int, IReadOnlyDictionary<ulong, int>> owners)
    {
        if (stacks.PlayerApplier || stacks.FromCard || stacks.AnotherCreatureApplied || !stacks.OtherDebuffActing) return null;
        IReadOnlyDictionary<ulong, int> parts = owners(amount);
        return parts.Count > 0 ? parts : null;
    }
}
