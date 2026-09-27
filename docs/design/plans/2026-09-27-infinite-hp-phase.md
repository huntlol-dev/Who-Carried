# Infinite-HP phases don't count — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop counting hits on an enemy whose health bar shows infinite (Waterfall Giant's wind-up, modded undying phases), so players can't pad their damage by attacking into them.

**Architecture:** The game marks these phases itself with `Creature.HpDisplay.IsInfinite()`. `FactsExtractor` reads it into a new `DamageFacts.TargetHpInfinite`, and a Core rule, `Attribution.Counts`, says such a hit counts for nothing. `Tracker.OnDamage` then logs the hit as "not counted" in a form `LogReplay` ignores, and skips damage, block removed, debuff bonus and pile credit. The same flag stops Weak's "damage lost" on the way in, and stops Doom and direct kills of such a creature from crediting its HP.

**Tech Stack:** C# / .NET 9, Harmony, the game's API (`MegaCrit.Sts2.Core.Entities.Creatures`), the repo's console test runner.

**Spec:** [Infinite-HP phases don't count](../specs/2026-09-27-infinite-hp-phase-design.md).

## Global constraints

- Work on the branch `bug/infinite-hp-phase` (already made from `main`). Commit after each task. Don't push. When it's done, it's squash-merged into `main` as one commit with a changelog-style subject.
- Build and test with `C:\Program Files\dotnet\dotnet.exe`: the x86 dotnet first on PATH can't see the x64 .NET 9 SDK. In bash: `"/c/Program Files/dotnet/dotnet.exe"`.
- The signal is `Creature.HpDisplay.IsInfinite()` and nothing else. No enemy, power or mod is named in code. It exists on v0.107.1 and v0.111.0, so no `GameCompat` shim is needed.
- The recap never runs game commands: every change only reads.
- Only the attacking side changes. An infinite-HP enemy's own attacks on players, and debuffs applied to it, are counted as today.
- The killing blow that starts a phase counts: the flag is read at `Hook.AfterDamageGiven`, before the game's death handling sets it.
- Log lines are the replay's input. A skipped hit is written as `{where} {name} not counted {hp} hp from {Kind}:{Id} ({Label}) | target {target}, blocked {n}, infinite HP`. It must not match `LogReplay`'s `Hit` or `Kill` patterns.
- Don't name or compare other mods in code, docs or commit messages.

## Review focus

1. **The killing blow.** It has to count in full. It does only because death handling runs after `AfterDamageGiven`. A reviewer checks that nothing reads the flag later than `FactsExtractor.Extract` inside `OnDamage`. The in-game check in Task 4 confirms it: the kill line is an ordinary hit.
2. **State for the skipped hit is still cleared.** `DebuffBonusTracker.Take(target)` and `AbsorbLayers.Take(target)` must still run for a skipped hit. Otherwise its pending Vulnerable boost or eaten armour would leak onto the next hit. Task 2 keeps both calls above the early return; a reviewer checks the order.
3. **Poison ticking on an infinite enemy.** The tick is skipped before the pile split runs. `SharedPile.Credit` shrinks to the pile's real size on its next call (`ShrinkTo(pileNow)`), so skipping a split leaves no drift. A reviewer checks that `PoisonShares` isn't called for a skipped hit.
4. **A hit on a player or pet with some future "infinite" display.** Only enemies are affected: `Attribution.Counts` is true whenever the target isn't an enemy. Task 1 tests it.
5. **Old logs.** A log from before this change has no "not counted" lines and replays exactly as before. Task 1's replay test runs an ordinary hit beside a skipped one.

## Files

| File | Change |
|---|---|
| `src/WhoCarried/Core/Model.cs` | `DamageFacts.TargetHpInfinite` (optional, default `false`) |
| `src/WhoCarried/Core/Attribution.cs` | `Attribution.Counts(DamageFacts)` |
| `src/WhoCarried/Core/LogReplay.cs` | `LogReplay.NotCountedLine(...)` |
| `src/WhoCarried/Core/EffectCredit.cs` | `Kill.HpInfinite` (optional, default `false`); `ForKill` returns null for it |
| `src/WhoCarried/Game/FactsExtractor.cs` | `HpInfinite(Creature)`; fill `TargetHpInfinite` |
| `src/WhoCarried/Game/Tracker.cs` | Skip the hit in `OnDamage`; skip Weak's cost in `OnBeforeDamage`; skip Doom and direct kills |
| `tests/WhoCarried.Tests/AttributionTests.cs` | Counts rule |
| `tests/WhoCarried.Tests/EffectCreditTests.cs` | Kill of an infinite creature |
| `tests/WhoCarried.Tests/ReplayTests.cs` | Not-counted line stays out of a replay |
| `CHANGELOG.md`, `README.md` | Document it |

---

### Task 1: The rules in Core

**Files:**
- Modify: `src/WhoCarried/Core/Model.cs` (the `DamageFacts` record, line 20)
- Modify: `src/WhoCarried/Core/Attribution.cs`
- Modify: `src/WhoCarried/Core/LogReplay.cs` (next to `PetTookLine`)
- Modify: `src/WhoCarried/Core/EffectCredit.cs` (the `Kill` record and `ForKill`)
- Test: `tests/WhoCarried.Tests/AttributionTests.cs`, `tests/WhoCarried.Tests/EffectCreditTests.cs`, `tests/WhoCarried.Tests/ReplayTests.cs`

**Interfaces:**
- Produces:
  - `DamageFacts(..., SourceCandidate? Effect = null, bool TargetHpInfinite = false)`
  - `public static bool Attribution.Counts(DamageFacts facts)`
  - `public static string LogReplay.NotCountedLine(string who, SourceRef source, int hp, int blocked, string target)`
  - `EffectCredit.Kill(bool EffectActing, bool IsEnemy, bool IsAlive, int Hp, bool CountedAsDoom, ulong? EffectOwner, bool HpInfinite = false)`

- [ ] **Step 1: Write the failing tests**

In `AttributionTests.cs`, give the `Facts` helper an `infinite` parameter and add a test:

```csharp
    private static DamageFacts Facts(ulong? dealer = null, SourceCandidate? pet = null, SourceCandidate? card = null,
                                     SourceCandidate? stack = null, SourceCandidate? fallback = null,
                                     SourceCandidate? effect = null, bool infinite = false) =>
        new(HpRemoved: 7, Blocked: 0, TargetIsEnemy: true, TargetPlayerId: null, DealerPlayerId: dealer,
            Pet: pet, Card: card, StackTop: stack, Fallback: fallback, Effect: effect, TargetHpInfinite: infinite);

    [Test]
    public static void AHitOnAnEnemyWithInfiniteHpDoesNotCount()
    {
        // The Waterfall Giant's wind-up after it's "killed": 999,999,999 HP and a purple bar.
        Check.True(!Attribution.Counts(Facts(dealer: 1, card: new(Strike, 1), infinite: true)), "infinite enemy");
        Check.True(Attribution.Counts(Facts(dealer: 1, card: new(Strike, 1))), "a normal enemy counts");
        Check.True(Attribution.Counts(Facts(dealer: 1, infinite: true) with { TargetIsEnemy = false, TargetPlayerId = 2 }),
            "only enemies are affected");
    }
```

In `EffectCreditTests.cs`, add:

```csharp
    [Test]
    public static void AKillOfACreatureWithInfiniteHpIsNotCounted()
    {
        // A judgement killing an enemy mid-undying-phase would otherwise credit 999,999,999 HP.
        Check.Equal("not counted", Show(EffectCredit.ForKill(Enemy(hp: 999_999_999, owner: You) with { HpInfinite = true },
            StackedBy((You, 6)))), "infinite HP");
        Check.Equal("1:25", Show(EffectCredit.ForKill(Enemy(owner: You), null)), "a normal kill still counts");
    }
```

In `ReplayTests.cs`, add:

```csharp
    [Test]
    public static void AHitThatWasNotCountedStaysOutOfAReplay()
    {
        var strike = new SourceRef(SourceKind.Card, "STRIKE_IRONCLAD", "Strike");
        string line = LogReplay.NotCountedLine("Moth", strike, 24, 3, "WATERFALL_GIANT");
        Check.Equal("Moth not counted 24 hp from Card:STRIKE_IRONCLAD (Strike) | target WATERFALL_GIANT, blocked 3, infinite HP",
            line, "line");
        string[] log =
        {
            "player 11 = Moth (The Tailor) #5c350f",
            "[F33 A2] fight start: Waterfall Giant [boss]",
            "[F33 A2] Moth <- Card:STRIKE_IRONCLAD (Strike) 9 hp | target WATERFALL_GIANT, blocked 0, dealer player Moth, stack [STRIKE_IRONCLAD]",
            "[F33 A2] " + line,
            "[F33 A2] fight end, saved",
        };
        PlayerTotals moth = LogReplay.Parse(log).Stats.Get(11)!;
        Check.Equal(9, moth.DamageDealt, "only the counted hit");
        Check.Equal(0, moth.BlockRemoved, "its block isn't counted either");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: the build fails. `DamageFacts` has no `TargetHpInfinite`, `Attribution.Counts`, `LogReplay.NotCountedLine` and `EffectCredit.Kill.HpInfinite` don't exist.

- [ ] **Step 3: Write the minimal implementation**

`Model.cs`: add the last parameter to `DamageFacts`:

```csharp
    SourceCandidate? Effect = null,
    bool TargetHpInfinite = false);
```

and to the record's doc comment, if it has one for each parameter:
`/// <param name="TargetHpInfinite">The target's health bar shows infinite: an enemy in a phase where it can't die.</param>`

`Attribution.cs`: add below `Resolve`:

```csharp
    /// <summary>
    /// Whether a hit counts at all. A hit on an enemy whose health bar shows infinite (a phase where it can't die, like
    /// the Waterfall Giant winding up its last attack) changes nothing, so nothing done to it is counted.
    /// </summary>
    public static bool Counts(DamageFacts facts) => !(facts.TargetIsEnemy && facts.TargetHpInfinite);
```

`LogReplay.cs`: add next to `PetTookLine`:

```csharp
    /// <summary>
    /// A hit on an enemy with infinite HP, kept for the record. It has no " &lt;- ", so the replay doesn't read it as
    /// damage.
    /// </summary>
    public static string NotCountedLine(string who, SourceRef source, int hp, int blocked, string target) =>
        $"{who} not counted {hp} hp from {source.Kind}:{source.Id} ({source.Label}) | target {target}, blocked {blocked}, infinite HP";
```

`EffectCredit.cs`: add the field and the check, and mention it in the `ForKill` summary ("…a creature already dead or at 0, one showing infinite HP, or Doom's own kill."):

```csharp
    /// <param name="HpInfinite">The creature's health bar shows infinite: its HP isn't real.</param>
    public sealed record Kill(bool EffectActing, bool IsEnemy, bool IsAlive, int Hp, bool CountedAsDoom, ulong? EffectOwner,
                              bool HpInfinite = false);
```

```csharp
        if (!kill.EffectActing || !kill.IsEnemy || !kill.IsAlive || kill.Hp <= 0 || kill.CountedAsDoom || kill.HpInfinite) return null;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: all tests pass, including the three new ones.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core tests/WhoCarried.Tests
git commit -m "feat: hits and kills on an enemy with infinite HP don't count"
```

---

### Task 2: Read the flag and skip the hit in the game

**Files:**
- Modify: `src/WhoCarried/Game/FactsExtractor.cs`
- Modify: `src/WhoCarried/Game/Tracker.cs` (`OnDamage`, `OnBeforeDamage`, `OnDoomKill`, `OnDirectKill`)

**Interfaces:**
- Consumes: `DamageFacts.TargetHpInfinite`, `Attribution.Counts`, `LogReplay.NotCountedLine`, `EffectCredit.Kill.HpInfinite` (Task 1)
- Produces: `internal static bool FactsExtractor.HpInfinite(Creature creature)`

This is game glue. Core covers its rules, and the game layer has no unit tests; Task 4 checks it in a game.

- [ ] **Step 1: `FactsExtractor`: the one place the flag is read**

Add below `PlayerIdOf`:

```csharp
    /// <summary>
    /// The creature's health bar shows infinite: the game's own mark for a phase where it can't die (the Waterfall Giant
    /// winding up its last attack, a per-turn HP cap used up). Its HP isn't real, so what's done to it doesn't count.
    /// </summary>
    public static bool HpInfinite(Creature creature)
    {
        try { return creature.HpDisplay.IsInfinite(); }
        catch (Exception) { return false; }
    }
```

In `Extract`, after the `Effect:` argument, add:

```csharp
            Effect: dealer == null && effect != null ? Safe(() => Candidate(effect)) : null,
            TargetHpInfinite: HpInfinite(target));
```

(Remove the `)` that closed the list after `Effect:`.)

- [ ] **Step 2: `Tracker.OnDamage`: skip the hit, after clearing its state**

Just after `(int absorbed, IReadOnlyList<string> layers) = AbsorbLayers.Take(target);` and before the `if (absorbed > 0)` log line, insert:

```csharp
        if (!Attribution.Counts(facts))
        {
            // An enemy that can't die right now: its pending boost and armour are cleared above, and nothing else counts.
            AttributionResult hitter = Attribution.Resolve(facts);
            _log?.Write($"{Where} " + LogReplay.NotCountedLine(NameOf(hitter.PlayerId), hitter.Source, facts.HpRemoved,
                facts.Blocked + absorbed, Describe(target)));
            return;
        }
```

This return comes before `PoisonShares`, `RecordHit` and `CreditDebuffBonus`. `NoteHp` only matters for players, whom this branch never reaches.

- [ ] **Step 3: `Tracker.OnBeforeDamage`: no Weak "damage lost" into an infinite enemy**

In the attacking branch, change

```csharp
            if (amount <= 0m) return;
```

to

```csharp
            if (amount <= 0m || FactsExtractor.HpInfinite(target)) return;
```

Keep the three calls above it (`DebuffBonusTracker.BeforeDamage`, `AbsorbLayers.Starting`, `DebuffBonusTracker.TakeBaseDamage`) exactly where they are. They set up and clear per-hit state and must run for every hit.

- [ ] **Step 4: `Tracker.OnDoomKill` and `OnDirectKill`**

In `OnDoomKill`, change

```csharp
            if (creature == null || !creature.IsEnemy) continue;
```

to

```csharp
            if (creature == null || !creature.IsEnemy || FactsExtractor.HpInfinite(creature)) continue;
```

In `OnDirectKill`, pass the flag into the kill:

```csharp
            var kill = new EffectCredit.Kill(effect != null, creature.IsEnemy, creature.IsAlive, creature.CurrentHp,
                countedAsDoom, source?.OwnerId, FactsExtractor.HpInfinite(creature));
```

- [ ] **Step 5: Build against both game versions, and run the tests**

```bash
"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release
"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release -p:GameData="E:/Claude/sts2-refs/v0.107.1"
"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests
```

Expected: both builds succeed with no new warnings; all tests pass. Rebuild once more without `-p:GameData` so the output is the beta build again.

- [ ] **Step 6: Commit**

```bash
git add src/WhoCarried/Game
git commit -m "fix: hits on an enemy showing infinite HP no longer count as damage"
```

---

### Task 3: Docs

**Files:**
- Modify: `CHANGELOG.md` (`## [Unreleased]` → `### Fixed`, first item)
- Modify: `README.md` (the **Fair credit** bullet under "Good to know")

- [ ] **Step 1: Changelog**

Add as the first item under `### Fixed` in `[Unreleased]`:

```markdown
- Hitting an enemy that can't die right now no longer counts as damage. The Waterfall Giant's last turn after it's beaten, and modded enemies with the same kind of undying phase, show an infinite health bar; attacks into them used to pad the attacker's damage and block knocked off. The blow that knocks the Giant down still counts, and so does everything done against its final attack. Runs recorded before this update keep their old totals.
```

- [ ] **Step 2: README**

Append one sentence to the **Fair credit** bullet:

```markdown
Hits on an enemy whose health bar shows infinite (an undying phase) don't count.
```

- [ ] **Step 3: Commit**

```bash
git add CHANGELOG.md README.md
git commit -m "docs: infinite-HP phases don't count"
```

---

### Task 4: Install and check in a game

- [ ] **Step 1: Deploy.** Only if the game isn't running (`tasklist | grep -i "SlayTheSpire2"` returns nothing). Never close the owner's game.

```bash
powershell -File tools/deploy.ps1
```

- [ ] **Step 2: Dev preview.** It uses sample stats, so it doesn't exercise this path, but it shows nothing else broke. Create `preview.flag` in `%APPDATA%\SlayTheSpire2\WhoCarried\`, launch through Steam (ask the owner first), wait for `preview-15-podium-focus.png`, close the game, and recycle the flag. Check that the preview screenshots look like before and that `events.log` has no `ERROR`.

- [ ] **Step 3: A real Waterfall Giant fight.** This is the owner's next run that reaches it. The owner plays it; no scripted check reaches this boss. In `events.log`, check:
  - the killing blow is an ordinary `… <- … hp | target WATERFALL_GIANT …` line;
  - every hit after it until the fight ends is a `… not counted … | target WATERFALL_GIANT, …, infinite HP` line;
  - the Giant's final attack on players still produces `prevented …` lines where Weak or Strength-down were on it;
  - the recap's damage for the fight is in the hundreds, not the millions.

- [ ] **Step 4: Finish.** Report to the owner. When they're happy, squash-merge into `main` as one commit, subject `Hits on an enemy with infinite HP don't count`, body listing the changes. Then run `git branch -D bug/infinite-hp-phase`. Don't push.
