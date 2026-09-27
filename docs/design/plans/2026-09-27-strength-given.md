# Damage from Strength you gave — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Credit players with the extra damage teammates dealt because of Strength they gave them, as bonus damage beside Vulnerable's, counted for Enabler.

**Architecture:** `Game/StrengthGifts.cs` keeps a per-creature ledger of lasting Strength other players gave, fed from `Tracker.OnPowerChanged`, and reads temporary Strength buffs live. At `BeforeDamageReceived` on an enemy it stores a pending gifted hit (in `Fight.Now`, like `BoostedHits`). At `AfterDamageGiven`, Core's `StrengthGift` works out the HP and splits it by giver. The credit lands in a new `PlayerTotals.BuffBonus`, which the scoreboard bonus, Enabler and the summary note add to `DebuffBonus`.

**Tech Stack:** C# / .NET 9, Harmony, the game's API, the repo's console test runner.

**Spec:** [Damage from Strength you gave](../specs/2026-09-27-strength-given-design.md).

## Global constraints

- Branch `feature/strength-given` from `main`. Commit after each task. Don't push. When done, squash-merge into `main` as one commit, subject `Strength you give teammates counts as your bonus damage`.
- Build and test with `"/c/Program Files/dotnet/dotnet.exe"`; build against both game versions (default and `-p:GameData="E:/Claude/sts2-refs/v0.107.1"`).
- Vanilla `StrengthPower` and `TemporaryStrengthPower` may be named (the tracker already does). No card, potion or mod is named.
- Every new or changed English string gets its `zhs.json` counterpart in the same commit.
- Only read game state.
- Log line: `{giver} +{n} buff bonus via {id} ({label}) on {hitter}'s hit (+{s} Strength, x{m}, {hp} hp)`. It must not match the existing `Bonus` pattern (`\+(\d+) bonus via`), and `LogReplay` must read it into `BuffBonus`.
- If `bug/infinite-hp-phase` landed first, the gifted-hit credit goes below its `Attribution.Counts` early return in `OnDamage`, and the pending gifted hit is still taken (cleared) before that return.

## Review focus

1. **Temporary Strength counted twice.** Coordinate or Flex Potion adds Strength through a `StrengthPower` change with the teammate as applier, and also exists as a buff read live. Task 2 subtracts the temporary buff's amount from the lasting ledger when it lands. A reviewer checks both arrive (order doesn't matter) and net out to zero in the ledger.
2. **The wear-off.** Temporary Strength ends with the player as applier. It must not reduce the teammate's lasting gift or credit anyone. Task 2 only substitutes a running teammate power for *positive* self-applied changes.
3. **Gifted Strength above actual Strength** (the player lost Strength since). Task 1 tests scaling, and 0 or negative Strength giving nothing.
4. **A pet attacking** (Osty) with Strength given to Osty by a teammate. The ledger is per creature, and the hitter is the pet's owner. A gift the owner gave their own pet is self-help and never recorded. A reviewer checks `PlayerIdOf` is used on both sides.
5. **Vulnerable and Strength on one hit** must not claim more than their joint effect. Task 1 tests it; Task 3 passes the pending debuff amplifiers' product so Strength's multiplier leaves them out.

## Files

| File | Change |
|---|---|
| `src/WhoCarried/Core/StrengthGift.cs` | New: scale gifts to real Strength; the HP bonus |
| `src/WhoCarried/Core/RunStats.cs` | `PlayerTotals.BuffBonus`; `RecordBuffBonus`; `PlayerTotals.BonusDamage` |
| `src/WhoCarried/Core/LogReplay.cs` | `BuffBonus` pattern and `BuffBonusLine` |
| `src/WhoCarried/Core/RecapBuilder.cs` | Overview bonus and summary note from both |
| `src/WhoCarried/Core/AwardBuilder.cs` | Enabler from both |
| `src/WhoCarried/Game/StrengthGifts.cs` | New: the ledger and live temporary gifts |
| `src/WhoCarried/Game/Fight.cs` | `GiftedHits` |
| `src/WhoCarried/Game/Tracker.cs` | Feed the ledger; pending gifted hit; credit it |
| `src/WhoCarried/UI/DevPreview.cs` | Sample Strength bonus |
| `src/WhoCarried/Localization/eng.json`, `zhs.json` | Nothing new unless a string changes (see Task 2) |
| `tests/WhoCarried.Tests/StrengthGiftTests.cs` (new), `AwardTests.cs`, `ReplayTests.cs` | Tests |
| `README.md`, `CHANGELOG.md` | Docs |

---

### Task 1: The arithmetic in Core

**Files:**
- Create: `src/WhoCarried/Core/StrengthGift.cs`, `tests/WhoCarried.Tests/StrengthGiftTests.cs`

**Interfaces:**
- Produces:
  - `public static IReadOnlyList<(ulong Giver, decimal Strength)> StrengthGift.Scale(IReadOnlyList<(ulong Giver, int Strength)> gifts, int actualStrength)`
  - `public static int StrengthGift.Bonus(decimal amount, decimal gifted, decimal multiplier, int blocked, int hpRemoved)`

- [ ] **Step 1: Write the failing tests**

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class StrengthGiftTests
{
    [Test]
    public static void GiftsCountUpToTheAttackersRealStrength()
    {
        var gifts = new[] { ((ulong)2, 3), ((ulong)3, 2) };
        var full = StrengthGift.Scale(gifts, actualStrength: 10);
        Check.Equal(3m, full[0].Strength, "all of Ash's 3");
        Check.Equal(2m, full[1].Strength, "all of Jo's 2");
        var cut = StrengthGift.Scale(gifts, actualStrength: 4);
        Check.Equal(4m, cut.Sum(g => g.Strength), "no more than the 4 they have");
        Check.Near(2.4, (double)cut[0].Strength, "in proportion");
        Check.Equal(0, StrengthGift.Scale(gifts, actualStrength: 0).Count, "no Strength, no gift");
        Check.Equal(0, StrengthGift.Scale(gifts, actualStrength: -2).Count, "below zero, no gift");
    }

    [Test]
    public static void TheBonusIsTheHpTheGiftAdded()
    {
        // 10 base + 3 gifted Strength.
        Check.Equal(3, StrengthGift.Bonus(13m, 3m, 1m, blocked: 0, hpRemoved: 13), "plain");
        Check.Equal(3, StrengthGift.Bonus(13m, 3m, 1m, blocked: 11, hpRemoved: 2), "past block: 2 HP vs 0");
        // Killing blow: the enemy had 2 HP left, and 10 without the gift still removes both.
        Check.Equal(0, StrengthGift.Bonus(13m, 3m, 1m, blocked: 0, hpRemoved: 2), "killing blow");
        Check.Equal(0, StrengthGift.Bonus(13m, 0m, 1m, blocked: 0, hpRemoved: 13), "no gift");
    }

    [Test]
    public static void VulnerableAndStrengthDontClaimTheSameHp()
    {
        // (10 base + 3 gifted) × 1.5 Vulnerable = 19.5 → 19 HP. Without both: 10.
        decimal amount = 19.5m;
        int hp = 19;
        int vulnerable = DebuffBonus.Bonus(amount, 1.5m, blocked: 0, hpRemoved: hp);          // 19 - 13 = 6
        int strength = StrengthGift.Bonus(amount, 3m, multiplier: 1m, blocked: 0, hpRemoved: hp); // Vulnerable left out: 19 - 16 = 3
        Check.Equal(hp - 10, vulnerable + strength, "together they're the whole difference");
    }
}
```

- [ ] **Step 2: Run to verify they fail** — build error, `StrengthGift` doesn't exist.

- [ ] **Step 3: Implement `Core/StrengthGift.cs`**

```csharp
namespace WhoCarried.Core;

/// <summary>
/// Strength one player gave another, and the HP it added to their attacks: bonus damage for the giver, like the
/// extra damage their Vulnerable sets up. Pure arithmetic; the game layer keeps the ledger.
/// </summary>
public static class StrengthGift
{
    /// <summary>
    /// Gifts as they count on this hit: all of them while the attacker has at least that much Strength, scaled down in
    /// proportion when they have less, none at 0 or below.
    /// </summary>
    public static IReadOnlyList<(ulong Giver, decimal Strength)> Scale(IReadOnlyList<(ulong Giver, int Strength)> gifts, int actualStrength)
    {
        int total = gifts.Where(g => g.Strength > 0).Sum(g => g.Strength);
        if (total <= 0 || actualStrength <= 0) return Array.Empty<(ulong, decimal)>();
        decimal scale = Math.Min(1m, (decimal)actualStrength / total);
        return gifts.Where(g => g.Strength > 0).Select(g => (g.Giver, g.Strength * scale)).ToList();
    }

    /// <summary>
    /// Extra HP the gifted Strength added: HP removed minus HP the hit would have removed with the gift × the
    /// hit's other multipliers taken off, after the same block. Killing blows get only what the gift made the difference for.
    /// </summary>
    /// <param name="amount">The hit's final damage, before block.</param>
    /// <param name="multiplier">The hit's multipliers, less the debuffs whose own bonus is credited.</param>
    public static int Bonus(decimal amount, decimal gifted, decimal multiplier, int blocked, int hpRemoved)
    {
        if (amount <= 0m || gifted <= 0m || hpRemoved <= 0) return 0;
        decimal without = Math.Max(0m, amount - gifted * multiplier);
        int hpWithout = Math.Min((int)Math.Max(without - blocked, 0m), hpRemoved);
        return Math.Max(0, hpRemoved - hpWithout);
    }
}
```

- [ ] **Step 4: Run the tests** — pass.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/StrengthGift.cs tests/WhoCarried.Tests/StrengthGiftTests.cs
git commit -m "feat: work out the damage Strength a teammate gave added"
```

---

### Task 2: Buff bonus in the stats, the scoreboard and Enabler

**Files:**
- Modify: `src/WhoCarried/Core/RunStats.cs`, `LogReplay.cs`, `RecapBuilder.cs`, `AwardBuilder.cs`
- Test: `tests/WhoCarried.Tests/AwardTests.cs`, `ReplayTests.cs`

**Interfaces:**
- Produces: `PlayerTotals.BuffBonus` (`Dictionary<string, SourceTotal>`); `PlayerTotals.BonusDamage` (int); `RunStats.RecordBuffBonus(ulong playerId, SourceRef buff, int amount)`; `LogReplay.BuffBonusLine(string giver, SourceRef buff, int amount, string hitter, string detail)`

- [ ] **Step 1: Write the failing tests**

`AwardTests.cs`:

```csharp
    [Test]
    public static void StrengthGivenCountsForEnabler()
    {
        var s = new RunStats();
        var strength = new SourceRef(SourceKind.Power, "STRENGTH_POWER", "Strength");
        var vulnerable = new SourceRef(SourceKind.Power, "VULNERABLE_POWER", "Vulnerable");
        s.RecordDamage(1, new SourceRef(SourceKind.Card, "STRIKE", "Strike"), 50);
        s.RecordDamage(2, new SourceRef(SourceKind.Card, "STRIKE", "Strike"), 40);
        s.RecordDebuffBonus(1, vulnerable, 10);
        s.RecordBuffBonus(2, strength, 15);
        Check.Equal(15, s.Get(2)!.BonusDamage, "buff bonus is bonus damage");
        IReadOnlyList<Award> a = AwardBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>());
        Check.Equal("Bob", Find(a, AwardBuilder.Enabler)!.PlayerName, "Strength alone wins it");
        Check.Equal("+15", Find(a, AwardBuilder.Enabler)!.Value, "value");
        Check.True(Find(a, AwardBuilder.Enabler)!.Detail.Contains("Strength"), "detail names Strength");
    }
```

`ReplayTests.cs`:

```csharp
    [Test]
    public static void ABuffBonusReplaysAsBuffBonusNotDebuffBonus()
    {
        var strength = new SourceRef(SourceKind.Power, "STRENGTH_POWER", "Strength");
        string line = LogReplay.BuffBonusLine("Ash", strength, 4, "Moth", "+3 Strength, x1, 13 hp");
        Check.Equal("Ash +4 buff bonus via STRENGTH_POWER (Strength) on Moth's hit (+3 Strength, x1, 13 hp)", line, "line");
        string[] log = { "player 1 = Ash (The Regent) #d85a30", "[F3 A1] fight start: Cultist", "[F3 A1] " + line };
        PlayerTotals ash = LogReplay.Parse(log).Stats.Get(1)!;
        Check.Equal(4, ash.BuffBonus["Power:STRENGTH_POWER"].Amount, "buff bonus");
        Check.Equal(0, ash.DebuffBonus.Count, "not a debuff bonus");
    }
```

- [ ] **Step 2: Run to verify they fail** — build errors.

- [ ] **Step 3: Implement**

`RunStats.cs`, in `PlayerTotals` after `DebuffBonus`:

```csharp
    /// <summary>
    /// Extra damage teammates dealt because of buffs this player gave them (Strength), by power. Already inside the
    /// teammates' own damage; shown alongside, never added to team totals.
    /// </summary>
    public Dictionary<string, SourceTotal> BuffBonus { get; set; } = new();

    /// <summary>All the bonus damage this player set up for teammates: debuffs' and buffs'.</summary>
    [JsonIgnore]
    public int BonusDamage => DebuffBonus.Values.Sum(b => b.Amount) + BuffBonus.Values.Sum(b => b.Amount);
```

(Add `using System.Text.Json.Serialization;` at the top. System.Text.Json writes get-only public properties, so without `[JsonIgnore]` every save would carry a stale copy of the sum.)

In `RunStats`:

```csharp
    public void RecordBuffBonus(ulong playerId, SourceRef buff, int amount)
    {
        if (amount <= 0) return;
        Entry(GetOrAdd(KeyFor(playerId)).BuffBonus, buff).Amount += amount;
    }
```

`LogReplay.cs`:

```csharp
    private static readonly Regex BuffBonus = new(Where + @"(.+?) \+(\d+) buff bonus via (\S+) \((.*)\) on ", RegexOptions.Compiled);

    public static string BuffBonusLine(string giver, SourceRef buff, int amount, string hitter, string detail) =>
        $"{giver} +{amount} buff bonus via {buff.Id} ({buff.Label}) on {hitter}'s hit ({detail})";
```

and in `Parse`, beside the `Bonus` branch:

```csharp
            else if ((m = BuffBonus.Match(line)).Success)
            {
                if (Who(m.Groups[3].Value) is ulong id)
                    stats.RecordBuffBonus(id, Power(m.Groups[5].Value, m.Groups[6].Value), Int(m.Groups[4]));
            }
```

`RecapBuilder.cs`:
- in the overview, replace `stats.Get(p.NetId)?.DebuffBonus.Values.Sum(b => b.Amount) ?? 0` with `stats.Get(p.NetId)?.BonusDamage ?? 0`;
- replace `Note(stats, players, t => t.DebuffBonus, "WHO_CARRIED.summary.bonus_note")` with `Note(stats, players, t => Both(t), "WHO_CARRIED.summary.bonus_note")`, adding:

```csharp
    /// <summary>A player's debuff and buff bonuses together, for the lines that name them.</summary>
    private static Dictionary<string, SourceTotal> Both(PlayerTotals t) =>
        t.DebuffBonus.Concat(t.BuffBonus).ToDictionary(kv => kv.Key, kv => kv.Value);
```

`AwardBuilder.cs`, Enabler (lines 85–88):

```csharp
        PlayerInfo? enabler = team ? Most(byRank, p => T(p)?.BonusDamage ?? 0) : null;
        if (enabler != null)
            Give(Enabler, enabler, "+" + Num(T(enabler)!.BonusDamage),
                Loc.Text("WHO_CARRIED.award.enabler_detail",
                    RecapBuilder.JoinAnd(Labels(T(enabler)!.DebuffBonus).Concat(Labels(T(enabler)!.BuffBonus)).ToList())));
```

This replaces the three lines starting `PlayerInfo? enabler = team ? Most(byRank, p => Sum(T(p)?.DebuffBonus))`. `Most` already returns null when nobody has any, as it does today.

The Enabler detail ("damage teammates gained from their {0}") and the summary note ("…thanks to your {0}") read correctly with "Vulnerable and Strength", so no strings change. Check the `zhs.json` versions read correctly with a buff in the list too. If the Chinese says "debuffs" (减益) explicitly, change it to a neutral word in both files.

- [ ] **Step 4: Run all tests** — pass.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core tests/WhoCarried.Tests src/WhoCarried/Localization
git commit -m "feat: bonus damage from buffs counts with Vulnerable's, and for Enabler"
```

---

### Task 3: Track gifted Strength in the game

**Files:**
- Create: `src/WhoCarried/Game/StrengthGifts.cs`
- Modify: `src/WhoCarried/Game/Fight.cs`, `src/WhoCarried/Game/Tracker.cs`, `src/WhoCarried/UI/DevPreview.cs`

**Interfaces:**
- Consumes: `StrengthGift.Scale`, `StrengthGift.Bonus` (Task 1); `RunStats.RecordBuffBonus`, `LogReplay.BuffBonusLine` (Task 2)
- Produces: `StrengthGifts.OnPowerChanged(PowerModel power, decimal amount, Creature? applier)`; `StrengthGifts.At(Creature attacker)` → `IReadOnlyList<(ulong Giver, decimal Strength)>`; `Fight.Now.GiftedHits`

- [ ] **Step 1: `Game/StrengthGifts.cs`**

```csharp
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>
/// Strength players gave each other, per creature: lasting Strength in a ledger, temporary Strength read live from the
/// buffs on the attacker. Mirrors how Strength taken off enemies is tracked. Only reads game state.
/// </summary>
internal static class StrengthGifts
{
    private static readonly ConditionalWeakTable<Creature, Dictionary<ulong, int>> Lasting = new();

    /// <summary>A power's amount changed (see <see cref="Tracker.OnPowerChanged"/>).</summary>
    public static void OnPowerChanged(PowerModel power, decimal amount, Creature? applier)
    {
        Creature? owner = power.Owner;
        if (owner == null || owner.IsEnemy || FactsExtractor.PlayerIdOf(owner) is not ulong recipient) return;
        int change = (int)Math.Round(amount);
        if (change == 0 || Giver(applier, recipient, owner, change) is not ulong giver) return;
        if (power is StrengthPower) Adjust(owner, giver, change);
        // Its Strength arrives as a StrengthPower change too; the buff itself is read live, so take it back out here.
        else if (power is TemporaryStrengthPower && power.Type == PowerType.Buff && change > 0) Adjust(owner, giver, -change);
    }

    /// <summary>
    /// Who gave this change: another player named as applier; or, for a positive change the player gave themselves,
    /// a teammate's buff on them acting at a turn boundary (Ritual from a potion thrown at them).
    /// </summary>
    private static ulong? Giver(Creature? applier, ulong recipient, Creature owner, int change)
    {
        if (FactsExtractor.PlayerIdOf(applier) is ulong named && named != recipient) return named;
        if (change <= 0) return null;
        return EffectSources.Running is PowerModel acting && acting.Owner == owner &&
               FactsExtractor.PlayerIdOf(acting.Applier) is ulong teammate && teammate != recipient
            ? teammate
            : null;
    }

    private static void Adjust(Creature owner, ulong giver, int delta)
    {
        Dictionary<ulong, int> byGiver = Lasting.GetOrCreateValue(owner);
        byGiver[giver] = byGiver.GetValueOrDefault(giver) + delta;
    }

    /// <summary>Gifted Strength on this attacker as it counts now: lasting and temporary, scaled to their real Strength.</summary>
    public static IReadOnlyList<(ulong Giver, decimal Strength)> At(Creature attacker)
    {
        if (FactsExtractor.PlayerIdOf(attacker) is not ulong self) return Array.Empty<(ulong, decimal)>();
        var gifts = new Dictionary<ulong, int>();
        if (Lasting.TryGetValue(attacker, out Dictionary<ulong, int>? lasting))
            foreach ((ulong giver, int n) in lasting) gifts[giver] = gifts.GetValueOrDefault(giver) + n;
        foreach (PowerModel p in attacker.Powers.Where(p => p is TemporaryStrengthPower && p.Type == PowerType.Buff && p.Amount > 0))
            if (FactsExtractor.PlayerIdOf(p.Applier) is ulong giver && giver != self)
                gifts[giver] = gifts.GetValueOrDefault(giver) + p.Amount;
        return StrengthGift.Scale(gifts.Select(kv => (kv.Key, kv.Value)).ToList(), attacker.GetPowerAmount<StrengthPower>());
    }
}
```

On v0.107.1 `GetPowerAmount<T>` exists on `Creature` (`UndyingSigil` uses it on both). If the build says otherwise, use `attacker.GetPower<StrengthPower>()?.Amount ?? 0`.

- [ ] **Step 2: `Fight.cs`**

```csharp
    /// <summary>Hits carrying Strength teammates gave the attacker, waiting for their result (<see cref="StrengthGifts"/>).</summary>
    public Dictionary<Creature, (decimal Amount, IReadOnlyList<(ulong Giver, decimal Strength)> Gifts, decimal Multiplier)> GiftedHits { get; }
        = new(ReferenceEqualityComparer.Instance);
```

- [ ] **Step 3: `Tracker`**

In `OnPowerChanged`, right after `TrackStrengthLoss(power, amount, applier);`:

```csharp
        StrengthGifts.OnPowerChanged(power, amount, applier);
```

In `OnBeforeDamage`, as the first lines of the method, before any `return`:

```csharp
        Fight.Now.GiftedHits.Remove(target);
        if (target.IsEnemy && dealer != null && amount > 0m && props.IsPoweredAttack() && _run != null &&
            StrengthGifts.At(dealer) is { Count: > 0 } gifts)
            Fight.Now.GiftedHits[target] = (amount, gifts, DebuffBonusTracker.DamageMultiplier(_run, target, dealer, props, cardSource));
```

In `OnDamage`, after `DebuffBonusTracker.PendingHit? boosted = DebuffBonusTracker.Take(target);`:

```csharp
        Fight.Now.GiftedHits.Remove(target, out var gifted);
```

and inside `if (facts.TargetIsEnemy)`, after the `if (boosted != null) CreditDebuffBonus(...)` line:

```csharp
            if (gifted.Gifts != null) CreditStrengthGift(gifted, boosted, facts, who.PlayerId);
```

and add:

```csharp
    private static SourceRef StrengthGiven => new(SourceKind.Power, "STRENGTH_POWER",
        GameText.Native("powers", "STRENGTH_POWER.title", "Strength"));

    /// <summary>
    /// The extra HP Strength teammates gave the hitter added to this hit, shared by giver. The debuffs whose own bonus is
    /// credited (Vulnerable) are left out of Strength's multiplier, so the two never claim the same HP.
    /// </summary>
    private static void CreditStrengthGift((decimal Amount, IReadOnlyList<(ulong Giver, decimal Strength)> Gifts, decimal Multiplier) hit,
                                           DebuffBonusTracker.PendingHit? boosted, DamageFacts facts, ulong? hitter)
    {
        decimal debuffs = boosted?.Amplifiers.Aggregate(1m, (p, a) => p * a.Multiplier) ?? 1m;
        decimal multiplier = debuffs > 0m ? hit.Multiplier / debuffs : hit.Multiplier;
        decimal total = hit.Gifts.Sum(g => g.Strength);
        int bonus = StrengthGift.Bonus(hit.Amount, total, multiplier, facts.Blocked, facts.HpRemoved);
        int[] shares = DebuffBonus.SplitIndexed(bonus, hit.Gifts.Select(g => g.Strength).ToList());
        for (int i = 0; i < hit.Gifts.Count; i++)
        {
            ulong giver = hit.Gifts[i].Giver;
            if (shares[i] <= 0 || giver == hitter) continue;
            _stats.RecordBuffBonus(giver, StrengthGiven, shares[i]);
            _log?.Write($"{Where} " + LogReplay.BuffBonusLine(NameOf(giver), StrengthGiven, shares[i], NameOf(hitter),
                $"+{hit.Gifts[i].Strength:0.##} Strength, x{multiplier:0.##}, {facts.HpRemoved} hp"));
        }
    }
```

- [ ] **Step 4: DevPreview** — with the sample Vulnerable bonuses (around line 668):

```csharp
                if (fight % 3 == 0) stats.RecordBuffBonus(P(1).NetId, new SourceRef(SourceKind.Power, "STRENGTH_POWER", "Strength"), rng.Next(2, 9) * act);
```

- [ ] **Step 5: Build both versions, run the tests** — clean, all pass.

- [ ] **Step 6: Commit**

```bash
git add src/WhoCarried/Game src/WhoCarried/UI/DevPreview.cs
git commit -m "feat: credit damage from Strength given to teammates"
```

---

### Task 4: Docs, preview, a real run

- [ ] **Step 1: Changelog** — `[Unreleased]` → `### Added`:

```markdown
- Strength you give a teammate (Blaze, Coordinate, a Strength or Flex Potion thrown at them, Mazaleth's Gift) now counts the extra damage it adds to their attacks as your **bonus damage**, like your Vulnerable does, and for **Enabler**.
```

- [ ] **Step 2: README** — in the Scoreboard bullet, "the bonus damage their Vulnerable set up for teammates" becomes "the bonus damage their Vulnerable and the Strength they gave set up for teammates".

- [ ] **Step 3: Commit** `git commit -am "docs: Strength given counts as bonus damage"`

- [ ] **Step 4: Preview.** Deploy and run the dev preview only if the game isn't running; ask before launching. The scoreboard shows the sample bonus, and the Enabler award reads "…from their Vulnerable and Strength" where both appear.

- [ ] **Step 5: A real co-op run:** a Strength Potion thrown at a teammate, then their attacks, once on a Vulnerable enemy. `events.log` shows `buff bonus via STRENGTH_POWER` lines whose `+N Strength` matches the gift, and a hit with both Vulnerable and Strength shows both lines without their sum going over the hit's HP.

- [ ] **Step 6: Finish.** Report to the owner. When they're happy, squash-merge into `main`, subject `Strength you give teammates counts as your bonus damage`, then run `git branch -D feature/strength-given`. Don't push.
