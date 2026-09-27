using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>
/// For Vulnerable-style bonus damage. Per enemy hit: the final damage (before block) and any debuff on the target
/// that multiplied it. Per debuff instance: how many stacks each player put in. Only reads game state.
/// </summary>
internal static class DebuffBonusTracker
{
    /// <param name="Parts">Who the multiplier's parts belong to (Debilitate, the hitter's relic…); null when it's all the debuff's.</param>
    public sealed record Amplifier(PowerModel Power, decimal Multiplier, IReadOnlyList<(PowerModel? Power, decimal Weight)>? Parts = null);

    public sealed record PendingHit(decimal Amount, IReadOnlyList<Amplifier> Amplifiers);

    private static readonly ConditionalWeakTable<PowerModel, StackLedger> Stacks = new();
    private static readonly ConditionalWeakTable<PowerModel, SharedPile> Piles = new();

    /// <summary>Poison and Doom: one pile per enemy, whose damage is shared by who owns it.</summary>
    private static bool IsSharedPile(PowerModel power) => power is PoisonPower or DoomPower;

    /// <summary>Called just before a hit's block is applied, with the hit's final damage.</summary>
    public static void BeforeDamage(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        Fight.Now.BoostedHits.Remove(target);
        if (!target.IsEnemy || amount <= 0m) return;
        List<Amplifier>? found = null;
        foreach (PowerModel power in target.Powers.ToList())
        {
            if (power.Type != PowerType.Debuff) continue;
            decimal multiplier;
            try { multiplier = GameCompat.DamageMultiplicative(power, target, amount, props, dealer, cardSource); }
            catch (Exception) { continue; }
            if (multiplier > 1m)
                (found ??= new List<Amplifier>()).Add(new Amplifier(power, multiplier,
                    VanillaAmplifiers.Parts(power, target, amount, props, dealer, cardSource, multiplier)));
        }
        if (found != null) Fight.Now.BoostedHits[target] = new PendingHit(amount, found);
    }

    /// <summary>Debuffs on the attacking enemy that shrank this hit (Weak), with their multipliers (below 1).</summary>
    public static IReadOnlyList<Amplifier> Reducers(Creature dealer, Creature target, decimal amount, ValueProp props,
                                                    CardModel? cardSource) =>
        DamageMultipliers(dealer, target, amount, props, dealer, cardSource, m => m > 0m && m < 1m);

    /// <summary>
    /// Debuffs on <paramref name="holder"/> whose damage multiplier for this hit passes <paramref name="keep"/>. Holder
    /// is the target for Vulnerable-style debuffs and the dealer for Weak-style ones.
    /// </summary>
    public static IReadOnlyList<Amplifier> DamageMultipliers(Creature holder, Creature target, decimal amount, ValueProp props,
                                                             Creature? dealer, CardModel? cardSource, Func<decimal, bool> keep)
    {
        var found = new List<Amplifier>();
        foreach (PowerModel power in holder.Powers.ToList())
        {
            if (power.Type != PowerType.Debuff) continue;
            decimal multiplier;
            try { multiplier = GameCompat.DamageMultiplicative(power, target, amount, props, dealer, cardSource); }
            catch (Exception) { continue; }
            if (keep(multiplier)) found.Add(new Amplifier(power, multiplier,
                VanillaAmplifiers.Parts(power, target, amount, props, dealer, cardSource, multiplier)));
        }
        return found;
    }

    /// <summary>Debuffs on a creature that shrink the block it gains (Frail), with their multipliers (below 1).</summary>
    public static IReadOnlyList<Amplifier> BlockReducers(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
    {
        var found = new List<Amplifier>();
        foreach (PowerModel power in creature.Powers.ToList())
        {
            if (power.Type != PowerType.Debuff) continue;
            decimal multiplier;
            try { multiplier = power.ModifyBlockMultiplicative(creature, amount, props, cardSource, null); }
            catch (Exception) { continue; }
            if (multiplier > 0m && multiplier < 1m) found.Add(new Amplifier(power, multiplier));
        }
        return found;
    }

    /// <summary>
    /// The product of every damage multiplier on this hit (Weak, Vulnerable, relics…), from the game's own damage
    /// calculation, the same one it uses to preview intents. 1 if it can't be worked out.
    /// </summary>
    public static decimal DamageMultiplier(IRunState run, Creature target, Creature dealer, ValueProp props, CardModel? cardSource)
    {
        try
        {
            return GameCompat.ModifyDamage(run, target.CombatState, target, dealer, 1m, props, cardSource,
                ModifyDamageHookType.Multiplicative, CardPreviewMode.None);
        }
        catch (Exception)
        {
            return 1m;
        }
    }

    /// <summary>A real hit's damage before modifiers, and what the game's calculation made of it.</summary>
    internal sealed record Calc(Creature? Dealer, ValueProp Props, decimal Damage, decimal Result);

    private static bool _recalculating;

    /// <summary>The game just worked out a real hit's final damage (see <see cref="ModifyDamagePatch"/>).</summary>
    public static void OnDamageCalculated(Creature target, Creature? dealer, decimal damage, ValueProp props, decimal result)
    {
        if (!_recalculating) Fight.Now.Calcs[target] = new Calc(dealer, props, damage, result);
    }

    /// <summary>
    /// This hit's damage before modifiers, if the game's calculation of it was seen and it matches (same attacker, same
    /// final damage). Removes it. Null if it wasn't seen.
    /// </summary>
    public static decimal? TakeBaseDamage(Creature target, Creature? dealer, decimal amount, ValueProp props) =>
        Fight.Now.Calcs.Remove(target, out Calc? calc) && ReferenceEquals(calc.Dealer, dealer) && calc.Props == props && calc.Result == amount
            ? calc.Damage
            : null;

    /// <summary>
    /// The game's own final damage for a hit that starts at <paramref name="damage"/>: every modifier and cap, floored
    /// at zero, as it works out a real hit. Isn't remembered as a hit. Null if it can't be worked out.
    /// </summary>
    public static decimal? Recalculate(IRunState run, Creature target, Creature dealer, decimal damage, ValueProp props, CardModel? cardSource)
    {
        _recalculating = true;
        try
        {
            return GameCompat.ModifyDamage(run, target.CombatState, target, dealer, damage, props, cardSource,
                ModifyDamageHookType.All, CardPreviewMode.None);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            _recalculating = false;
        }
    }

    /// <summary>Temporary Strength-down debuffs on an enemy (Piercing Wail, Dark Shackles) and how much Strength each removes.</summary>
    public static IReadOnlyList<(PowerModel Power, int Amount)> TemporaryStrengthLoss(Creature enemy) =>
        enemy.Powers.Where(p => p is TemporaryStrengthPower && p.Type == PowerType.Debuff && p.Amount > 0)
            .Select(p => (p, p.Amount)).ToList();

    private static readonly ConditionalWeakTable<Creature, Dictionary<ulong, int>> StrengthLoss = new();

    /// <summary>
    /// Lasting Strength a player took off an enemy (Malaise). Negative Strength from a player adds to it; a temporary
    /// Strength-down debuff from that player takes its own amount back out, because it lowered Strength the same way
    /// and is counted on its own.
    /// </summary>
    public static void AdjustStrengthLoss(Creature enemy, ulong player, int delta)
    {
        Dictionary<ulong, int> byPlayer = StrengthLoss.GetOrCreateValue(enemy);
        byPlayer[player] = byPlayer.GetValueOrDefault(player) + delta;
    }

    public static IReadOnlyList<(ulong Player, int Amount)> LastingStrengthLoss(Creature enemy) =>
        StrengthLoss.TryGetValue(enemy, out Dictionary<ulong, int>? byPlayer)
            ? byPlayer.Where(kv => kv.Value > 0).Select(kv => (kv.Key, kv.Value)).ToList()
            : Array.Empty<(ulong, int)>();

    private static readonly ConditionalWeakTable<Creature, TieTurnsByKey<(ulong Player, string Debuff)>> StrengthLossTies = new();

    /// <summary>
    /// Shares HP that Strength taken off this enemy kept off one of its hits, between the players and debuffs that took
    /// it (share i goes with part i). Exact ties take turns over the enemy's hits.
    /// </summary>
    public static int[] ShareStrengthLoss(Creature enemy, int prevented, IReadOnlyList<((ulong Player, string Debuff) Key, decimal Strength)> parts) =>
        StrengthLossTies.GetOrCreateValue(enemy).Split(prevented, parts);

    /// <summary>The pending hit on this target, if a debuff boosted it. Removes it.</summary>
    public static PendingHit? Take(Creature target) => Fight.Now.BoostedHits.Remove(target, out PendingHit? hit) ? hit : null;

    /// <summary>Stacks landing on an enemy's debuff; <paramref name="player"/> null when no player applied them.</summary>
    public static void AddStacks(PowerModel power, ulong? player, int stacks)
    {
        if (stacks <= 0) return;
        // The game has already added them: Hook.AfterPowerAmountChanged fires after the amount changes.
        if (IsSharedPile(power)) Piles.GetOrCreateValue(power).Add(player, stacks, power.Amount);
        else Stacks.GetOrCreateValue(power).Add(player, stacks);
    }

    /// <summary>
    /// Shares out <paramref name="damage"/> that a Poison or Doom pile just dealt, by who owns it. Null when no player
    /// has a share in it.
    /// </summary>
    public static IReadOnlyDictionary<ulong, int>? SplitPile(PowerModel power, int damage)
    {
        IReadOnlyDictionary<ulong, int> credits = Piles.GetOrCreateValue(power).Credit(power.Amount, damage);
        return credits.Count > 0 ? credits : null;
    }

    /// <summary>
    /// How to share what this debuff did on a hit right now: each player's stacks still on the enemy (the oldest wear
    /// off first). If no player's stacks are left (all applied before a Save &amp; Quit resume), the power's own
    /// applier gets it all; failing that, everyone who ever stacked it, by how much.
    /// </summary>
    public static IReadOnlyList<(ulong Player, int Weight)> Weights(PowerModel power)
    {
        if (Stacks.TryGetValue(power, out StackLedger? ledger))
        {
            ledger.SyncTo(power.Amount);
            IReadOnlyList<(ulong Player, int Weight)> active = ledger.Active();
            if (active.Count > 0) return active;
        }
        return Fallback(power, ledger);
    }

    /// <summary>
    /// Shares <paramref name="amount"/> that this debuff did on a hit, in whole points, by the same weights as
    /// <see cref="Weights"/>. While players' stacks are on the enemy, exact ties take turns (equal Vulnerable or Weak
    /// evens out over a fight); the fallbacks split plainly.
    /// </summary>
    public static IReadOnlyDictionary<ulong, int> Share(PowerModel power, int amount)
    {
        if (Stacks.TryGetValue(power, out StackLedger? ledger))
        {
            ledger.SyncTo(power.Amount);
            if (ledger.Active().Count > 0) return ledger.Share(amount);
        }
        return DebuffBonus.Split(amount, Fallback(power, ledger));
    }

    /// <summary>Stacks that landed on an enemy's debuff in one change, owned by several players.</summary>
    public static void AddStacks(PowerModel power, IReadOnlyDictionary<ulong, int> parts)
    {
        if (IsSharedPile(power))
        {
            Piles.GetOrCreateValue(power).Add(parts.Select(p => ((ulong?)p.Key, p.Value)).ToList(), power.Amount);
            return;
        }
        StackLedger ledger = Stacks.GetOrCreateValue(power);
        foreach ((ulong player, int stacks) in parts) ledger.Add(player, stacks);
    }

    /// <summary>
    /// <paramref name="stacks"/> that <paramref name="from"/> is handing on (Zone the Spire's Hallowed turning half of
    /// itself into Doom), shared by who owns it right now: its pile for Poison and Doom, otherwise the same weights as
    /// <see cref="Share"/>. Empty when no player owns any of it.
    /// </summary>
    public static IReadOnlyDictionary<ulong, int> PassOn(PowerModel from, int stacks)
    {
        if (!IsSharedPile(from)) return DebuffBonus.Split(stacks, Weights(from));
        IReadOnlyList<(ulong Player, decimal Share)> shares = Piles.GetOrCreateValue(from).Shares();
        int[] parts = DebuffBonus.SplitIndexed(stacks, shares.Select(s => s.Share).ToList());
        var passed = new Dictionary<ulong, int>();
        for (int i = 0; i < shares.Count; i++)
            if (parts[i] > 0) passed[shares[i].Player] = parts[i];
        return passed;
    }

    /// <summary>
    /// Shares out a creature's remaining HP that this debuff took with a direct kill (a mod's Doom-like judgement), by
    /// the same weights as <see cref="Share"/>, or by the pile for Poison and Doom. Empty when no player stacked it.
    /// </summary>
    public static IReadOnlyDictionary<ulong, int> ShareKill(PowerModel power, int hp) =>
        IsSharedPile(power) ? SplitPile(power, hp) ?? DebuffBonus.Split(hp, Fallback(power, null)) : Share(power, hp);

    /// <summary>No player's stacks left: the power's own applier, failing that everyone who ever stacked it.</summary>
    private static IReadOnlyList<(ulong Player, int Weight)> Fallback(PowerModel power, StackLedger? ledger)
    {
        if (FactsExtractor.PlayerIdOf(power.Applier) is ulong id) return new[] { (id, 1) };
        return ledger?.Lifetime() ?? Array.Empty<(ulong, int)>();
    }
}
