using System.Globalization;

namespace WhoCarried.Core;

/// <summary>What one player gave another (see <see cref="RunStats.RecordSupport"/>).</summary>
public enum SupportKind { Energy, Cards, Block, Buffs, Draws }

public sealed class SourceTotal
{
    public SourceKind Kind { get; set; }
    public string Label { get; set; } = "";

    /// <summary>HP removed (for damage sources), or the plain count/amount for everything else.</summary>
    public int Amount { get; set; }

    /// <summary>Enemy block this source knocked off (damage sources only). Shown separately, never added to damage.</summary>
    public int BlockRemoved { get; set; }
}

/// <summary>A badge the game gave a player when the run ended. Rarity is "gold", "silver" or "bronze".</summary>
public sealed class EarnedBadge
{
    public string Id { get; set; } = "";
    public string Rarity { get; set; } = "";
}

/// <summary>What an enemy debuff on a player cost them: extra damage taken, damage not dealt, or block not gained.</summary>
public sealed class CostTotal
{
    public string Label { get; set; } = "";
    public string Effect { get; set; } = "";
    public int Amount { get; set; }
}

public sealed class PlayerTotals
{
    /// <summary>HP this player removed from enemies.</summary>
    public int DamageDealt { get; set; }

    /// <summary>Enemy block this player knocked off. Shown separately, never added to damage dealt.</summary>
    public int BlockRemoved { get; set; }

    /// <summary>Damage this player's own block absorbed.</summary>
    public int Blocked { get; set; }

    /// <summary>
    /// HP this player's pets (Osty) lost to enemies: hits they soaked, for their owner or on their own. Not part of the
    /// owner's damage taken, which is only the owner's own HP.
    /// </summary>
    public int PetTanked { get; set; }

    public Dictionary<string, SourceTotal> Sources { get; set; } = new();

    /// <summary>Debuff stacks this player put on enemies, by power.</summary>
    public Dictionary<string, SourceTotal> DebuffsApplied { get; set; } = new();

    /// <summary>Debuff stacks enemies put on this player, by power.</summary>
    public Dictionary<string, SourceTotal> DebuffsReceived { get; set; } = new();

    /// <summary>
    /// Extra damage teammates dealt because of damage-boosting debuffs (Vulnerable) this player applied, by power.
    /// Already inside the teammates' own damage; shown alongside, never added to team totals.
    /// </summary>
    public Dictionary<string, SourceTotal> DebuffBonus { get; set; } = new();

    /// <summary>HP this player's debuffs (Weak, Strength loss) kept off the team, themselves included, by power.</summary>
    public Dictionary<string, SourceTotal> DebuffPrevented { get; set; } = new();

    /// <summary>Cards this player created during fights (Souls, Shivs…), by card.</summary>
    public Dictionary<string, SourceTotal> CardsCreated { get; set; } = new();

    /// <summary>What enemy debuffs on this player cost them, by effect and power.</summary>
    public Dictionary<string, CostTotal> DebuffCosts { get; set; } = new();

    /// <summary>Energy this player gave teammates.</summary>
    public int EnergyGiven { get; set; }

    /// <summary>Cards this player created into teammates' piles. Also counted in <see cref="CardsCreated"/>.</summary>
    public int CardsGiven { get; set; }

    /// <summary>Block this player gave teammates, after the game's modifiers.</summary>
    public int BlockGiven { get; set; }

    /// <summary>Buff stacks (Strength, Dexterity…) this player put on teammates, all powers together.</summary>
    public int BuffsGiven { get; set; }

    /// <summary>Cards teammates drew because of this player, their own hand draw not included.</summary>
    public int CardsDrawnForTeam { get; set; }

    /// <summary>How much of one kind of help this player gave teammates.</summary>
    public int Given(SupportKind kind) => kind switch
    {
        SupportKind.Energy => EnergyGiven,
        SupportKind.Cards => CardsGiven,
        SupportKind.Block => BlockGiven,
        SupportKind.Buffs => BuffsGiven,
        SupportKind.Draws => CardsDrawnForTeam,
        _ => 0,
    };

    /// <summary>The most HP one hit of this player's removed, and what dealt it.</summary>
    public int BiggestHit { get; set; }
    public string BiggestHitLabel { get; set; } = "";

    /// <summary>The lowest HP this player dropped to and lived (as a share of max HP), and their max HP then; 0 = none seen.</summary>
    public int LowestHp { get; set; }
    public int LowestHpMax { get; set; }

    /// <summary>The game's end-of-run badges for this player; empty until the run ends.</summary>
    public List<EarnedBadge> Badges { get; set; } = new();
}

public sealed class FightBucket
{
    public int Act { get; set; }
    public int Floor { get; set; }
    public string Label { get; set; } = "";

    /// <summary>The map room it was in: "monster", "elite", "boss", "unknown" (an event fight), or "" when not known.</summary>
    public string Room { get; set; } = "";

    /// <summary>Damage dealt (HP removed) in this fight, by player key.</summary>
    public Dictionary<string, int> DamageByPlayer { get; set; } = new();
}

/// <summary>Everything the mod has counted for one run. Serialized as-is to current_run.dat.</summary>
public sealed class RunStats
{
    public const string UnattributedKey = "unattributed";

    /// <summary>Cost effects (see <see cref="CostTotal.Effect"/>).</summary>
    public const string CostTaken = "taken", CostDealt = "dealt", CostBlock = "block";

    public string RunKey { get; set; } = "";
    public bool Finished { get; set; }
    public bool? Victory { get; set; }
    public Dictionary<string, PlayerTotals> Players { get; set; } = new();
    public List<FightBucket> Fights { get; set; } = new();

    private FightBucket? _openFight;

    public static string KeyFor(ulong? playerId) =>
        playerId.HasValue ? playerId.Value.ToString(CultureInfo.InvariantCulture) : UnattributedKey;

    public PlayerTotals? Get(ulong? playerId) => Players.GetValueOrDefault(KeyFor(playerId));

    public void BeginFight(int act, int floor, string label, string room = "")
    {
        _openFight = new FightBucket { Act = act, Floor = floor, Label = label, Room = room };
        Fights.Add(_openFight);
    }

    public void EndFight() => _openFight = null;

    /// <param name="hp">HP removed.</param>
    /// <param name="blocked">Enemy block the hit knocked off.</param>
    public void RecordDamage(ulong? playerId, SourceRef source, int hp, int blocked = 0)
    {
        hp = Math.Max(0, hp);
        blocked = Math.Max(0, blocked);
        if (hp == 0 && blocked == 0) return;
        string key = KeyFor(playerId);
        PlayerTotals totals = GetOrAdd(key);
        totals.DamageDealt += hp;
        totals.BlockRemoved += blocked;
        SourceTotal total = Entry(totals.Sources, source);
        total.Amount += hp;
        total.BlockRemoved += blocked;
        if (hp > totals.BiggestHit)
        {
            totals.BiggestHit = hp;
            totals.BiggestHitLabel = source.Label;
        }
        if (_openFight != null && hp > 0)
            _openFight.DamageByPlayer[key] = _openFight.DamageByPlayer.GetValueOrDefault(key) + hp;
    }

    public void RecordBlocked(ulong playerId, int amount)
    {
        if (amount <= 0) return;
        GetOrAdd(KeyFor(playerId)).Blocked += amount;
    }

    /// <summary>HP a player's pet lost to an enemy's hit.</summary>
    public void RecordPetTanked(ulong playerId, int hp)
    {
        if (hp <= 0) return;
        GetOrAdd(KeyFor(playerId)).PetTanked += hp;
    }

    /// <summary>
    /// A player's HP after taking a hit (or at the end of a floor). Keeps the lowest share of max HP they lived
    /// through; returns true when this is a new low. 0 HP (dead) doesn't count.
    /// </summary>
    public bool RecordHp(ulong playerId, int hp, int maxHp)
    {
        if (hp <= 0 || maxHp <= 0) return false;
        PlayerTotals totals = GetOrAdd(KeyFor(playerId));
        // hp / maxHp < lowest / lowestMax, without dividing.
        if (totals.LowestHpMax > 0 && (long)hp * totals.LowestHpMax >= (long)totals.LowestHp * maxHp) return false;
        totals.LowestHp = hp;
        totals.LowestHpMax = maxHp;
        return true;
    }

    /// <summary>Replaces a player's end-of-run badges.</summary>
    public void SetBadges(ulong playerId, IEnumerable<EarnedBadge> badges) =>
        GetOrAdd(KeyFor(playerId)).Badges = badges.ToList();

    public void RecordDebuffApplied(ulong playerId, SourceRef debuff, int stacks)
    {
        if (stacks <= 0) return;
        Entry(GetOrAdd(KeyFor(playerId)).DebuffsApplied, debuff).Amount += stacks;
    }

    /// <summary>
    /// Adds or takes away applied stacks (Strength loss nets out the part that only lasts a turn). The entry
    /// disappears once it reaches zero.
    /// </summary>
    public void AdjustDebuffApplied(ulong playerId, SourceRef debuff, int delta)
    {
        if (delta == 0) return;
        Dictionary<string, SourceTotal> applied = GetOrAdd(KeyFor(playerId)).DebuffsApplied;
        SourceTotal entry = Entry(applied, debuff);
        entry.Amount += delta;
        if (entry.Amount <= 0) applied.Remove(debuff.Key);
    }

    public void RecordDebuffReceived(ulong playerId, SourceRef debuff, int stacks)
    {
        if (stacks <= 0) return;
        Entry(GetOrAdd(KeyFor(playerId)).DebuffsReceived, debuff).Amount += stacks;
    }

    public void RecordDebuffBonus(ulong playerId, SourceRef debuff, int amount)
    {
        if (amount <= 0) return;
        Entry(GetOrAdd(KeyFor(playerId)).DebuffBonus, debuff).Amount += amount;
    }

    public void RecordDebuffPrevented(ulong playerId, SourceRef debuff, int hp)
    {
        if (hp <= 0) return;
        Entry(GetOrAdd(KeyFor(playerId)).DebuffPrevented, debuff).Amount += hp;
    }

    public void RecordCardCreated(ulong playerId, SourceRef card, int count = 1)
    {
        if (count <= 0) return;
        Entry(GetOrAdd(KeyFor(playerId)).CardsCreated, card).Amount += count;
    }

    /// <summary>
    /// Something <paramref name="giver"/> gave a teammate. Help a player gives themselves isn't support, so it's ignored.
    /// </summary>
    public void RecordSupport(ulong giver, ulong recipient, SupportKind kind, int amount)
    {
        if (giver == recipient || amount <= 0) return;
        PlayerTotals totals = GetOrAdd(KeyFor(giver));
        switch (kind)
        {
            case SupportKind.Energy: totals.EnergyGiven += amount; break;
            case SupportKind.Cards: totals.CardsGiven += amount; break;
            case SupportKind.Block: totals.BlockGiven += amount; break;
            case SupportKind.Buffs: totals.BuffsGiven += amount; break;
            case SupportKind.Draws: totals.CardsDrawnForTeam += amount; break;
        }
    }

    /// <param name="effect"><see cref="CostTaken"/>, <see cref="CostDealt"/> or <see cref="CostBlock"/>.</param>
    public void RecordDebuffCost(ulong playerId, SourceRef debuff, string effect, int amount)
    {
        if (amount <= 0) return;
        Dictionary<string, CostTotal> costs = GetOrAdd(KeyFor(playerId)).DebuffCosts;
        string key = $"{effect}:{debuff.Key}";
        if (!costs.TryGetValue(key, out CostTotal? cost))
        {
            cost = new CostTotal { Label = debuff.Label, Effect = effect };
            costs[key] = cost;
        }
        cost.Amount += amount;
    }

    private static SourceTotal Entry(Dictionary<string, SourceTotal> into, SourceRef source)
    {
        if (!into.TryGetValue(source.Key, out SourceTotal? total))
        {
            total = new SourceTotal { Kind = source.Kind, Label = source.Label };
            into[source.Key] = total;
        }
        return total;
    }

    private PlayerTotals GetOrAdd(string key)
    {
        if (!Players.TryGetValue(key, out PlayerTotals? totals))
        {
            totals = new PlayerTotals();
            Players[key] = totals;
        }
        return totals;
    }
}
