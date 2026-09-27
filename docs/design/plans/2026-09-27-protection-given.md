# Damage prevented for teammates — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Credit players for damage their buffs kept off teammates (Intercept's Covered, Tank's Guarded, and any mod buff that works the same way), as a new Support kind with a new award, Guardian.

**Architecture:** A new `SupportKind.Protection` flows through the existing support path: `RunStats.RecordSupport`, the `gave N protection to` log line, `SupportRow`, the Support tab's kinds, and `AwardBuilder.SupportAwards`. The counting happens in `Tracker.OnBeforeDamage` for enemy hits on players: `DebuffBonusTracker.Protectors` finds teammate-applied buffs with a multiplier below 1, and Core's `Protection` works out and splits the HP they kept off. For a ×0 buff it recalculates the hit through `DebuffBonusTracker.DamageWithout`.

**Tech Stack:** C# / .NET 9, Harmony, the game's API, the repo's console test runner.

**Spec:** [Damage prevented for teammates](../specs/2026-09-27-protection-given-design.md).

## Global constraints

- Branch `feature/protection-given` from `main`. Commit after each task. Don't push. When done, squash-merge into `main` as one commit, subject `Damage prevented for teammates, and the Guardian award`.
- Build and test with `"/c/Program Files/dotnet/dotnet.exe"`; build against both game versions (default and `-p:GameData="E:/Claude/sts2-refs/v0.107.1"`).
- No card, power or mod is named in code. Buffs are found by type (`PowerType.Buff`), applier (another player) and multiplier (below 1).
- Every new English string gets its `zhs.json` entry in the same commit, using the game's own Chinese terms.
- Only read game state; never run game commands.
- The support log line is `{giver} gave {n} protection to {recipient} | {power id}`, and `LogReplay` must read it.
- Help a player gives themselves is never support (`RecordSupport` already drops it).

## Review focus

1. **A ×0 buff when where the hit started wasn't seen** (`baseDamage` null): nothing is credited, rather than a guess. Task 1 tests `Protection.Shares` with `without: null`.
2. **Covered on a teammate the enemy doesn't attack:** there's no hit, so nothing is credited. No code is involved; a reviewer checks nothing is credited outside a hit.
3. **Protection on a pet (Osty)** credits the buff's applier with the pet's owner as recipient. If that's the applier themselves, it's dropped. `FactsExtractor.PlayerIdOf(target)` gives the owner; a reviewer checks it's the victim id used.
4. **Seven kinds of help on the exported image and the copied picture.** `ShareLayout.SupportColumns` and `SummaryCard`'s one-row layout must still fit. Task 3 checks it in the dev preview's export and copy screenshots.
5. **More than sixteen awards.** With Guardian (and Medic, if the healing branch lands) there can be 18. Task 2 extends the award-grid test to 18.

## Files

| File | Change |
|---|---|
| `src/WhoCarried/Core/Protection.cs` | New: undoing the protection, and sharing what it kept off |
| `src/WhoCarried/Core/RunStats.cs` | `SupportKind.Protection`; `PlayerTotals.DamagePreventedForTeam`; `Given`; `RecordSupport` |
| `src/WhoCarried/Core/LogReplay.cs` | `protection` in the `Gave` pattern |
| `src/WhoCarried/Core/RecapBuilder.cs` | `SupportRow.Protection`; fill it |
| `src/WhoCarried/Core/AwardBuilder.cs` | `Guardian`, `MinProtectionGiven`, its `SupportAwards` entry |
| `src/WhoCarried/Game/DebuffBonusTracker.cs` | `Protectors`, `DamageWithout` |
| `src/WhoCarried/Game/Tracker.cs` | `CreditProtection`, called from `OnBeforeDamage` |
| `src/WhoCarried/UI/SupportTab.cs`, `GameArt.cs`, `RecapTexts.cs`, `DevPreview.cs` | The Support card, art, award art, sample data |
| `src/WhoCarried/Localization/eng.json`, `zhs.json` | Three keys |
| `tests/WhoCarried.Tests/ProtectionTests.cs` (new), `SupportTests.cs`, `AwardTests.cs` | Tests |
| `README.md`, `CHANGELOG.md` | Docs |

---

### Task 1: The arithmetic in Core

**Files:**
- Create: `src/WhoCarried/Core/Protection.cs`, `tests/WhoCarried.Tests/ProtectionTests.cs`

**Interfaces:**
- Produces: `public static decimal? Protection.Undo(decimal amount, IReadOnlyList<decimal> multipliers)`; `public static int[] Protection.Shares(decimal amount, decimal? without, IReadOnlyList<decimal> multipliers, int block, int hpCap)`

- [ ] **Step 1: Write the failing tests**

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class ProtectionTests
{
    [Test]
    public static void GuardedHalvesTheHitAndIsCreditedTheOtherHalf()
    {
        Check.Equal<decimal?>(20m, Protection.Undo(10m, new[] { 0.5m }), "the hit without Guarded");
        Check.Equal(10, Protection.Shares(10m, 20m, new[] { 0.5m }, block: 0, hpCap: 50)[0], "10 kept off");
    }

    [Test]
    public static void OnlyHpCountsAfterBlockAndUpToTheirHp()
    {
        Check.Equal(5, Protection.Shares(10m, 20m, new[] { 0.5m }, block: 15, hpCap: 50)[0], "15 block: 5 HP vs 0");
        Check.Equal(2, Protection.Shares(10m, 20m, new[] { 0.5m }, block: 0, hpCap: 12)[0], "12 HP left: 12 vs 10");
    }

    [Test]
    public static void CoveredTakesTheWholeHitWhenItsStartIsKnown()
    {
        Check.True(Protection.Undo(0m, new[] { 0m }) == null, "x0 can't be divided back");
        Check.Equal(18, Protection.Shares(0m, 18m, new[] { 0m }, block: 0, hpCap: 50)[0], "all 18");
        Check.Equal(0, Protection.Shares(0m, null, new[] { 0m }, block: 0, hpCap: 50)[0], "unknown start: nothing");
    }

    [Test]
    public static void SeveralProtectorsShareByHowMuchEachShrankIt()
    {
        int[] zero = Protection.Shares(0m, 18m, new[] { 0m, 0.5m }, block: 0, hpCap: 50);
        Check.Equal(18, zero[0], "x0 takes it all");
        Check.Equal(0, zero[1], "x0.5 gets none beside x0");
        int[] two = Protection.Shares(5m, 20m, new[] { 0.5m, 0.5m }, block: 0, hpCap: 50);
        Check.Equal(15, two.Sum(), "adds up");
        Check.Equal(8, two[0], "tie: the earlier gets the odd point");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- Protection`
Expected: build error, `Protection` doesn't exist.

- [ ] **Step 3: Implement `Core/Protection.cs`**

```csharp
namespace WhoCarried.Core;

/// <summary>
/// Damage a teammate's buff kept off a player (Covered, Guarded): the HP the hit would have removed without those buffs,
/// minus what it removes, after block and up to the player's HP. Pure arithmetic; the game layer finds the buffs.
/// </summary>
public static class Protection
{
    /// <summary>The hit without the protective multipliers; null when one is ×0 and can't be divided back out.</summary>
    public static decimal? Undo(decimal amount, IReadOnlyList<decimal> multipliers)
    {
        decimal product = 1m;
        foreach (decimal m in multipliers)
        {
            if (m <= 0m) return null;
            if (m < 1m) product *= m;
        }
        return amount / product;
    }

    /// <summary>
    /// What the protection kept off, shared between the multipliers: by how much each shrank the hit (logarithm), or,
    /// when any is ×0, evenly between the ×0 ones. Share i goes with multiplier i. All zero when
    /// <paramref name="without"/> is unknown.
    /// </summary>
    public static int[] Shares(decimal amount, decimal? without, IReadOnlyList<decimal> multipliers, int block, int hpCap)
    {
        if (without is not decimal full || full <= amount) return new int[multipliers.Count];
        int kept = DebuffBonus.HpDifference(full, amount, block, hpCap);
        bool anyZero = multipliers.Any(m => m <= 0m);
        List<decimal> weights = multipliers
            .Select(m => anyZero ? (m <= 0m ? 1m : 0m) : m > 0m && m < 1m ? (decimal)-Math.Log((double)m) : 0m)
            .ToList();
        return DebuffBonus.SplitIndexed(kept, weights);
    }
}
```

- [ ] **Step 4: Run the tests** — pass.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/Protection.cs tests/WhoCarried.Tests/ProtectionTests.cs
git commit -m "feat: work out damage a teammate's buff kept off"
```

---

### Task 2: A new kind of support, and the Guardian award

**Files:**
- Modify: `src/WhoCarried/Core/RunStats.cs`, `LogReplay.cs`, `RecapBuilder.cs`, `AwardBuilder.cs`
- Modify: `src/WhoCarried/Localization/eng.json`, `zhs.json`
- Test: `tests/WhoCarried.Tests/SupportTests.cs`, `AwardTests.cs`

**Interfaces:**
- Produces: `SupportKind.Protection`; `PlayerTotals.DamagePreventedForTeam`; `SupportRow.Protection` (last parameter, default 0); `AwardBuilder.Guardian`, `AwardBuilder.MinProtectionGiven = 10`

- [ ] **Step 1: Write the failing tests**

In `SupportTests.cs`:

```csharp
    [Test]
    public static void ProtectionIsAKindOfSupportAndReplays()
    {
        var s = new RunStats();
        s.RecordSupport(1, 2, SupportKind.Protection, 18);
        s.RecordSupport(2, 2, SupportKind.Protection, 5);
        Check.Equal(18, s.Get(1)!.DamagePreventedForTeam, "given");
        Check.Equal(18, s.Get(1)!.Given(SupportKind.Protection), "Given");
        Check.True(s.Get(2) == null || s.Get(2)!.DamagePreventedForTeam == 0, "to yourself isn't support");
        string[] log =
        {
            "player 1 = Ash (The Regent) #d85a30",
            "player 2 = Moth (The Silent) #7fff00",
            "[F3 A1] fight start: Cultist",
            "[F3 A1] " + LogReplay.SupportLine("Ash", "Moth", SupportKind.Protection, 18, "GUARDED_POWER"),
        };
        Check.Equal(18, LogReplay.Parse(log).Stats.Get(1)!.DamagePreventedForTeam, "replayed");
    }
```

In `AwardTests.cs`, add `s.RecordSupport(2, 1, SupportKind.Protection, 20);` to `EachSupportAwardGoesToItsLeader` with:

```csharp
        Check.Equal("Bob", Find(a, AwardBuilder.Guardian)!.PlayerName, "guardian");
        Check.Equal("damage kept off teammates", Find(a, AwardBuilder.Guardian)!.Detail, "guardian detail");
```

add `s.RecordSupport(1, 2, SupportKind.Protection, AwardBuilder.MinProtectionGiven - 1);` to `SupportAwardsNeedARealAmount` and `AwardBuilder.Guardian` to its title list, and change `ManyAwardsFitTheSpreadBothWays` to `Enumerable.Range(11, 8)` (up to 18 awards).

- [ ] **Step 2: Run to verify they fail** — build errors (`SupportKind.Protection`, `DamagePreventedForTeam`, `Guardian` missing).

- [ ] **Step 3: Implement**

`RunStats.cs`:

```csharp
public enum SupportKind { Energy, Cards, Block, Buffs, Draws, Protection }
```

In `PlayerTotals`, after `CardsDrawnForTeam`:

```csharp
    /// <summary>HP this player's buffs (Covered, Guarded) kept off teammates' hits.</summary>
    public int DamagePreventedForTeam { get; set; }
```

`Given`: `SupportKind.Protection => DamagePreventedForTeam,`; `RecordSupport`: `case SupportKind.Protection: totals.DamagePreventedForTeam += amount; break;`

`LogReplay.cs`: in the `Gave` pattern, `(energy|cards|block|buffs|draws|protection)`, and in `SupportWord`'s doc comment add "protection".

`RecapBuilder.cs`:

```csharp
public sealed record SupportRow(string Label, string ColorHex, string? IconKey, string Character,
                                int Energy, int Cards, int Block, int Buffs, int Draws, int Protection = 0)
{
    /// <summary>Whether this player gave a teammate anything at all.</summary>
    public bool Any => Energy + Cards + Block + Buffs + Draws + Protection > 0;
}
```

and in `Support(...)` pass `t?.DamagePreventedForTeam ?? 0` as the last argument.

`AwardBuilder.cs`: add `MinProtectionGiven = 10` to the `Min…` constants, `Guardian = "WHO_CARRIED.award.guardian"` to the titles, and after the Bodyguard entry in `SupportAwards`:

```csharp
        (Guardian, SupportKind.Protection, MinProtectionGiven, "WHO_CARRIED.award.guardian_detail"),
```

`eng.json` (alphabetical with the neighbours):

```json
  "WHO_CARRIED.award.guardian": "Guardian",
  "WHO_CARRIED.award.guardian_detail": "damage kept off teammates",
  "WHO_CARRIED.support.protection": "Damage prevented",
```

`zhs.json`:

```json
  "WHO_CARRIED.award.guardian": "守护者",
  "WHO_CARRIED.award.guardian_detail": "为队友挡下的伤害",
  "WHO_CARRIED.support.protection": "挡下的伤害",
```

- [ ] **Step 4: Run all tests** — pass, including `LocalizationTests` (key parity).

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core src/WhoCarried/Localization tests/WhoCarried.Tests
git commit -m "feat: protection given as a kind of support, with the Guardian award"
```

---

### Task 3: Count it in the game, and show it

**Files:**
- Modify: `src/WhoCarried/Game/DebuffBonusTracker.cs`, `src/WhoCarried/Game/Tracker.cs`
- Modify: `src/WhoCarried/UI/SupportTab.cs`, `GameArt.cs`, `RecapTexts.cs`, `DevPreview.cs`

**Interfaces:**
- Consumes: `Protection.Undo`, `Protection.Shares` (Task 1); `SupportKind.Protection`, `SupportRow.Protection`, `AwardBuilder.Guardian` (Task 2)
- Produces: `DebuffBonusTracker.Protectors(...)`, `DebuffBonusTracker.DamageWithout(...)`

- [ ] **Step 1: `DebuffBonusTracker`**

```csharp
    /// <summary>
    /// Buffs on a player (or pet) that another player applied and that shrink this hit (Covered ×0, Guarded ×0.5),
    /// with who applied each and its multiplier.
    /// </summary>
    public static IReadOnlyList<(PowerModel Power, ulong Giver, decimal Multiplier)> Protectors(Creature target, Creature dealer,
        decimal amount, ValueProp props, CardModel? cardSource, ulong victim)
    {
        var found = new List<(PowerModel, ulong, decimal)>();
        foreach (PowerModel power in target.Powers.ToList())
        {
            if (power.Type != PowerType.Buff || FactsExtractor.PlayerIdOf(power.Applier) is not ulong giver || giver == victim) continue;
            decimal multiplier;
            try { multiplier = GameCompat.DamageMultiplicative(power, target, amount, props, dealer, cardSource); }
            catch (Exception) { continue; }
            if (multiplier >= 0m && multiplier < 1m) found.Add((power, giver, multiplier));
        }
        return found;
    }

    /// <summary>
    /// The game's damage for a hit starting at <paramref name="damage"/> with <paramref name="leaveOut"/> left out:
    /// additive modifiers, every other multiplier, then damage caps, in the game's order. Null if it can't be worked out.
    /// </summary>
    public static decimal? DamageWithout(IRunState run, Creature target, Creature dealer, decimal damage, ValueProp props,
        CardModel? cardSource, IReadOnlyCollection<AbstractModel> leaveOut)
    {
        try
        {
            decimal added = GameCompat.ModifyDamage(run, target.CombatState, target, dealer, damage, props, cardSource,
                ModifyDamageHookType.Additive, CardPreviewMode.None);
            decimal now = added;
            foreach (AbstractModel model in run.IterateHookListeners(target.CombatState))
                if (!leaveOut.Contains(model)) now *= GameCompat.DamageMultiplicative(model, target, now, props, dealer, cardSource);
            return Math.Max(0m, GameCompat.ModifyDamage(run, target.CombatState, target, dealer, Math.Max(0m, now), props,
                cardSource, ModifyDamageHookType.Cap, CardPreviewMode.None));
        }
        catch (Exception) { return null; }
    }
```

`ModifyDamagePatch` only records calls of type `All`, so these calls don't disturb the base-damage bookkeeping.

- [ ] **Step 2: `Tracker`**

In `OnBeforeDamage`, after the Vulnerable-cost `foreach` and before `Touch();`:

```csharp
        CreditProtection(target, amount, baseDamage, props, dealer, cardSource, victim, block, hp);
```

and add:

```csharp
    /// <summary>
    /// Damage a teammate's buff kept off this hit (Covered, Guarded), credited to whoever applied it. A ×0 buff is undone
    /// by working the hit out again from where it started; without that, it gets nothing.
    /// </summary>
    private static void CreditProtection(Creature target, decimal amount, decimal? baseDamage, ValueProp props, Creature dealer,
                                         CardModel? cardSource, ulong victim, int block, int hp)
    {
        var protectors = DebuffBonusTracker.Protectors(target, dealer, amount, props, cardSource, victim);
        if (protectors.Count == 0 || _run == null) return;
        List<decimal> multipliers = protectors.Select(p => p.Multiplier).ToList();
        decimal? without = Protection.Undo(amount, multipliers)
            ?? (baseDamage is decimal start
                ? DebuffBonusTracker.DamageWithout(_run, target, dealer, start, props, cardSource,
                    protectors.Select(p => (AbstractModel)p.Power).ToList())
                : null);
        int[] shares = Protection.Shares(amount, without, multipliers, block, hp);
        for (int i = 0; i < protectors.Count; i++)
            Support(protectors[i].Giver, victim, SupportKind.Protection, shares[i], protectors[i].Power.Id.Entry);
    }
```

- [ ] **Step 3: UI**

`GameArt.cs`: add `Covered = "covered", Intercept = "intercept"` to the names and

```csharp
        [Covered] = Powers + "covered_power.png",
        [Intercept] = Powers + "intercept_power.png",
```

`RecapTexts.AwardArt`: `AwardBuilder.Guardian => GameArt.Get(GameArt.Intercept),`

`SupportTab.Kinds`, after the Block kind:

```csharp
        new("WHO_CARRIED.support.protection", RecapTheme.Relic, r => r.Protection, (_, _) => GameArt.Get(GameArt.Covered),
            AwardBuilder.Guardian),
```

`DevPreview.cs`, beside the other sample `RecordSupport` calls (around line 700):

```csharp
                    stats.RecordSupport(from, to, SupportKind.Protection, help.Next(0, i == 1 ? 14 : 5) * act);
```

- [ ] **Step 4: Build both versions, run the tests** — clean, all pass.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Game src/WhoCarried/UI
git commit -m "feat: count damage teammates' buffs kept off, and show it in Support"
```

---

### Task 4: Docs, preview, a real fight

- [ ] **Step 1: Changelog** — under `[Unreleased]` → `### Added`:

```markdown
- **Damage prevented** in Support: damage your buffs kept off teammates, like Intercept (they take nothing from attacks) and Tank (they take half), with a new award, **Guardian**.
```

- [ ] **Step 2: README** — in "What it shows", in the **Support** bullet, add "damage prevented" to the list of what players gave; in **Awards**, add Guardian and change "sixteen titles" to "seventeen titles" (or the right count if the healing branch landed first).

- [ ] **Step 3: Commit** `git commit -am "docs: damage prevented for teammates"`

- [ ] **Step 4: Preview.** Deploy and run the dev preview only if the game isn't running; ask before launching. Check `preview-5-support.png` shows a Damage prevented card, the awards screenshot shows Guardian with Intercept's icon, and the export and copy screenshots still fit their support row with seven kinds.

- [ ] **Step 5: A real co-op fight** (the owner's next run): Intercept on a teammate an enemy attacks, and Tank. `events.log` shows `gave N protection to … | COVERED_POWER` and `| GUARDED_POWER`, and N matches the damage the hit would have done.

- [ ] **Step 6: Finish.** Report to the owner. When they're happy, squash-merge into `main`, subject `Damage prevented for teammates, and the Guardian award`, then run `git branch -D feature/protection-given`. Don't push.
