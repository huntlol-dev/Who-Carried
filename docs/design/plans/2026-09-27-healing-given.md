# Healing given to teammates — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Credit players for HP they restore to teammates (potions thrown at them, Regen they applied), as a new Support kind with a new award, Medic.

**Architecture:** A new `SupportKind.Healing` flows through the existing support path (`RunStats.RecordSupport`, the `gave N healing to` log line, `SupportRow`, the Support tab, `AwardBuilder.SupportAwards`). A prefix on `CreatureCmd.Heal` measures what a player or pet actually regains. The giver comes from a new potion-use scope (`Game/PotionUse.cs`, an `EffectScopes` watching `PotionModel.OnUseWrapper`), falling back to `SupportGiver.Find`.

**Tech Stack:** C# / .NET 9, Harmony, the game's API, the repo's console test runner.

**Spec:** [Healing given to teammates](../specs/2026-09-27-healing-given-design.md).

## Global constraints

- Branch `feature/healing-given` from `main`. Commit after each task. Don't push. When done, squash-merge into `main` as one commit, subject `Healing given to teammates, and the Medic award`.
- Build and test with `"/c/Program Files/dotnet/dotnet.exe"`; build against both game versions (default and `-p:GameData="E:/Claude/sts2-refs/v0.107.1"`).
- No potion, power or mod is named in code.
- Every new English string gets its `zhs.json` entry in the same commit.
- Only read game state. The potion patch only notes which potion is in use; it never changes the call.
- The support log line is `{giver} gave {n} healing to {recipient} | {source}`, and `LogReplay` must read it.
- If `feature/protection-given` landed first, `SupportKind` already has `Protection` and `SupportRow` a `Protection` parameter: add `Healing` after it, and count awards from what's actually there when updating the README.

## Review focus

1. **Healing a full-HP teammate** counts nothing (missing HP is 0). Task 1 tests `Healing.Restored`.
2. **A dead creature being revived** (Fairy in a Bottle, Lizard Tail) is the player's own, so it's dropped as self-help. A reviewer checks the giver is never forced for revives.
3. **Potion scope leaking** past the potion's use, onto a later heal: `EffectScopes` frames end when the returned task completes. A reviewer checks the finalizer passes `__result` (the task) to `LeaveEffect`.
4. **Enemy heals** (the Waterfall Giant's Siphon) must be ignored: `FactsExtractor.PlayerIdOf` is null for enemies. A reviewer checks the early return.
5. **Seven kinds of help on the exported image and the copied picture** must still fit (Task 3's preview check), and the award grid holds 18 awards (the grid test is extended to 18 if the protection branch hasn't done it).

## Files

| File | Change |
|---|---|
| `src/WhoCarried/Core/Healing.cs` | New: HP actually restored |
| `src/WhoCarried/Core/RunStats.cs` | `SupportKind.Healing`; `PlayerTotals.HpHealedForTeam`; `Given`; `RecordSupport` |
| `src/WhoCarried/Core/LogReplay.cs` | `healing` in the `Gave` pattern |
| `src/WhoCarried/Core/RecapBuilder.cs` | `SupportRow.Healing`; fill it |
| `src/WhoCarried/Core/AwardBuilder.cs` | `Medic`, `MinHealingGiven`, its `SupportAwards` entry |
| `src/WhoCarried/Game/PotionUse.cs` | New: the potion being used, and its patch |
| `src/WhoCarried/Game/Patches.cs`, `ModEntry.cs` | `HealPatch`; register it and `PotionUsePatch` in `PatchClasses` |
| `src/WhoCarried/Game/Tracker.cs` | `OnHealed` |
| `src/WhoCarried/UI/SupportTab.cs`, `GameArt.cs`, `RecapTexts.cs`, `RecapTheme.cs`, `DevPreview.cs` | The Support card, art, award art, its colour, sample data |
| `src/WhoCarried/Localization/eng.json`, `zhs.json` | Three keys |
| `tests/WhoCarried.Tests/HealingTests.cs` (new), `SupportTests.cs`, `AwardTests.cs` | Tests |
| `README.md`, `CHANGELOG.md` | Docs |

---

### Task 1: Core — what a heal restored, the kind, the award

**Files:**
- Create: `src/WhoCarried/Core/Healing.cs`, `tests/WhoCarried.Tests/HealingTests.cs`
- Modify: `src/WhoCarried/Core/RunStats.cs`, `LogReplay.cs`, `RecapBuilder.cs`, `AwardBuilder.cs`, `src/WhoCarried/Localization/eng.json`, `zhs.json`
- Test: `tests/WhoCarried.Tests/SupportTests.cs`, `AwardTests.cs`

**Interfaces:**
- Produces: `public static int Healing.Restored(decimal amount, int currentHp, int maxHp)`; `SupportKind.Healing`; `PlayerTotals.HpHealedForTeam`; `SupportRow.Healing` (default 0); `AwardBuilder.Medic`, `AwardBuilder.MinHealingGiven = 10`

- [ ] **Step 1: Write the failing tests**

`HealingTests.cs`:

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class HealingTests
{
    [Test]
    public static void OnlyMissingHpIsRestored()
    {
        Check.Equal(12, Healing.Restored(12m, 30, 80), "all of it");
        Check.Equal(5, Healing.Restored(20m, 75, 80), "up to max HP");
        Check.Equal(0, Healing.Restored(20m, 80, 80), "already full");
        Check.Equal(0, Healing.Restored(0m, 10, 80), "nothing");
        Check.Equal(3, Healing.Restored(3.9m, 10, 80), "whole HP, like the game");
    }

    [Test]
    public static void HealingIsAKindOfSupportAndReplays()
    {
        var s = new RunStats();
        s.RecordSupport(1, 2, SupportKind.Healing, 12);
        Check.Equal(12, s.Get(1)!.HpHealedForTeam, "given");
        Check.Equal(12, s.Get(1)!.Given(SupportKind.Healing), "Given");
        string[] log =
        {
            "player 1 = Ash (The Regent) #d85a30",
            "player 2 = Moth (The Silent) #7fff00",
            "[F3 A1] " + LogReplay.SupportLine("Ash", "Moth", SupportKind.Healing, 12, "BLOOD_POTION"),
        };
        Check.Equal(12, LogReplay.Parse(log).Stats.Get(1)!.HpHealedForTeam, "replayed, even outside a fight");
    }
}
```

In `AwardTests.EachSupportAwardGoesToItsLeader` add `s.RecordSupport(1, 2, SupportKind.Healing, 15);` and:

```csharp
        Check.Equal("Alice", Find(a, AwardBuilder.Medic)!.PlayerName, "medic");
        Check.Equal("HP restored to teammates", Find(a, AwardBuilder.Medic)!.Detail, "medic detail");
```

In `SupportAwardsNeedARealAmount` add `s.RecordSupport(1, 2, SupportKind.Healing, AwardBuilder.MinHealingGiven - 1);` and `AwardBuilder.Medic` to the title list. If `ManyAwardsFitTheSpreadBothWays` still uses `Enumerable.Range(11, 6)`, change it to `Enumerable.Range(11, 8)`.

- [ ] **Step 2: Run to verify they fail** — build errors for the missing members.

- [ ] **Step 3: Implement**

`Core/Healing.cs`:

```csharp
namespace WhoCarried.Core;

/// <summary>HP a heal actually gave back: no more than was missing, in whole points like the game's run history.</summary>
public static class Healing
{
    public static int Restored(decimal amount, int currentHp, int maxHp) =>
        amount <= 0m ? 0 : (int)Math.Max(0m, Math.Min(amount, maxHp - currentHp));
}
```

`RunStats.cs`: add `Healing` to `SupportKind` (after the last existing member); in `PlayerTotals`:

```csharp
    /// <summary>HP this player restored to teammates (potions thrown at them, Regen they applied).</summary>
    public int HpHealedForTeam { get; set; }
```

`Given`: `SupportKind.Healing => HpHealedForTeam,`; `RecordSupport`: `case SupportKind.Healing: totals.HpHealedForTeam += amount; break;`

`LogReplay.cs`: add `|healing` to the `Gave` pattern's word list.

`RecapBuilder.cs`: add `int Healing = 0` as the last `SupportRow` parameter, add `+ Healing` to `Any`, and pass `t?.HpHealedForTeam ?? 0` last in `Support(...)`.

`AwardBuilder.cs`: `MinHealingGiven = 10`; `Medic = "WHO_CARRIED.award.medic"`; in `SupportAwards` after the Block (Bodyguard) entry, or after Guardian if present:

```csharp
        (Medic, SupportKind.Healing, MinHealingGiven, "WHO_CARRIED.award.medic_detail"),
```

`eng.json`:

```json
  "WHO_CARRIED.award.medic": "Medic",
  "WHO_CARRIED.award.medic_detail": "HP restored to teammates",
  "WHO_CARRIED.support.healing": "Healing",
```

`zhs.json`:

```json
  "WHO_CARRIED.award.medic": "医者",
  "WHO_CARRIED.award.medic_detail": "为队友恢复的生命",
  "WHO_CARRIED.support.healing": "治疗",
```

- [ ] **Step 4: Run all tests** — pass.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core src/WhoCarried/Localization tests/WhoCarried.Tests
git commit -m "feat: healing given as a kind of support, with the Medic award"
```

---

### Task 2: Measure heals in the game

**Files:**
- Create: `src/WhoCarried/Game/PotionUse.cs`
- Modify: `src/WhoCarried/Game/Patches.cs`, `src/WhoCarried/ModEntry.cs`, `src/WhoCarried/Game/Tracker.cs`

**Interfaces:**
- Consumes: `Healing.Restored`, `SupportKind.Healing` (Task 1)
- Produces: `PotionUse.Owner` (`ulong?`), `PotionUse.Id` (`string?`); `Tracker.OnHealed(Creature creature, decimal amount)`

- [ ] **Step 1: `Game/PotionUse.cs`**

```csharp
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>
/// The potion being used right now, in or out of combat, for as long as its effect runs: a potion thrown at a teammate
/// names its owner as the giver of what it does. Any potion, a mod's included.
/// </summary>
internal static class PotionUse
{
    private static readonly EffectScopes Scopes = new();

    private static PotionModel? Current => Scopes.Effect as PotionModel;

    public static ulong? Owner
    {
        get { try { return Current?.Owner?.NetId; } catch (Exception) { return null; } }
    }

    public static string? Id => Current?.Id.Entry;

    public static EffectScopes.Frame Enter(PotionModel potion) => Scopes.EnterEffect(potion);

    public static void Leave(EffectScopes.Frame? frame, Task? returned) => Scopes.LeaveEffect(frame, returned);
}

[HarmonyPatch(typeof(PotionModel), nameof(PotionModel.OnUseWrapper))]
internal static class PotionUsePatch
{
    private static void Prefix(PotionModel __instance, out EffectScopes.Frame? __state) => __state = PotionUse.Enter(__instance);

    private static void Finalizer(EffectScopes.Frame? __state, Task? __result) => PotionUse.Leave(__state, __result);
}
```

- [ ] **Step 2: The heal patch** (`Patches.cs`, add `using MegaCrit.Sts2.Core.Commands;` if missing)

```csharp
/// <summary>Every heal, before it lands: what a player or pet actually regains, and who gave it.</summary>
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Heal))]
internal static class HealPatch
{
    private static void Prefix(Creature creature, decimal amount)
    {
        try { Tracker.OnHealed(creature, amount); }
        catch (Exception e) { Tracker.LogError("Heal", e); }
    }
}
```

Add `typeof(HealPatch),` and `typeof(PotionUsePatch),` to `ModEntry.PatchClasses`, after `typeof(GainEnergyPatch),`.

- [ ] **Step 3: `Tracker.OnHealed`**

```csharp
    /// <summary>
    /// A player or pet about to be healed: what they'll actually regain goes to whoever gave it. A potion in use names
    /// its owner; otherwise the usual support rules (a turn effect's applier, the card being played).
    /// </summary>
    public static void OnHealed(Creature creature, decimal amount)
    {
        if (FactsExtractor.PlayerIdOf(creature) is not ulong recipient) return;
        int restored = Healing.Restored(amount, creature.CurrentHp, creature.MaxHp);
        Support(SupportGiver.Find(_run, PotionUse.Owner), recipient, SupportKind.Healing, restored,
            PotionUse.Id ?? SupportGiver.Running());
    }
```

- [ ] **Step 4: Build both versions, run the tests** — clean, all pass.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Game src/WhoCarried/ModEntry.cs
git commit -m "feat: count HP restored to teammates, potions thrown at them included"
```

---

### Task 3: Show it

**Files:**
- Modify: `src/WhoCarried/UI/SupportTab.cs`, `GameArt.cs`, `RecapTexts.cs`, `DevPreview.cs`

- [ ] **Step 1: Art and the card**

`GameArt.cs`: add `HealIntent = "heal_intent", Regen = "regen"` to the names and

```csharp
        [HealIntent] = "res://images/packed/intents/intent_heal.png",
        [Regen] = Powers + "regen_power.png",
```

`RecapTexts.AwardArt`: `AwardBuilder.Medic => GameArt.Get(GameArt.Regen),`

`SupportTab.Kinds`, after the Block kind (or after Damage prevented if present):

```csharp
        new("WHO_CARRIED.support.healing", RecapTheme.Heal, r => r.Healing, (_, _) => GameArt.Get(GameArt.HealIntent),
            AwardBuilder.Medic),
```

Add the colour to `RecapTheme.cs`, after `Blocked` (line 30), in the file's own style:

```csharp
    public static readonly Color Heal = new("7bd389");
```

`DevPreview.cs`, with the other sample support:

```csharp
                    stats.RecordSupport(from, to, SupportKind.Healing, help.Next(0, i == 2 ? 12 : 4) * act);
```

- [ ] **Step 2: Build both versions, run the tests** — clean, all pass.

- [ ] **Step 3: Commit**

```bash
git add src/WhoCarried/UI
git commit -m "feat: show healing given in Support, with the Medic award's picture"
```

---

### Task 4: Docs, preview, a real run

- [ ] **Step 1: Changelog** — `[Unreleased]` → `### Added`:

```markdown
- **Healing** in Support: HP you restored to teammates, like a Blood Potion thrown at them or Regen you gave them, in or out of a fight, with a new award, **Medic**.
```

- [ ] **Step 2: README** — add "healing" to the Support bullet's list, Medic to the awards list, and update the title count to what's actually there.

- [ ] **Step 3: Commit** `git commit -am "docs: healing given to teammates"`

- [ ] **Step 4: Preview.** Deploy and run the dev preview only if the game isn't running; ask before launching. Check the Support screenshot shows the Healing card and its icon, the awards screenshot shows Medic, and the export and copy screenshots fit the support row.

- [ ] **Step 5: A real co-op run** (the owner's next run): a Blood Potion thrown at a damaged teammate in a fight and on the map, and a Regen Potion on a teammate. `events.log` shows `gave N healing to …` with N equal to the HP they actually regained.

- [ ] **Step 6: Finish.** Report to the owner. When they're happy, squash-merge into `main`, subject `Healing given to teammates, and the Medic award`, then run `git branch -D feature/healing-given`. Don't push.
