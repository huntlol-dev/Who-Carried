# Vanilla attribution fixes — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix three vanilla miscredits: hit caps (Slippery, Hardened Shell) counted as enemy block knocked off; Debilitate, Paper Phrog, Cruelty and Paper Krane's parts of Vulnerable and Weak credited to the Vulnerable or Weak applier; Expose's block strip not counted.

**Architecture:** Each fix is a pure rule in Core with its own tests, plus a small change where the game layer reads the game. Caps: `AbsorbLayers.Measured` asks the `modifiers` list whether a game-owned model acted and drops that reduction on enemies. Amplifiers: a new `Game/VanillaAmplifiers.cs` rebuilds Vulnerable's and Weak's multipliers from the game's public methods into weighted parts, and `Tracker` splits each debuff's HP between its parts with `DebuffBonus.SplitIndexed`. Expose: a new patch on `CreatureCmd.LoseBlock` records the stripped block as a 0-HP hit.

**Tech Stack:** C# / .NET 9, Harmony, the game's API, the repo's console test runner.

**Spec:** [Vanilla attribution fixes](../specs/2026-09-27-vanilla-attribution-fixes-design.md).

> **Implementation correction:** Real Harmony validation found that v0.107.1 has `LoseBlock(Creature creature, decimal amount)`, while v0.111.0 has the four arguments used below. The implemented prefix accepts both forms; the public-version fallback credits only a caller matching a live effect or card context. It also mirrors the command's combat-ending guard and integer rounding of remaining block. See the updated spec. Checked by installing the real prefix with Harmony against the v0.107.1 DLLs (the old four-argument prefix fails there with `Parameter "choiceContext" not found`); the beta DLL can't run outside the game, and a prefix taking `__args` binds to any argument list.

## Global constraints

- Branch `bug/vanilla-attribution` from `main`. Commit after each task. Don't push. When done, squash-merge into `main` as one commit, subject `Fix vanilla credit for Debilitate, hit caps and Expose`.
- Build and test with `"/c/Program Files/dotnet/dotnet.exe"` (the x86 dotnet on PATH can't see the SDK).
- Build against both game versions: default (v0.111.0) and `-p:GameData="E:/Claude/sts2-refs/v0.107.1"`.
- Vanilla types may be named. No other mod's types, names or ids may appear in code, docs or commit messages.
- Only read game state; never run game commands.
- Log lines are the replay's input: Debilitate's credit uses the existing `+N bonus via` and `prevented N via` formats; Expose uses the existing hit format with `0 hp`.
- If `bug/infinite-hp-phase` has been merged first, keep its `Attribution.Counts` early return in `OnDamage` above everything this plan adds, and skip Expose on an enemy where `FactsExtractor.HpInfinite(target)` is true. If it hasn't, add the Expose check when merging the second of the two branches.

## Review focus

1. **The fallback when parts don't add up.** If the parts rebuilt from the game's methods don't reproduce the game's own multiplier (within 0.0001), the whole bonus must go to the debuff as today, not be lost. Task 2 tests `VulnerableParts`/`WeakParts` returning null on a mismatch, and the Tracker uses the old path when they're null.
2. **The hitter's own part.** When the hitter is also the Vulnerable applier, the existing rule already drops their share; the new hitter part (Phrog, Cruelty) is dropped too, and must never be credited to another player. Task 2's Core test pins the split; a reviewer checks that `Tracker` skips parts with no power.
3. **Debilitate with no player stacks left** (applied before a Save & Quit resume): its part goes through `DebuffBonusTracker.Share`, which falls back to the power's applier, as Vulnerable does today. No new code; a reviewer checks the same `Share` is used.
4. **A mod's armour on an enemy with no game content acting** keeps counting as block knocked off. Task 1 tests it.
5. **Expose on an enemy with no block** records nothing, and neither does `LoseBlock` from an enemy's own power (remover null or an enemy). Task 3's Core test covers the amount; a reviewer checks the remover check.

## Files

| File | Change |
|---|---|
| `src/WhoCarried/Core/AbsorbLedger.cs` | `AbsorbLedger.CountsOn(bool enemy, bool gameContentActed)` |
| `src/WhoCarried/Game/AbsorbLayers.cs` | Drop game-owned reductions on enemies |
| `src/WhoCarried/Core/AmplifierParts.cs` | New: the additive parts of a Vulnerable or Weak multiplier |
| `src/WhoCarried/Game/VanillaAmplifiers.cs` | New: rebuild those parts from the game's methods |
| `src/WhoCarried/Game/DebuffBonusTracker.cs` | `Amplifier.Parts`; fill it for Vulnerable and Weak |
| `src/WhoCarried/Game/Tracker.cs` | Credit parts in `CreditDebuffBonus` and Weak prevention; `OnBlockStripped` |
| `src/WhoCarried/Core/BlockStrip.cs` | New: how much block a strip removed |
| `src/WhoCarried/Game/Patches.cs` | `LoseBlockPatch` |
| `src/WhoCarried/ModEntry.cs` | Add `LoseBlockPatch` to `PatchClasses` |
| `tests/WhoCarried.Tests/AbsorbLedgerTests.cs`, `AmplifierPartsTests.cs` (new), `BlockStripTests.cs` (new) | Tests |
| `CHANGELOG.md`, `docs/design/specs/2026-09-20-absorb-layers-design.md` | Docs |

---

### Task 1: Game-owned caps on enemies don't count as armour

**Files:**
- Modify: `src/WhoCarried/Core/AbsorbLedger.cs`, `src/WhoCarried/Game/AbsorbLayers.cs`
- Test: `tests/WhoCarried.Tests/AbsorbLedgerTests.cs`

**Interfaces:**
- Produces: `public static bool AbsorbLedger.CountsOn(bool enemy, bool gameContentActed)`

- [ ] **Step 1: Write the failing test** (append to `AbsorbLedgerTests`)

```csharp
    [Test]
    public static void TheGamesOwnCapsOnAnEnemyArentArmour()
    {
        // Slippery (1 HP per hit) and Hardened Shell (HP per turn) take HP loss away in the same hook as a mod's armour.
        Check.True(!AbsorbLedger.CountsOn(enemy: true, gameContentActed: true), "a game cap on an enemy");
        Check.True(AbsorbLedger.CountsOn(enemy: true, gameContentActed: false), "a mod's armour on an enemy");
        Check.True(AbsorbLedger.CountsOn(enemy: false, gameContentActed: true), "Buffer or Tungsten Rod on a player");
        Check.True(AbsorbLedger.CountsOn(enemy: false, gameContentActed: false), "a mod's armour on a player");
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- AbsorbLedger`
Expected: build error, `AbsorbLedger` has no `CountsOn`.

- [ ] **Step 3: Implement**

In `AbsorbLedger.cs`, add:

```csharp
    /// <summary>
    /// Whether what a layer took off counts as armour. The game's own content only caps HP loss on enemies (Slippery,
    /// Hardened Shell): that isn't block anyone knocked off. On players every layer counts, as the absorb spec decided.
    /// </summary>
    public static bool CountsOn(bool enemy, bool gameContentActed) => !(enemy && gameContentActed);
```

In `AbsorbLayers.Measured`, after `if (eaten <= 0m) return;`:

```csharp
        List<AbstractModel> acted = Safe(modifiers);
        if (!AbsorbLedger.CountsOn(target.IsEnemy, acted.Any(IsGameContent)))
        {
            Tracker.Note($"caps on {target.Monster?.Id.Entry ?? "?"} ate {(int)eaten} hp ({string.Join(", ", acted.Select(m => m.Id.Entry))}), not counted");
            return;
        }
        Ledger.Add(target, eaten, Layer(acted));
```

and replace `Layer` and add helpers:

```csharp
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
```

`Tracker.Note` writes to `events.log`. Its line has no `[F.. A..]` prefix, and no replay pattern matches it.

- [ ] **Step 4: Run the tests** — all pass.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/AbsorbLedger.cs src/WhoCarried/Game/AbsorbLayers.cs tests/WhoCarried.Tests/AbsorbLedgerTests.cs
git commit -m "fix: the game's own hit caps on enemies aren't block knocked off"
```

---

### Task 2: Vulnerable's and Weak's amplifiers go to their owners

**Files:**
- Create: `src/WhoCarried/Core/AmplifierParts.cs`, `src/WhoCarried/Game/VanillaAmplifiers.cs`, `tests/WhoCarried.Tests/AmplifierPartsTests.cs`
- Modify: `src/WhoCarried/Game/DebuffBonusTracker.cs`, `src/WhoCarried/Game/Tracker.cs`

**Interfaces:**
- Produces:
  - `public static (decimal Debuff, decimal Hitter, decimal Debilitate)? AmplifierParts.Vulnerable(decimal baseMultiplier, decimal afterHitter, decimal afterDebilitate, decimal gameMultiplier)`
  - `public static (decimal Debuff, decimal Victim, decimal Debilitate)? AmplifierParts.Weak(decimal baseMultiplier, decimal afterVictim, decimal afterDebilitate, decimal gameMultiplier)`
  - `DebuffBonusTracker.Amplifier(PowerModel Power, decimal Multiplier, IReadOnlyList<(PowerModel? Power, decimal Weight)>? Parts = null)`
  - `internal static IReadOnlyList<(PowerModel? Power, decimal Weight)>? VanillaAmplifiers.Parts(PowerModel power, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, decimal gameMultiplier)`

- [ ] **Step 1: Write the failing tests** (`tests/WhoCarried.Tests/AmplifierPartsTests.cs`)

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class AmplifierPartsTests
{
    [Test]
    public static void DebilitateDoublesVulnerableAndTakesWhatItAdded()
    {
        // 1.5 -> (no Phrog) 1.5 -> Debilitate 2.0: half the extra is Vulnerable's, half Debilitate's.
        Check.Equal(((decimal, decimal, decimal)?)(0.5m, 0m, 0.5m), AmplifierParts.Vulnerable(1.5m, 1.5m, 2.0m, 2.0m), "parts");
    }

    [Test]
    public static void PaperPhrogIsTheHittersOwnPart()
    {
        Check.Equal(((decimal, decimal, decimal)?)(0.5m, 0.25m, 0m), AmplifierParts.Vulnerable(1.5m, 1.75m, 1.75m, 1.75m), "Phrog only");
        Check.Equal(((decimal, decimal, decimal)?)(0.5m, 0.25m, 0.75m), AmplifierParts.Vulnerable(1.5m, 1.75m, 2.5m, 2.5m), "Phrog then Debilitate");
    }

    [Test]
    public static void PartsThatDontMatchTheGameAreNotUsed()
    {
        // A game version or mod changed Vulnerable: fall back to crediting it all to Vulnerable.
        Check.True(AmplifierParts.Vulnerable(1.5m, 1.5m, 2.0m, 2.2m) == null, "mismatch");
        Check.True(AmplifierParts.Weak(0.75m, 0.75m, 0.5m, 0.4m) == null, "mismatch");
    }

    [Test]
    public static void DebilitateAndPaperKraneShareWeaksReduction()
    {
        Check.Equal(((decimal, decimal, decimal)?)(0.25m, 0m, 0.25m), AmplifierParts.Weak(0.75m, 0.75m, 0.5m, 0.5m), "Debilitate");
        Check.Equal(((decimal, decimal, decimal)?)(0.25m, 0.15m, 0m), AmplifierParts.Weak(0.75m, 0.6m, 0.6m, 0.6m), "Krane");
        // Krane 0.6, then Debilitate: 0.6 - 0.4 = 0.2.
        Check.Equal(((decimal, decimal, decimal)?)(0.25m, 0.15m, 0.4m), AmplifierParts.Weak(0.75m, 0.6m, 0.2m, 0.2m), "both");
    }

    [Test]
    public static void AHitsBonusSplitsByPartsInWholePoints()
    {
        // 10 extra HP from 1.5 -> 2.5 with Phrog: 0.5 / 0.25 / 0.75 of it.
        int[] shares = DebuffBonus.SplitIndexed(10, new[] { 0.5m, 0.25m, 0.75m });
        Check.Equal(10, shares.Sum(), "adds up");
        Check.Equal(3, shares[0], "Vulnerable (3.33)");
        Check.Equal(2, shares[1], "hitter (1.67 rounds up)");
        Check.Equal(5, shares[2], "Debilitate");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- AmplifierParts`
Expected: build error, `AmplifierParts` doesn't exist. (The last test compiles against `SplitIndexed`; it fails with the file.)

- [ ] **Step 3: Implement `Core/AmplifierParts.cs`**

```csharp
namespace WhoCarried.Core;

/// <summary>
/// The base game strengthens Vulnerable and Weak from inside their own damage maths: Paper Phrog and Cruelty (the
/// hitter's), Paper Krane (the victim's) and Debilitate (a debuff with its own appliers). These split a debuff's
/// multiplier into additive parts, so each part's HP goes to whoever it belongs to. Null when the parts don't rebuild
/// the multiplier the game itself returned: the caller then credits it all to the debuff, as before.
/// </summary>
public static class AmplifierParts
{
    private const decimal Tolerance = 0.0001m;

    /// <param name="baseMultiplier">Vulnerable's own (1.5).</param>
    /// <param name="afterHitter">After the hitter's amplifiers (Paper Phrog, Cruelty).</param>
    /// <param name="afterDebilitate">After Debilitate on the target.</param>
    /// <param name="gameMultiplier">What the game's own Vulnerable returned for this hit.</param>
    public static (decimal Debuff, decimal Hitter, decimal Debilitate)? Vulnerable(decimal baseMultiplier, decimal afterHitter,
        decimal afterDebilitate, decimal gameMultiplier)
    {
        if (Math.Abs(afterDebilitate - gameMultiplier) > Tolerance || baseMultiplier <= 1m) return null;
        return (baseMultiplier - 1m, Math.Max(0m, afterHitter - baseMultiplier), Math.Max(0m, afterDebilitate - afterHitter));
    }

    /// <param name="baseMultiplier">Weak's own (0.75).</param>
    /// <param name="afterVictim">After the victim's amplifiers (Paper Krane).</param>
    /// <param name="afterDebilitate">After Debilitate on the attacker.</param>
    /// <param name="gameMultiplier">What the game's own Weak returned for this hit.</param>
    public static (decimal Debuff, decimal Victim, decimal Debilitate)? Weak(decimal baseMultiplier, decimal afterVictim,
        decimal afterDebilitate, decimal gameMultiplier)
    {
        if (Math.Abs(afterDebilitate - gameMultiplier) > Tolerance || baseMultiplier >= 1m) return null;
        return (1m - baseMultiplier, Math.Max(0m, baseMultiplier - afterVictim), Math.Max(0m, afterVictim - afterDebilitate));
    }
}
```

- [ ] **Step 4: Run the tests** — the five new tests pass.

- [ ] **Step 5: `Game/VanillaAmplifiers.cs`** (game glue; mirrors `VulnerablePower`/`WeakPower.ModifyDamageMultiplicative`)

```csharp
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>
/// Vulnerable's and Weak's multipliers taken apart into who they belong to, by calling the game's own amplifier methods
/// in the game's order. A part with no power is the hitter's or victim's own and isn't credited to anyone.
/// </summary>
internal static class VanillaAmplifiers
{
    public static IReadOnlyList<(PowerModel? Power, decimal Weight)>? Parts(PowerModel power, Creature target, decimal amount,
        ValueProp props, Creature? dealer, CardModel? cardSource, decimal gameMultiplier)
    {
        try
        {
            return power switch
            {
                VulnerablePower v => Vulnerable(v, target, props, dealer, cardSource, gameMultiplier),
                WeakPower w => Weak(w, target, props, dealer, cardSource, gameMultiplier),
                _ => null,
            };
        }
        catch (Exception) { return null; }
    }

    private static IReadOnlyList<(PowerModel?, decimal)>? Vulnerable(VulnerablePower power, Creature target, ValueProp props,
        Creature? dealer, CardModel? cardSource, decimal game)
    {
        decimal start = power.DynamicVars["DamageIncrease"].BaseValue, now = start;
        if (dealer != null)
        {
            if (dealer.Player?.GetRelic<PaperPhrog>() is { } phrog) now = phrog.ModifyVulnerableMultiplier(target, now, props, dealer, cardSource);
            if ((dealer.GetPower<CrueltyPower>() ?? dealer.PetOwner?.Creature.GetPower<CrueltyPower>()) is { } cruelty)
                now = cruelty.ModifyVulnerableMultiplier(target, now, props, dealer, cardSource);
        }
        decimal afterHitter = now;
        DebilitatePower? debilitate = target.GetPower<DebilitatePower>();
        if (debilitate != null) now = debilitate.ModifyVulnerableMultiplier(target, now, props, dealer, cardSource);
        if (AmplifierParts.Vulnerable(start, afterHitter, now, game) is not { } parts) return null;
        return new (PowerModel?, decimal)[] { (power, parts.Debuff), (null, parts.Hitter), (debilitate, parts.Debilitate) };
    }

    private static IReadOnlyList<(PowerModel?, decimal)>? Weak(WeakPower power, Creature target, ValueProp props,
        Creature? dealer, CardModel? cardSource, decimal game)
    {
        if (dealer == null) return null;
        decimal start = power.DynamicVars["DamageDecrease"].BaseValue, now = start;
        if (target.Player?.GetRelic<PaperKrane>() is { } krane) now = krane.ModifyWeakMultiplier(target, now, props, dealer, cardSource);
        decimal afterVictim = now;
        DebilitatePower? debilitate = dealer.GetPower<DebilitatePower>();
        if (debilitate != null) now = debilitate.ModifyWeakMultiplier(dealer, now, props, dealer, cardSource);
        if (AmplifierParts.Weak(start, afterVictim, now, game) is not { } parts) return null;
        return new (PowerModel?, decimal)[] { (power, parts.Debuff), (null, parts.Victim), (debilitate, parts.Debilitate) };
    }
}
```

On v0.107.1, Cruelty doesn't look at a pet's owner. The rebuilt multiplier then won't match the game's own for a pet's hit, and the hit falls back as intended.

- [ ] **Step 6: `DebuffBonusTracker`: carry the parts**

Change the record:

```csharp
    /// <param name="Parts">Who the multiplier's parts belong to (Debilitate, the hitter's relic…); null when it's all the debuff's.</param>
    public sealed record Amplifier(PowerModel Power, decimal Multiplier, IReadOnlyList<(PowerModel? Power, decimal Weight)>? Parts = null);
```

In `BeforeDamage`, where an amplifier is added:

```csharp
            if (multiplier > 1m)
                (found ??= new List<Amplifier>()).Add(new Amplifier(power, multiplier,
                    VanillaAmplifiers.Parts(power, target, amount, props, dealer, cardSource, multiplier)));
```

In `DamageMultipliers`, the same for every kept amplifier (it serves Weak on the attacking enemy through `Reducers`):

```csharp
            if (keep(multiplier)) found.Add(new Amplifier(power, multiplier,
                VanillaAmplifiers.Parts(power, target, amount, props, dealer, cardSource, multiplier)));
```

- [ ] **Step 7: `Tracker`: credit each part**

Add a helper to `Tracker`:

```csharp
    /// <summary>
    /// Shares <paramref name="hp"/> a debuff did on one hit between the parts of its multiplier: each part with a power
    /// goes to that power's owners (Vulnerable's, Debilitate's), a part with none is the hitter's or victim's own and
    /// goes to nobody. With no parts, all of it is the debuff's.
    /// </summary>
    private static IEnumerable<(PowerModel Power, ulong Player, int Share)> PartShares(DebuffBonusTracker.Amplifier amp, int hp)
    {
        if (amp.Parts == null)
        {
            foreach ((ulong player, int share) in DebuffBonusTracker.Share(amp.Power, hp)) yield return (amp.Power, player, share);
            yield break;
        }
        int[] byPart = DebuffBonus.SplitIndexed(hp, amp.Parts.Select(p => p.Weight).ToList());
        for (int i = 0; i < amp.Parts.Count; i++)
        {
            if (amp.Parts[i].Power is not PowerModel power || byPart[i] <= 0) continue;
            foreach ((ulong player, int share) in DebuffBonusTracker.Share(power, byPart[i])) yield return (power, player, share);
        }
    }
```

In `CreditDebuffBonus`, replace the inner `foreach` over `DebuffBonusTracker.Share(amp.Power, bonus)` with:

```csharp
            foreach ((PowerModel power, ulong player, int share) in PartShares(amp, bonus))
            {
                if (share <= 0 || player == hitter) continue;
                SourceRef debuff = DebuffRef(power);
                _stats.RecordDebuffBonus(player, debuff, share);
                _log?.Write($"{Where} {NameOf(player)} +{share} bonus via {debuff.Id} ({debuff.Label}) " +
                            $"on {NameOf(hitter)}'s hit (x{amp.Multiplier}, {facts.HpRemoved} hp)");
            }
```

(and remove the now-unused `SourceRef debuff = DebuffRef(amp.Power);` above it).

In `OnBeforeDamage`, in the Weak prevention loop, replace `foreach ((ulong player, int share) in DebuffBonusTracker.Share(weak.Power, prevented))` and its body with:

```csharp
            foreach ((PowerModel power, ulong player, int share) in PartShares(weak, prevented))
            {
                if (share <= 0) continue;
                SourceRef debuff = DebuffRef(power);
                _stats.RecordDebuffPrevented(player, debuff, share);
                _log?.Write($"{Where} {NameOf(player)} prevented {share} via {debuff.Id} ({debuff.Label}) | " +
                            $"{Describe(dealer)} hit {NameOf(victim)} for {amount} (x{weak.Multiplier}, block {block})");
            }
```

(and remove the loop's `SourceRef debuff = DebuffRef(weak.Power);`).

- [ ] **Step 8: Build both versions, run the tests**

```bash
"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release -p:GameData="E:/Claude/sts2-refs/v0.107.1"
"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release
"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests
```

Expected: both builds clean, all tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/WhoCarried/Core/AmplifierParts.cs src/WhoCarried/Game tests/WhoCarried.Tests/AmplifierPartsTests.cs
git commit -m "fix: Debilitate, Paper Phrog, Cruelty and Paper Krane's parts of Vulnerable and Weak go to their owners"
```

---

### Task 3: Expose's block strip counts as block knocked off

**Files:**
- Create: `src/WhoCarried/Core/BlockStrip.cs`, `tests/WhoCarried.Tests/BlockStripTests.cs`
- Modify: `src/WhoCarried/Game/Patches.cs`, `src/WhoCarried/Game/Tracker.cs`

**Interfaces:**
- Produces: `public static int BlockStrip.Removed(decimal amount, int block)`; `Tracker.OnBlockStripped(PlayerChoiceContext? context, Creature target, decimal amount, Creature? remover)`

- [ ] **Step 1: Write the failing test** (`tests/WhoCarried.Tests/BlockStripTests.cs`)

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class BlockStripTests
{
    [Test]
    public static void OnlyBlockTheEnemyHadCounts()
    {
        Check.Equal(14, BlockStrip.Removed(14m, 14), "Expose strips all of it");
        Check.Equal(6, BlockStrip.Removed(999_999_999m, 6), "asking for more than there is");
        Check.Equal(3, BlockStrip.Removed(3m, 10), "part of it");
        Check.Equal(0, BlockStrip.Removed(5m, 0), "no block");
        Check.Equal(0, BlockStrip.Removed(0m, 8), "nothing asked");
    }

    [Test]
    public static void AStripReplaysAsBlockRemoved()
    {
        string[] log =
        {
            "player 11 = Moth (The Silent) #5c350f",
            "[F12 A1] fight start: Cultist",
            "[F12 A1] Moth <- Card:EXPOSE (Expose) 0 hp | target CULTIST, blocked 14, dealer player Moth, stack [EXPOSE]",
        };
        PlayerTotals moth = LogReplay.Parse(log).Stats.Get(11)!;
        Check.Equal(14, moth.BlockRemoved, "block removed");
        Check.Equal(0, moth.DamageDealt, "no damage");
    }
}
```

- [ ] **Step 2: Run to verify it fails** — build error, `BlockStrip` doesn't exist.

- [ ] **Step 3: Implement `Core/BlockStrip.cs`**

```csharp
namespace WhoCarried.Core;

/// <summary>Block taken off a creature without a hit (Expose): only what it actually had.</summary>
public static class BlockStrip
{
    public static int Removed(decimal amount, int block) => amount <= 0m || block <= 0 ? 0 : (int)Math.Min(amount, block);
}
```

- [ ] **Step 4: Run the tests** — pass.

- [ ] **Step 5: The patch and the tracker**

`Patches.cs`:

```csharp
/// <summary>Block taken off without a hit (Expose). Read before it goes, so the enemy's block is still there to measure.</summary>
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.LoseBlock))]
internal static class LoseBlockPatch
{
    private static void Prefix(PlayerChoiceContext choiceContext, Creature target, decimal amount, Creature? remover)
    {
        try { Tracker.OnBlockStripped(choiceContext, target, amount, remover); }
        catch (Exception e) { Tracker.LogError("LoseBlock", e); }
    }
}
```

(add `using MegaCrit.Sts2.Core.Commands;`). Add `typeof(LoseBlockPatch),` to `ModEntry.PatchClasses` after `typeof(AfterBlockGainedPatch),`. Patches are applied from that list, not by scanning the assembly.

`Tracker.cs`:

```csharp
    /// <summary>
    /// A player stripping an enemy's block without hitting it (Expose): counted as block knocked off, under whatever
    /// started it. Enemies stripping their own block name no player and aren't counted.
    /// </summary>
    public static void OnBlockStripped(PlayerChoiceContext? context, Creature target, decimal amount, Creature? remover)
    {
        if (!target.IsEnemy || target.IsDead || FactsExtractor.PlayerIdOf(remover) is not ulong player) return;
        int removed = BlockStrip.Removed(amount, target.Block);
        if (removed <= 0) return;
        SourceRef source = FactsExtractor.StackTop(context)?.Source ?? SourceRef.Unknown;
        RecordHit(player, source, 0, removed, target, remover, context);
        Touch();
    }
```

`RecordHit` writes the standard hit line, which the replay reads (Step 1's second test).

- [ ] **Step 6: Build both versions, run the tests** — as in Task 2 Step 8.

- [ ] **Step 7: Commit**

```bash
git add src/WhoCarried/Core/BlockStrip.cs src/WhoCarried/Game tests/WhoCarried.Tests/BlockStripTests.cs src/WhoCarried/ModEntry.cs
git commit -m "fix: block Expose strips counts as block knocked off"
```

---

### Task 4: Docs, install, check in a game

- [ ] **Step 1: Changelog** — under `[Unreleased]` → `### Fixed`:

```markdown
- Vantom, Inklets and Skulking Colony no longer hand out fake "block knocked off": their HP caps (Slippery, Hardened Shell) were counted as enemy block, which could decide Siege breaker.
- Debilitate gets credit for the extra Vulnerable damage and Weak protection it adds, instead of whoever applied the Vulnerable or Weak. Paper Phrog, Cruelty and Paper Krane's extra no longer goes to the debuff's applier either.
- Block that Expose strips from an enemy counts as block knocked off.
```

- [ ] **Step 2: Absorb spec** — in `docs/design/specs/2026-09-20-absorb-layers-design.md`, under "Decision", replace the sentence "A run without such a mod records zero absorbed, so nothing about it changes." with: "The base game's own HP-loss caps on enemies (Slippery, Hardened Shell) are not armour and aren't counted; see [vanilla attribution fixes](2026-09-27-vanilla-attribution-fixes-design.md)."

- [ ] **Step 3: Commit**

```bash
git add CHANGELOG.md docs/design/specs/2026-09-20-absorb-layers-design.md
git commit -m "docs: vanilla attribution fixes"
```

- [ ] **Step 4: Deploy and preview.** Only if the game isn't running (`tasklist | grep -i SlayTheSpire2`); ask the owner before launching. Run `powershell -File tools/deploy.ps1`, then the dev preview (`preview.flag`, wait for `preview-15-podium-focus.png`), close the game, recycle the flag. `events.log` has no `ERROR`, and the patch count went up by one.

- [ ] **Step 5: Real fights** (the owner's next runs), checking `events.log`:
  - Vantom or Inklets: `caps on VANTOM ate … (SLIPPERY_POWER), not counted`, and hit lines' `blocked` is only real block.
  - Debilitate on an enemy a teammate made Vulnerable: `+N bonus via DEBILITATE_POWER` beside smaller `VULNERABLE_POWER` lines.
  - Expose on a blocking enemy: `<- Card:EXPOSE (Expose) 0 hp | …, blocked N`.

- [ ] **Step 6: Finish.** Report to the owner. When they're happy, squash-merge into `main`, subject `Fix vanilla credit for Debilitate, hit caps and Expose`, then run `git branch -D bug/vanilla-attribution`. Don't push.
