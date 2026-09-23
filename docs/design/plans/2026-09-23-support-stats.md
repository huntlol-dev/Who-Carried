# Support Stats Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Count the energy, cards, block, buffs and draws each player gives teammates, and show them on a new Support tab, on the exported image (only when there's something to show), and as five new awards.

**Architecture:** Core (plain C#, unit-tested) gets the counters, the rule for who gave something, the log line and its replay, the view rows, the awards and the Awards-tab grid maths. Game glue reads the gifts at five game hooks and asks Core who to credit. UI adds an eighth tab and a section on the image, both built from the view rows.

**Tech Stack:** C# / .NET 9, Godot 4 (the game's), Harmony patches, the repo's own console test runner.

**Spec:** `docs/design/specs/2026-09-23-support-stats-design.md`

## Global Constraints

- Branch `feature/support-stats`. Commit after each task; messages in the repo's style (a plain sentence, no `feat:` prefix), ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Use the x64 SDK: `"C:/Program Files/dotnet/dotnet.exe"` (the x86 `dotnet` first on PATH can't see it; see `local.props`).
- Tests: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- <filter>`. The filter matches `Class.Method`, case-insensitively; no filter runs everything.
- Build: `"C:/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release`.
- Support means a player gives something to a **different** player, during a fight. Self-help never counts. Help landing on a teammate's pet counts for that teammate.
- Every new on-screen English string goes into both `src/WhoCarried/Localization/eng.json` and `zhs.json`, in alphabetical key order, in the same task that first uses it. `LocalizationTests` fails if the catalogs' keys differ, if code uses a key `eng.json` lacks, or if a key goes unused. Keys must appear in code as whole string literals (`"WHO_CARRIED.x.y"`); the test finds them by regex.
- Chinese uses the game's terms: 能量 energy, 格挡 block, 抽牌 draw, 增益 buff, 卡牌 card.
- Harmony binds patch parameters by **name**: they must match the game's parameter names exactly. A private field is injected as `___` + its name (`_player` → `____player`).
- Every patch body is wrapped in `try { … } catch (Exception e) { Tracker.LogError("<where>", e); }`, like the existing ones.
- Nothing is uploaded to the Workshop. `WhoCarried.json` stays at `1.1.0`.

## Review Focus

1. **A `current_run.dat` saved by 1.1.0 (no support fields) resumed mid-run** must load with every support counter at 0. Pinned by `SupportTests.StatsSavedBeforeSupportLoadWithNothingGiven` in Task 1.
2. **A player's own gains never count**: their Defend, their relic's energy, their own Inflame, their hand draw. Core pins the rule (Task 1). The game side is checked by hand in Task 11, steps 3–7.
3. **`support: no giver` lines mustn't flood `events.log`** in an ordinary vanilla fight. Buff gifts only log when the applier is a player (Task 7), and Task 11 step 8 counts the lines.
4. **5-player modded lobbies**: five plates must fit the tab. Task 8's plate height shrinks with rows the way the Defense tab's does. Task 11 step 2 previews five characters.
5. **Long Chinese labels mustn't overflow the plates.** Numbers and words are short by design (`{0} 点能量`). Task 11 step 2 includes a Chinese preview.

---

### Task 1: Support counters in the run's stats

**Files:**
- Modify: `src/WhoCarried/Core/RunStats.cs`
- Test: `tests/WhoCarried.Tests/SupportTests.cs` (create)

**Interfaces:**
- Produces: `enum SupportKind { Energy, Cards, Block, Buffs, Draws }` (namespace `WhoCarried.Core`); `PlayerTotals.EnergyGiven`, `CardsGiven`, `BlockGiven`, `BuffsGiven`, `CardsDrawnForTeam` (`int`); `int PlayerTotals.Given(SupportKind kind)`; `void RunStats.RecordSupport(ulong giver, ulong recipient, SupportKind kind, int amount)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/SupportTests.cs`:

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class SupportTests
{
    [Test]
    public static void SupportCountsForTheGiverOnly()
    {
        var s = new RunStats();
        s.RecordSupport(1, 2, SupportKind.Energy, 2);
        s.RecordSupport(1, 2, SupportKind.Energy, 1);
        s.RecordSupport(1, 3, SupportKind.Cards, 3);
        s.RecordSupport(1, 2, SupportKind.Block, 8);
        s.RecordSupport(1, 2, SupportKind.Buffs, 2);
        s.RecordSupport(1, 3, SupportKind.Draws, 4);
        PlayerTotals a = s.Get(1)!;
        Check.Equal(3, a.EnergyGiven, "energy");
        Check.Equal(3, a.CardsGiven, "cards");
        Check.Equal(8, a.BlockGiven, "block");
        Check.Equal(2, a.BuffsGiven, "buffs");
        Check.Equal(4, a.CardsDrawnForTeam, "draws");
        Check.Equal(3, a.Given(SupportKind.Energy), "Given(Energy)");
        Check.Equal(4, a.Given(SupportKind.Draws), "Given(Draws)");
        Check.True(s.Get(2) == null && s.Get(3) == null, "receiving isn't counted");
    }

    [Test]
    public static void HelpingYourselfOrGivingNothingIsntSupport()
    {
        var s = new RunStats();
        s.RecordSupport(1, 1, SupportKind.Energy, 5);
        s.RecordSupport(1, 2, SupportKind.Block, 0);
        s.RecordSupport(1, 2, SupportKind.Block, -3);
        Check.True(s.Get(1) == null, "nothing recorded");
    }

    [Test]
    public static void SupportSurvivesASaveAndLoad()
    {
        string path = Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "current_run.dat");
        var s = new RunStats { RunKey = "SEED:1" };
        s.RecordSupport(1, 2, SupportKind.Block, 12);
        s.RecordSupport(1, 2, SupportKind.Draws, 2);
        RunStatsStore.Save(s, path);
        PlayerTotals loaded = RunStatsStore.LoadIfResumable(path, "SEED:1")!.Get(1)!;
        Check.Equal(12, loaded.BlockGiven, "block");
        Check.Equal(2, loaded.CardsDrawnForTeam, "draws");
    }

    [Test]
    public static void StatsSavedBeforeSupportLoadWithNothingGiven()
    {
        string path = Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "current_run.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // What 1.1.0 wrote: no support fields at all.
        File.WriteAllText(path, "{ \"RunKey\": \"SEED:1\", \"Players\": { \"1\": { \"DamageDealt\": 9 } } }");
        PlayerTotals loaded = RunStatsStore.LoadIfResumable(path, "SEED:1")!.Get(1)!;
        Check.Equal(9, loaded.DamageDealt, "old stats kept");
        Check.Equal(0, loaded.EnergyGiven + loaded.CardsGiven + loaded.BlockGiven + loaded.BuffsGiven + loaded.CardsDrawnForTeam,
            "support starts at nothing");
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- SupportTests`
Expected: build error, `SupportKind` / `RecordSupport` don't exist.

- [ ] **Step 3: Implement**

In `src/WhoCarried/Core/RunStats.cs`, before `public sealed class PlayerTotals`:

```csharp
/// <summary>What one player gave another (see <see cref="RunStats.RecordSupport"/>).</summary>
public enum SupportKind { Energy, Cards, Block, Buffs, Draws }
```

In `PlayerTotals`, after `DebuffCosts`:

```csharp
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
```

In `RunStats`, after `RecordCardCreated`:

```csharp
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
```

`WhoCarriedJson` is source-generated from `RunStats`, so the new properties serialize with no other change.

- [ ] **Step 4: Run the tests to see them pass**

Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- SupportTests`
Expected: 4 PASS. Then run the whole suite with no filter: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/RunStats.cs tests/WhoCarried.Tests/SupportTests.cs
git commit -m "Count what each player gives their teammates" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The rule for who gave it

**Files:**
- Create: `src/WhoCarried/Core/SupportCredit.cs`
- Test: `tests/WhoCarried.Tests/SupportTests.cs`

**Interfaces:**
- Produces: `static ulong? SupportCredit.Giver(ulong? named, ulong? turnEffect, IReadOnlyCollection<ulong>? midEffect, ulong? actionOwner)`. Task 7 calls it.

- [ ] **Step 1: Write the failing test**

Append to `SupportTests`:

```csharp
    [Test]
    public static void TheGiverIsTheMostDirectOneTheGameGives()
    {
        ulong[] alice = { 1 }, both = { 1, 2 }, nobody = Array.Empty<ulong>();
        Check.Equal((ulong?)3, SupportCredit.Giver(3, 4, alice, 5), "a giver the game named wins");
        Check.Equal((ulong?)4, SupportCredit.Giver(null, 4, alice, 5), "then a turn hook's owner");
        Check.Equal((ulong?)1, SupportCredit.Giver(null, null, alice, 5), "then the one player whose card or potion is working");
        Check.Equal((ulong?)null, SupportCredit.Giver(null, null, both, 5), "two players mid-effect: no guess");
        Check.Equal((ulong?)null, SupportCredit.Giver(null, null, nobody, 5), "nobody mid-effect: the action owner isn't asked");
        Check.Equal((ulong?)5, SupportCredit.Giver(null, null, null, 5), "only when the game can't say who's mid-effect");
        Check.Equal((ulong?)null, SupportCredit.Giver(null, null, null, null), "nothing to go on");
    }
```

- [ ] **Step 2: Run it to see it fail**

Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- SupportTests`
Expected: build error, `SupportCredit` doesn't exist.

- [ ] **Step 3: Implement**

Create `src/WhoCarried/Core/SupportCredit.cs`:

```csharp
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
```

- [ ] **Step 4: Run it to see it pass**

Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- SupportTests`
Expected: 5 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/SupportCredit.cs tests/WhoCarried.Tests/SupportTests.cs
git commit -m "Decide who gave help the game doesn't name a giver for" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Support in events.log, and its replay

**Files:**
- Modify: `src/WhoCarried/Core/LogReplay.cs`
- Test: `tests/WhoCarried.Tests/ReplayTests.cs`

**Interfaces:**
- Consumes: `SupportKind`, `RunStats.RecordSupport` (Task 1).
- Produces: `static string LogReplay.SupportLine(string giver, string recipient, SupportKind kind, int amount, string source)` → `"Moth gave 2 energy to Ironside | BELIEVE_IN_YOU"`; `static string LogReplay.SupportWord(SupportKind kind)` → `"energy"`, `"cards"`, `"block"`, `"buffs"`, `"draws"`. Task 7 writes both.

- [ ] **Step 1: Write the failing test**

Append to `ReplayTests`:

```csharp
    [Test]
    public static void ReplayReadsTheSupportLinesItWrites()
    {
        string[] log =
        {
            "Who Carried v1.2.0 - run SEED2:1 started 2026-09-23 20:00",
            "player 11 = Moth (The Tailor) #5c350f",
            "player 22 = Ironside (The Necrobinder) #ee82ee",
            "[F3 A1] fight start: Toadpoles",
            "[F3 A1] " + LogReplay.SupportLine("Moth", "Ironside", SupportKind.Energy, 2, "BELIEVE_IN_YOU"),
            "[F3 A1] " + LogReplay.SupportLine("Moth", "Ironside", SupportKind.Block, 8, "RALLY"),
            "[F3 A1] " + LogReplay.SupportLine("Moth", "Ironside", SupportKind.Draws, 1, "HUDDLE_UP"),
            "[F3 A1] " + LogReplay.SupportLine("Ironside", "Moth", SupportKind.Cards, 3, "GLIMPSE_BEYOND"),
            "[F3 A1] " + LogReplay.SupportLine("Ironside", "Moth", SupportKind.Buffs, 2, "BLAZE"),
            "[F3 A1] " + LogReplay.SupportLine("Stranger", "Moth", SupportKind.Energy, 5, "?"),
            "[F3 A1] support: no giver for 1 energy to Moth | ?",
            "[F3 A1] fight end, saved",
        };
        Check.Equal("Moth gave 2 energy to Ironside | BELIEVE_IN_YOU",
            LogReplay.SupportLine("Moth", "Ironside", SupportKind.Energy, 2, "BELIEVE_IN_YOU"), "line format");

        LogReplay.Result r = LogReplay.Parse(log);
        PlayerTotals moth = r.Stats.Get(11)!, ironside = r.Stats.Get(22)!;
        Check.Equal(2, moth.EnergyGiven, "energy; an unknown giver and a no-giver line add nothing");
        Check.Equal(8, moth.BlockGiven, "block");
        Check.Equal(1, moth.CardsDrawnForTeam, "draws");
        Check.Equal(3, ironside.CardsGiven, "cards");
        Check.Equal(2, ironside.BuffsGiven, "buffs");
        Check.Equal(0, ironside.EnergyGiven, "receiving isn't giving");
    }
```

- [ ] **Step 2: Run it to see it fail**

Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- ReplayReadsTheSupportLines`
Expected: build error, `SupportLine` doesn't exist.

- [ ] **Step 3: Implement**

In `LogReplay.cs`:

1. Add to the regexes, after `Badge`:

```csharp
    private static readonly Regex Gave = new(Where + @"(.+?) gave (\d+) (energy|cards|block|buffs|draws) to (.+?) \| (.*)$", RegexOptions.Compiled);
```

2. Add after `PetTookLine`:

```csharp
    /// <summary>
    /// Help one player gave another (after its "[F.. A..] " prefix), which <see cref="Parse"/> reads back.
    /// <paramref name="source"/> is the id of what gave it, or "?".
    /// </summary>
    public static string SupportLine(string giver, string recipient, SupportKind kind, int amount, string source) =>
        $"{giver} gave {amount} {SupportWord(kind)} to {recipient} | {source}";

    /// <summary>The log's word for a kind of support: "energy", "cards", "block", "buffs", "draws".</summary>
    public static string SupportWord(SupportKind kind) => kind.ToString().ToLowerInvariant();
```

3. In `Parse`'s loop, after the `PetTook` branch:

```csharp
            else if ((m = Gave.Match(line)).Success)
            {
                if (Who(m.Groups[3].Value) is ulong from && Who(m.Groups[6].Value) is ulong to)
                    stats.RecordSupport(from, to, Enum.Parse<SupportKind>(m.Groups[5].Value, ignoreCase: true), Int(m.Groups[4]));
            }
```

4. In the class summary's list of stats an older log never recorded, add "support given to teammates".

- [ ] **Step 4: Run it to see it pass**

Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- ReplayTests`
Expected: all ReplayTests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/LogReplay.cs tests/WhoCarried.Tests/ReplayTests.cs
git commit -m "Log each gift to a teammate, and replay it" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Support rows in the recap's view

**Files:**
- Modify: `src/WhoCarried/Core/RecapBuilder.cs`
- Test: `tests/WhoCarried.Tests/SupportTests.cs`

**Interfaces:**
- Consumes: `PlayerTotals` counters (Task 1).
- Produces: `sealed record SupportRow(string Label, string ColorHex, string? IconKey, string Character, int Energy, int Cards, int Block, int Buffs, int Draws)` with `bool Any`; `RecapView.Support` (`IReadOnlyList<SupportRow>`, scoreboard order, one per player) and `RecapView.HasSupport` (`bool`). Tasks 8 and 9 read these.

- [ ] **Step 1: Write the failing tests**

Append to `SupportTests`:

```csharp
    private static readonly PlayerInfo Alice = new(1, "Alice", "Ironclad", "d85a30", "IRONCLAD");
    private static readonly PlayerInfo Bob = new(2, "Bob", "The Silent", "7fff00", "SILENT");
    private static readonly IReadOnlyDictionary<ulong, DefenseTotals> NoDefense = new Dictionary<ulong, DefenseTotals>();

    [Test]
    public static void SupportRowsFollowTheScoreboard()
    {
        var s = new RunStats();
        s.RecordDamage(2, new SourceRef(SourceKind.Card, "NEUTRALIZE", "Neutralize"), 50);
        s.RecordDamage(1, new SourceRef(SourceKind.Card, "BASH", "Bash"), 10);
        s.RecordSupport(1, 2, SupportKind.Energy, 3);
        s.RecordSupport(1, 2, SupportKind.Block, 20);
        s.RecordSupport(2, 1, SupportKind.Cards, 2);
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, NoDefense, "h");
        Check.Equal(2, v.Support.Count, "one row per player");
        Check.Equal("Bob", v.Support[0].Label, "more damage ranks first, as on the scoreboard");
        Check.Equal(2, v.Support[0].Cards, "Bob's cards");
        Check.Equal("SILENT", v.Support[0].IconKey, "icon");
        SupportRow alice = v.Support[1];
        Check.Equal((3, 0, 20, 0, 0), (alice.Energy, alice.Cards, alice.Block, alice.Buffs, alice.Draws), "Alice's totals");
        Check.True(v.HasSupport, "someone gave something");
    }

    [Test]
    public static void NothingToShowWhenNobodyGaveAnything()
    {
        var solo = new RunStats();
        solo.RecordSupport(1, 1, SupportKind.Energy, 4); // to themselves: not support
        RecapView one = RecapBuilder.Build(solo, new[] { Alice }, NoDefense, "h");
        Check.Equal(1, one.Support.Count, "a lone player still has a row");
        Check.True(!one.HasSupport, "solo: nothing to show");

        RecapView pair = RecapBuilder.Build(new RunStats(), new[] { Alice, Bob }, NoDefense, "h");
        Check.True(!pair.HasSupport, "co-op with no gifts: nothing to show");
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- SupportTests`
Expected: build error, `SupportRow` / `RecapView.Support` don't exist.

- [ ] **Step 3: Implement**

In `RecapBuilder.cs`, after the `DefenseTotals` record:

```csharp
/// <summary>What one player gave their teammates over the run (see <see cref="RunStats.RecordSupport"/>).</summary>
public sealed record SupportRow(string Label, string ColorHex, string? IconKey, string Character,
                                int Energy, int Cards, int Block, int Buffs, int Draws)
{
    /// <summary>Whether this player gave a teammate anything at all.</summary>
    public bool Any => Energy + Cards + Block + Buffs + Draws > 0;
}
```

Add a last positional parameter to `RecapView`, after `RunFacts? Facts = null`:

```csharp
    RunFacts? Facts = null,
    IReadOnlyList<SupportRow>? SupportRows = null)
```

and inside its body, after `BadgesKnown`:

```csharp
    /// <summary>What each player gave teammates, in scoreboard order.</summary>
    public IReadOnlyList<SupportRow> Support => SupportRows ?? Array.Empty<SupportRow>();

    /// <summary>Whether anyone gave a teammate anything. The saved image leaves the Support section out otherwise.</summary>
    public bool HasSupport => Support.Any(r => r.Any);
```

In `Build`, after the `defenseRows` list:

```csharp
        List<SupportRow> support = byDamage.Select(p => Support(p, stats.Get(p.NetId))).ToList();
```

and pass it last in the `return new RecapView(…)` call: change the final `facts);` to `facts, support);`.

Add beside the other private helpers:

```csharp
    private static SupportRow Support(PlayerInfo p, PlayerTotals? t) =>
        new(p.Name, p.ColorHex, IconOf(p), p.Character, t?.EnergyGiven ?? 0, t?.CardsGiven ?? 0, t?.BlockGiven ?? 0,
            t?.BuffsGiven ?? 0, t?.CardsDrawnForTeam ?? 0);
```

- [ ] **Step 4: Run them to see them pass**

Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/RecapBuilder.cs tests/WhoCarried.Tests/SupportTests.cs
git commit -m "Give the recap a support row per player" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Five support awards

**Files:**
- Modify: `src/WhoCarried/Core/AwardBuilder.cs`, `src/WhoCarried/Localization/eng.json`, `src/WhoCarried/Localization/zhs.json`
- Test: `tests/WhoCarried.Tests/AwardTests.cs`

**Interfaces:**
- Consumes: `PlayerTotals.Given(SupportKind)` (Task 1).
- Produces: `AwardBuilder.Battery`, `CarePackage`, `Bodyguard`, `Coach`, `Playmaker` (award keys); `MinEnergyGiven = 2`, `MinCardsGiven = 3`, `MinBlockGiven = 10`, `MinBuffsGiven = 3`, `MinDrawsGiven = 3`. Task 8 maps the keys to art.

- [ ] **Step 1: Write the failing tests**

Append to `AwardTests`:

```csharp
    [Test]
    public static void EachSupportAwardGoesToItsLeader()
    {
        var s = new RunStats();
        s.RecordSupport(1, 2, SupportKind.Energy, 3);
        s.RecordSupport(2, 1, SupportKind.Energy, 1);
        s.RecordSupport(1, 2, SupportKind.Cards, 3);
        s.RecordSupport(2, 1, SupportKind.Block, 12);
        s.RecordSupport(2, 1, SupportKind.Buffs, 3);
        s.RecordSupport(1, 2, SupportKind.Draws, 4);
        IReadOnlyList<Award> a = AwardBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>());
        Check.Equal("Alice", Find(a, AwardBuilder.Battery)!.PlayerName, "battery");
        Check.Equal("3", Find(a, AwardBuilder.Battery)!.Value, "energy given");
        Check.Equal("energy given to teammates", Find(a, AwardBuilder.Battery)!.Detail, "battery detail");
        Check.Equal("Alice", Find(a, AwardBuilder.CarePackage)!.PlayerName, "care package");
        Check.Equal("Bob", Find(a, AwardBuilder.Bodyguard)!.PlayerName, "bodyguard");
        Check.Equal("12", Find(a, AwardBuilder.Bodyguard)!.Value, "block given");
        Check.Equal("Bob", Find(a, AwardBuilder.Coach)!.PlayerName, "coach");
        Check.Equal("Alice", Find(a, AwardBuilder.Playmaker)!.PlayerName, "playmaker");
    }

    [Test]
    public static void SupportAwardsNeedARealAmount()
    {
        var s = new RunStats();
        s.RecordSupport(1, 2, SupportKind.Energy, AwardBuilder.MinEnergyGiven - 1);
        s.RecordSupport(1, 2, SupportKind.Cards, AwardBuilder.MinCardsGiven - 1);
        s.RecordSupport(1, 2, SupportKind.Block, AwardBuilder.MinBlockGiven - 1);
        s.RecordSupport(1, 2, SupportKind.Buffs, AwardBuilder.MinBuffsGiven - 1);
        s.RecordSupport(1, 2, SupportKind.Draws, AwardBuilder.MinDrawsGiven - 1);
        IReadOnlyList<Award> a = AwardBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>());
        foreach (string title in new[] { AwardBuilder.Battery, AwardBuilder.CarePackage, AwardBuilder.Bodyguard, AwardBuilder.Coach, AwardBuilder.Playmaker })
            Check.True(Find(a, title) == null, $"{title} below its minimum");
    }

    [Test]
    public static void SoloRunsGetNoSupportAwards()
    {
        var s = new RunStats();
        s.RecordSupport(1, 2, SupportKind.Energy, 9); // a player who isn't in the run
        IReadOnlyList<Award> a = AwardBuilder.Build(s, new[] { Alice }, new Dictionary<ulong, DefenseTotals>());
        Check.True(Find(a, AwardBuilder.Battery) == null, "no teammates, no Battery");
    }

    [Test]
    public static void SupportAwardsComeStraightAfterProtectorAndTiesGoToTheHigherRank()
    {
        var s = new RunStats();
        s.RecordDebuffPrevented(2, Weak, 12);
        s.RecordBlocked(1, 80);
        s.RecordSupport(1, 2, SupportKind.Energy, 5);
        s.RecordSupport(2, 1, SupportKind.Energy, 5);
        IReadOnlyList<Award> a = AwardBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>());
        List<string> order = a.Select(x => x.Title).ToList();
        Check.True(order.IndexOf(AwardBuilder.Protector) < order.IndexOf(AwardBuilder.Battery), "after Protector");
        Check.True(order.IndexOf(AwardBuilder.Battery) < order.IndexOf(AwardBuilder.Wall), "before Wall");
        Check.Equal("Alice", Find(a, AwardBuilder.Battery)!.PlayerName, "a tie goes to the higher-ranked player");
        Check.Equal(AwardBuilder.Battery, AwardBuilder.Headline(a, 1), "Alice's headline is her support award, not Wall");
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- AwardTests`
Expected: build error, `AwardBuilder.Battery` doesn't exist.

- [ ] **Step 3: Implement**

In `AwardBuilder.cs`:

1. After `MinSources`:

```csharp
    /// <summary>What a support award needs, so one stray gift doesn't win a title.</summary>
    public const int MinEnergyGiven = 2, MinCardsGiven = 3, MinBlockGiven = 10, MinBuffsGiven = 3, MinDrawsGiven = 3;
```

2. Replace the title constants statement (`public const string Clutch = …, PunchingBag = "WHO_CARRIED.award.punching_bag";`) with the same statement plus five titles, then add the support table after it:

```csharp
    public const string Clutch = "WHO_CARRIED.award.clutch", HeavyHitter = "WHO_CARRIED.award.heavy_hitter", Enabler = "WHO_CARRIED.award.enabler", Protector = "WHO_CARRIED.award.protector",
        Wall = "WHO_CARRIED.award.wall", SiegeBreaker = "WHO_CARRIED.award.siege_breaker", FightLeader = "WHO_CARRIED.award.fight_leader", JackOfAllTrades = "WHO_CARRIED.award.jack_of_all_trades",
        CardFactory = "WHO_CARRIED.award.card_factory", Unscathed = "WHO_CARRIED.award.unscathed", PunchingBag = "WHO_CARRIED.award.punching_bag",
        Battery = "WHO_CARRIED.award.battery", CarePackage = "WHO_CARRIED.award.care_package", Bodyguard = "WHO_CARRIED.award.bodyguard",
        Coach = "WHO_CARRIED.award.coach", Playmaker = "WHO_CARRIED.award.playmaker";

    /// <summary>The support awards, in award order: title, what it counts, its minimum, and its detail.</summary>
    private static readonly (string Title, SupportKind Kind, int Min, string Detail)[] SupportAwards =
    {
        (Battery, SupportKind.Energy, MinEnergyGiven, "WHO_CARRIED.award.battery_detail"),
        (CarePackage, SupportKind.Cards, MinCardsGiven, "WHO_CARRIED.award.care_package_detail"),
        (Bodyguard, SupportKind.Block, MinBlockGiven, "WHO_CARRIED.award.bodyguard_detail"),
        (Coach, SupportKind.Buffs, MinBuffsGiven, "WHO_CARRIED.award.coach_detail"),
        (Playmaker, SupportKind.Draws, MinDrawsGiven, "WHO_CARRIED.award.playmaker_detail"),
    };
```

3. In `Build`, directly after the `Protector` block and before `PlayerInfo? wall = …`:

```csharp
        // Help given straight to teammates, one award per kind. Before Wall, so a support player's headline is this.
        foreach ((string title, SupportKind kind, int min, string detail) in SupportAwards)
        {
            PlayerInfo? giver = Most(byRank, p => T(p)?.Given(kind) ?? 0);
            if (giver != null && T(giver)!.Given(kind) >= min) Give(title, giver, Num(T(giver)!.Given(kind)), Loc.Text(detail));
        }
```

4. `eng.json`, in alphabetical order among the `WHO_CARRIED.award.*` keys:

```json
  "WHO_CARRIED.award.battery": "Battery",
  "WHO_CARRIED.award.battery_detail": "energy given to teammates",
  "WHO_CARRIED.award.bodyguard": "Bodyguard",
  "WHO_CARRIED.award.bodyguard_detail": "block given to teammates",
  "WHO_CARRIED.award.care_package": "Care package",
  "WHO_CARRIED.award.care_package_detail": "cards given to teammates",
  "WHO_CARRIED.award.coach": "Coach",
  "WHO_CARRIED.award.coach_detail": "buff stacks given to teammates",
  "WHO_CARRIED.award.playmaker": "Playmaker",
  "WHO_CARRIED.award.playmaker_detail": "cards teammates drew",
```

5. `zhs.json`, same keys, same positions:

```json
  "WHO_CARRIED.award.battery": "充电宝",
  "WHO_CARRIED.award.battery_detail": "给队友的能量",
  "WHO_CARRIED.award.bodyguard": "保镖",
  "WHO_CARRIED.award.bodyguard_detail": "给队友的格挡",
  "WHO_CARRIED.award.care_package": "爱心包裹",
  "WHO_CARRIED.award.care_package_detail": "给队友的卡牌",
  "WHO_CARRIED.award.coach": "教练",
  "WHO_CARRIED.award.coach_detail": "给队友的增益层数",
  "WHO_CARRIED.award.playmaker": "组织核心",
  "WHO_CARRIED.award.playmaker_detail": "让队友抽的牌",
```

6. Update the class summary's award count if it names one ("eleven" → "sixteen") anywhere in `AwardBuilder.cs` or `README.md`'s Awards bullet. The README's bullet lists every award by name: add the five after Protector.

- [ ] **Step 4: Run the tests to see them pass**

Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: all PASS, `LocalizationTests` included.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/AwardBuilder.cs src/WhoCarried/Localization/eng.json src/WhoCarried/Localization/zhs.json tests/WhoCarried.Tests/AwardTests.cs README.md
git commit -m "Award the players who gave the most energy, cards, block, buffs and draws" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Room for sixteen awards

**Files:**
- Create: `src/WhoCarried/Core/AwardGrid.cs`
- Modify: `src/WhoCarried/UI/AwardsTab.cs`
- Test: `tests/WhoCarried.Tests/AwardTests.cs`

**Interfaces:**
- Produces: `AwardGrid.For(int count)` → `AwardGrid.Layout(float Width, float Step, int Columns, float RowStep)`.

- [ ] **Step 1: Write the failing tests**

Append to `AwardTests`:

```csharp
    [Test]
    public static void UpToTenAwardsLayOutAsTheyAlwaysDid()
    {
        // The old rule: one row up to five, else two; steps of 1130 / columns, 226 at most; cards 20 narrower.
        foreach (int count in Enumerable.Range(1, 10))
        {
            int rows = count <= 5 ? 1 : 2, columns = (count + rows - 1) / rows;
            float step = Math.Min(226, 1130f / columns);
            AwardGrid.Layout g = AwardGrid.For(count);
            Check.Equal(columns, g.Columns, $"{count} awards: columns");
            Check.Near(step, g.Step, $"{count} awards: step", 0.001);
            Check.Near(step - 20, g.Width, $"{count} awards: width", 0.001);
        }
    }

    [Test]
    public static void ManyAwardsFitTheSpreadBothWays()
    {
        foreach (int count in Enumerable.Range(11, 6))
        {
            AwardGrid.Layout g = AwardGrid.For(count);
            int rows = (count + g.Columns - 1) / g.Columns;
            Check.True(rows == 3, $"{count} awards: three rows");
            Check.True(g.Columns * g.Step - AwardGrid.GapX <= AwardGrid.AreaW + 0.01, $"{count} awards fit across");
            Check.True((rows - 1) * g.RowStep + g.Width * HandLayout.Aspect <= AwardGrid.AreaH + 0.01, $"{count} awards fit down");
        }
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- AwardTests`
Expected: build error, `AwardGrid` doesn't exist.

- [ ] **Step 3: Implement**

Create `src/WhoCarried/Core/AwardGrid.cs`:

```csharp
namespace WhoCarried.Core;

/// <summary>How the Awards tab spreads its cards over its 1130 × 640 area.</summary>
public static class AwardGrid
{
    public const float AreaW = 1130, AreaH = 640, MaxStep = 226, GapX = 20, GapY = 22;

    /// <param name="Width">A card's width; its height is Width × <see cref="HandLayout.Aspect"/>.</param>
    /// <param name="Step">From one card's left edge to the next one's.</param>
    /// <param name="RowStep">From one row's top to the next one's.</param>
    public readonly record struct Layout(float Width, float Step, int Columns, float RowStep);

    /// <summary>One row up to five awards, two up to ten, three beyond: as big as fits across and down, 206 wide at most.</summary>
    public static Layout For(int count)
    {
        int rows = count <= 5 ? 1 : count <= 10 ? 2 : 3;
        int columns = Math.Max(1, (count + rows - 1) / rows);
        float tallest = (AreaH - GapY * (rows - 1)) / rows / HandLayout.Aspect + GapX;
        float step = Math.Min(Math.Min(MaxStep, AreaW / columns), tallest);
        float width = step - GapX;
        return new Layout(width, step, columns, width * HandLayout.Aspect + GapY);
    }
}
```

In `src/WhoCarried/UI/AwardsTab.cs`, inside `Apply`, replace

```csharp
                (float width, float step, int columns) = Grid(v.Awards.Count);
                for (int i = 0; i < v.Awards.Count; i++)
                {
                    Award award = v.Awards[i];
                    (CardFace face, Label value, Label detail) = AwardCard(k, award, width);
                    k.At(face.Root, (i % columns) * step, (i / columns) * (width * CardFace.Aspect + 22));
```

with

```csharp
                AwardGrid.Layout grid = AwardGrid.For(v.Awards.Count);
                for (int i = 0; i < v.Awards.Count; i++)
                {
                    Award award = v.Awards[i];
                    (CardFace face, Label value, Label detail) = AwardCard(k, award, grid.Width);
                    k.At(face.Root, (i % grid.Columns) * grid.Step, (i / grid.Columns) * grid.RowStep);
```

and delete the private `Grid(int count)` method and its summary.

- [ ] **Step 4: Run the tests, and build**

Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: all PASS.
Run: `"C:/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/AwardGrid.cs src/WhoCarried/UI/AwardsTab.cs tests/WhoCarried.Tests/AwardTests.cs
git commit -m "Make room for a third row of awards" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Catch the gifts in the game

The Game layer has no tests; this task is checked by building, and by hand in Task 11.

**Files:**
- Create: `src/WhoCarried/Game/SupportGiver.cs`
- Modify: `src/WhoCarried/Game/GameCompat.cs`, `src/WhoCarried/Game/Tracker.cs`, `src/WhoCarried/Game/Patches.cs`

**Interfaces:**
- Consumes: `SupportCredit.Giver` (Task 2), `LogReplay.SupportLine` / `SupportWord` (Task 3), `RunStats.RecordSupport` (Task 1), `EffectSources.Running`, `FactsExtractor.Candidate(AbstractModel)` / `PlayerIdOf(Creature?)`, `GameCompat.ModelStack(PlayerChoiceContext?)`.
- Produces: `Tracker.OnEnergyGained(Player, decimal)`, `Tracker.OnBlockGained(Creature, decimal, CardModel?)`, `Tracker.OnCardDrawn(PlayerChoiceContext?, CardModel, bool)`.

Game facts this relies on, checked against the installed `sts2.dll`:
- `PlayerCmd.GainEnergy` hands its final amount to `PlayerCombatState.GainEnergy(decimal amount)`, the only caller. `PlayerCombatState` keeps its player in `private readonly Player _player`.
- `Hook.AfterBlockGained(ICombatState combatState, Creature creature, decimal amount, ValueProp props, CardModel? cardSource)`: `amount` is after modifiers, and can be 0.
- `Hook.AfterCardDrawn(ICombatState combatState, PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)`.
- `CombatManager.IsExecutingCardOrPotionEffect(Player player)`: public on the installed game, and may be missing on the public branch, so it's looked up by name.
- `RunManager.Instance.ActionExecutor.CurrentlyRunningAction?.OwnerId` (`ulong`).
- Glimpse Beyond and Largesse create cards whose `Owner` is the teammate and whose `creator` is the caster.

- [ ] **Step 1: GameCompat lookups**

In `GameCompat.cs`, add beside the other `Lazy` fields:

```csharp
    private static readonly Lazy<Func<Player, bool>?> ExecutingImpl = new(FindExecuting);
```

Add these public members, after `ModelStack`:

```csharp
    /// <summary>Whether a player's card or potion is taking effect right now; null if this game can't say.</summary>
    public static Func<Player, bool>? ExecutingCardOrPotion => ExecutingImpl.Value;

    /// <summary>The player whose action is running (a card played, a potion used); null if none.</summary>
    public static ulong? RunningActionOwner() => RunManager.Instance?.ActionExecutor?.CurrentlyRunningAction?.OwnerId;
```

Add to `Describe()`'s list, after the model-stack entry:

```csharp
            Probe("card effects", () => ExecutingImpl.Value),
```

Add the finder beside the other `Find…` methods:

```csharp
    private static Func<Player, bool>? FindExecuting()
    {
        if (AccessTools.Method(typeof(CombatManager), "IsExecutingCardOrPotionEffect", new[] { typeof(Player) }) is not MethodInfo method)
            return null;
        var call = (Func<CombatManager, Player, bool>)Delegate.CreateDelegate(typeof(Func<CombatManager, Player, bool>), method);
        return player => CombatManager.Instance is CombatManager combat && call(combat, player);
    }
```

Add `using MegaCrit.Sts2.Core.Entities.Players;` if it isn't there. Also mention the method in the class summary's list of APIs that differ: "- Card effects in progress: `CombatManager.IsExecutingCardOrPotionEffect`, looked up by name; without it the running action's owner is used."

- [ ] **Step 2: SupportGiver**

Create `src/WhoCarried/Game/SupportGiver.cs`:

```csharp
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>Gathers what the game can say about who gave something, for <see cref="SupportCredit.Giver"/> to decide.</summary>
internal static class SupportGiver
{
    /// <param name="named">A giver the game named itself; wins when there is one.</param>
    public static ulong? Find(IRunState? run, ulong? named = null)
    {
        ulong? turnEffect = EffectSources.Running is AbstractModel effect ? FactsExtractor.Candidate(effect).OwnerId : null;
        IReadOnlyCollection<ulong>? midEffect = MidEffect(run);
        ulong? actionOwner = null;
        // Only asked on a game without the card-effect check; a missing member there shouldn't lose the gift.
        if (midEffect == null)
        {
            try { actionOwner = GameCompat.RunningActionOwner(); }
            catch (Exception) { }
        }
        return SupportCredit.Giver(named, turnEffect, midEffect, actionOwner);
    }

    /// <summary>What was running, for the log: the turn hook's content, or "?".</summary>
    public static string Running() => EffectSources.Running is AbstractModel effect ? effect.Id.Entry : "?";

    private static IReadOnlyCollection<ulong>? MidEffect(IRunState? run)
    {
        if (run == null || GameCompat.ExecutingCardOrPotion is not { } executing) return null;
        return run.Players.Where(p => executing(p)).Select(p => p.NetId).ToList();
    }
}
```

- [ ] **Step 3: Tracker handlers**

In `Tracker.cs`, add after `OnCardCreated`:

```csharp
    /// <summary>
    /// Help one player gave another. A missing giver is logged, not guessed; gifts to yourself are dropped (see
    /// <see cref="RunStats.RecordSupport"/>).
    /// </summary>
    private static void Support(ulong? giver, ulong recipient, SupportKind kind, int amount, string source)
    {
        if (amount <= 0) return;
        if (giver is not ulong from)
        {
            _log?.Write($"{Where} support: no giver for {amount} {LogReplay.SupportWord(kind)} to {NameOf(recipient)} | {source}");
            return;
        }
        if (from == recipient) return;
        _stats.RecordSupport(from, recipient, kind, amount);
        _log?.Write($"{Where} " + LogReplay.SupportLine(NameOf(from), NameOf(recipient), kind, amount, source));
        Touch();
    }

    /// <summary>Energy landing on a player, after the game's modifiers.</summary>
    public static void OnEnergyGained(Player recipient, decimal amount) =>
        Support(SupportGiver.Find(_run), recipient.NetId, SupportKind.Energy, (int)amount, SupportGiver.Running());

    /// <summary>Block a player or their pet gained, after modifiers: a teammate's card that gave it names the giver.</summary>
    public static void OnBlockGained(Creature creature, decimal amount, CardModel? cardSource)
    {
        if (FactsExtractor.PlayerIdOf(creature) is not ulong recipient) return;
        Support(SupportGiver.Find(_run, cardSource?.Owner?.NetId), recipient, SupportKind.Block, (int)amount,
            cardSource?.Id.Entry ?? SupportGiver.Running());
    }

    /// <summary>A card drawn outside the normal hand draw, credited to the card that made its owner draw.</summary>
    public static void OnCardDrawn(PlayerChoiceContext? context, CardModel card, bool fromHandDraw)
    {
        if (fromHandDraw || card.Owner is not Player recipient) return;
        CardModel? by = GameCompat.ModelStack(context).FirstOrDefault() as CardModel;
        Support(SupportGiver.Find(_run, by?.Owner?.NetId), recipient.NetId, SupportKind.Draws, 1,
            by?.Id.Entry ?? SupportGiver.Running());
    }
```

In `OnCardCreated`, after the `_stats.RecordCardCreated(…)` line:

```csharp
        // Made straight into a teammate's piles (Glimpse Beyond's Souls, Largesse): that's a gift too.
        if (card.Owner is Player owner && owner.NetId != creator.NetId)
            Support(creator.NetId, owner.NetId, SupportKind.Cards, 1, id);
```

In `OnPowerChanged`, directly after `TrackStrengthLoss(power, amount, applier);`:

```csharp
        // A buff one player put on another (Blaze, Fade, Coordinate): only a player applier counts, so self-buffs and
        // relic buffs with no applier never reach the log.
        if (amount > 0 && power.Type == PowerType.Buff && FactsExtractor.PlayerIdOf(power.Owner) is ulong buffed &&
            FactsExtractor.PlayerIdOf(applier) is ulong buffer)
            Support(buffer, buffed, SupportKind.Buffs, (int)Math.Round(amount), power.Id.Entry);
```

- [ ] **Step 4: Patches**

In `Patches.cs`, add `using MegaCrit.Sts2.Core.Entities.Players;` if missing (it's there already), and after `BeforeBlockGainedPatch`:

```csharp
/// <summary>After a creature gains block, with the amount after modifiers: where block given to a teammate is counted.</summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterBlockGained))]
internal static class AfterBlockGainedPatch
{
    private static void Prefix(Creature creature, decimal amount, CardModel? cardSource)
    {
        try { Tracker.OnBlockGained(creature, amount, cardSource); }
        catch (Exception e) { Tracker.LogError("AfterBlockGained", e); }
    }
}

/// <summary>
/// Where energy lands, after the game's modifiers: PlayerCmd.GainEnergy's only way in. Energy given to a teammate is
/// counted here.
/// </summary>
[HarmonyPatch(typeof(PlayerCombatState), nameof(PlayerCombatState.GainEnergy))]
internal static class GainEnergyPatch
{
    private static void Prefix(decimal amount, Player ____player)
    {
        try { Tracker.OnEnergyGained(____player, amount); }
        catch (Exception e) { Tracker.LogError("PlayerCombatState.GainEnergy", e); }
    }
}

/// <summary>Every card drawn; the hand draw is flagged. Where draws given to a teammate are counted.</summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardDrawn))]
internal static class AfterCardDrawnPatch
{
    private static void Prefix(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        try { Tracker.OnCardDrawn(choiceContext, card, fromHandDraw); }
        catch (Exception e) { Tracker.LogError("AfterCardDrawn", e); }
    }
}
```

`PlayerCombatState` is in `MegaCrit.Sts2.Core.Entities.Players`.

- [ ] **Step 5: Build and run the tests**

Run: `"C:/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release`
Expected: Build succeeded, 0 errors. If a `using` is missing (for example `PowerType` in `Tracker.cs` comes from `MegaCrit.Sts2.Core.Entities.Powers`, already imported), add it.
Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/WhoCarried/Game/SupportGiver.cs src/WhoCarried/Game/GameCompat.cs src/WhoCarried/Game/Tracker.cs src/WhoCarried/Game/Patches.cs
git commit -m "Catch energy, cards, block, buffs and draws given to teammates" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: The Support tab

**Files:**
- Create: `src/WhoCarried/UI/SupportTab.cs`
- Modify: `src/WhoCarried/UI/RecapPanel.cs`, `src/WhoCarried/UI/DevPreview.cs`, `src/WhoCarried/UI/Replay.cs`, `src/WhoCarried/UI/GameArt.cs`, `src/WhoCarried/UI/RecapTexts.cs`, `src/WhoCarried/Localization/eng.json`, `src/WhoCarried/Localization/zhs.json`

**Interfaces:**
- Consumes: `RecapView.Support`, `RecapView.HasSupport`, `SupportRow` (Task 4); award keys (Task 5); `DefenseTab.Portrait(Kit, string?, Color, float, float)`, `KeyedRows<T>`, `LiveNumber`, `Kit`.
- Produces: `SupportTab.Create(Kit, RecapView, Live)`, `SupportTab.Plates(Kit, RecapView, float width, int columns, float height, Live? live, bool compact = false)` (Task 9 uses `Plates`); `GameArt.DrawPile`.

- [ ] **Step 1: Draw pile art**

In `GameArt.cs`, add `DrawPile = "draw_pile"` to the `public const string` list (after `Perfect`, keeping the `;` at the end), and to `Paths`:

```csharp
        [DrawPile] = "res://images/packed/combat_ui/draw_pile.png",
```

(The path is in the installed `SlayTheSpire2.pck`.)

- [ ] **Step 2: Award art**

In `RecapTexts.AwardArt`, before the `_ =>` arm:

```csharp
        AwardBuilder.Battery => GameArt.Get(GameArt.Energy),
        AwardBuilder.CarePackage => GameArt.Get(GameArt.Cards),
        AwardBuilder.Bodyguard => GameArt.Get(GameArt.Block),
        AwardBuilder.Coach => k.Icon(DebuffBuilder.IconPrefix + "STRENGTH_POWER"),
        AwardBuilder.Playmaker => GameArt.Get(GameArt.DrawPile) ?? GameArt.Get(GameArt.Deck),
```

- [ ] **Step 3: The tab**

Create `src/WhoCarried/UI/SupportTab.cs`:

```csharp
using Godot;
using WhoCarried.Core;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// What each player gave their teammates, as nameplates: their portrait, then the energy, cards, block, buffs and draws
/// they gave. With nothing given (or nobody to give to) it says so instead.
/// </summary>
internal static class SupportTab
{
    public static Control Create(Kit k, RecapView view, Live live)
    {
        Control tab = k.Box(RecapPanel.DesignW, RecapPanel.DesignH);
        // Two plates a row; five or more players (modded lobbies) get shorter plates so everything still fits.
        int rows = (Math.Max(1, view.Support.Count) + 1) / 2;
        float plateH = rows <= 2 ? 230 : Math.Max(150, (560 - 24 * (rows - 1)) / rows);
        Control plates = k.At(Plates(k, view, 1522, 2, plateH, live), 40, 146);
        tab.AddChild(plates);

        Label note = k.Text(Loc.Text("WHO_CARRIED.support.hint"), 15, RecapTheme.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        tab.AddChild(k.At(note, 40, 146 + rows * plateH + (rows - 1) * 24 + 26, 1522, -1));

        Label empty = k.Text("", 18, RecapTheme.Muted);
        empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        tab.AddChild(k.At(empty, 40, 146, 1522, -1));

        void Apply(RecapView v)
        {
            plates.Visible = note.Visible = v.HasSupport;
            empty.Visible = !v.HasSupport;
            empty.Text = v.Support.Count > 1 ? Loc.Text("WHO_CARRIED.empty.support") : Loc.Text("WHO_CARRIED.support.solo");
        }
        Apply(view);
        live.On(Apply);
        return tab;
    }

    /// <summary>The nameplates in a grid, in scoreboard order.</summary>
    public static Control Plates(Kit k, RecapView view, float width, int columns, float height, Live? live, bool compact = false)
    {
        float gap = compact ? 18 : 30;
        var grid = new GridContainer { Columns = columns, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", k.F(gap));
        grid.AddThemeConstantOverride("v_separation", k.F(compact ? 14 : 24));
        float plateW = (width - gap * (columns - 1)) / columns;
        var plates = new KeyedRows<SupportRow>(grid, r => r.Label, r => Plate(k, r, plateW, height, compact));
        void Sync(RecapView v) => plates.Sync(v.Support);
        Sync(view);
        live?.On(Sync);
        return grid;
    }

    /// <summary>Each stat's icon, colour, "{0} energy"-style words, and value.</summary>
    private static List<(Texture2D? Icon, Color Tone, string Words, Func<SupportRow, int> Value)> Stats(Kit k) => new()
    {
        (GameArt.Get(GameArt.Energy), RecapTheme.Gold, "WHO_CARRIED.support.energy", r => r.Energy),
        (GameArt.Get(GameArt.Cards), RecapTheme.Text, "WHO_CARRIED.support.cards", r => r.Cards),
        (GameArt.Get(GameArt.Block), RecapTheme.Blocked, "WHO_CARRIED.support.block", r => r.Block),
        (k.Icon(DebuffBuilder.IconPrefix + "STRENGTH_POWER"), RecapTheme.Taken, "WHO_CARRIED.support.buffs", r => r.Buffs),
        (GameArt.Get(GameArt.DrawPile) ?? GameArt.Get(GameArt.Deck), RecapTheme.Teal, "WHO_CARRIED.support.draws", r => r.Draws),
    };

    private static (Control, Action<SupportRow>) Plate(Kit k, SupportRow row, float width, float height, bool compact)
    {
        Color color = RecapTheme.FromHex(row.ColorHex), accent = RecapTheme.Accent(row.ColorHex);
        PanelContainer tip = compact ? k.Tip(10, 8, new Color(accent, 0.47f)) : k.Tip(24, 20, new Color(accent, 0.47f));
        tip.CustomMinimumSize = k.V(width, height);
        HBoxContainer line = k.Row(compact ? 12 : 24);
        tip.AddChild(line);
        float portraitH = compact ? 84 : height - 42, portraitW = compact ? 66 : Math.Min(170, portraitH * 0.78f);
        line.AddChild(Kit.Center(DefenseTab.Portrait(k, row.IconKey, color, portraitW, portraitH)));

        VBoxContainer right = k.Column(compact ? 6 : 10);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(Kit.Center(right));
        Label name = k.Text(row.Label, compact ? 20 : 34, accent, true, compact ? Ink.Soft : Ink.Strong);
        k.Fit(name, width - portraitW - (compact ? 40 : 96), compact ? 14 : 20);
        right.AddChild(name);
        if (!compact) right.AddChild(k.Text(Loc.Text("WHO_CARRIED.support.gave"), 16, RecapTheme.Muted));

        var facts = new GridContainer { Columns = 3, MouseFilter = Control.MouseFilterEnum.Ignore };
        facts.AddThemeConstantOverride("h_separation", k.F(compact ? 14 : 28));
        facts.AddThemeConstantOverride("v_separation", k.F(compact ? 4 : 12));
        right.AddChild(facts);
        float icon = compact ? 18 : 30, text = compact ? 13 : 18;
        var numbers = new List<(LiveNumber Number, Func<SupportRow, int> Value)>();
        foreach ((Texture2D? art, Color tone, string words, Func<SupportRow, int> value) in Stats(k))
        {
            HBoxContainer fact = k.Row(compact ? 5 : 8);
            if (art != null) fact.AddChild(Kit.Center(k.Pic(art, icon, icon)));
            var number = new LiveNumber(k.Text("", text, tone, true, Ink.Soft), value(row), format: n => Loc.Text(words, Kit.Num(n)));
            fact.AddChild(Kit.Center(number.Control));
            facts.AddChild(fact);
            numbers.Add((number, value));
        }

        void Apply(SupportRow r)
        {
            foreach ((LiveNumber number, Func<SupportRow, int> value) in numbers) number.Set(value(r));
        }
        return (tip, Apply);
    }
}
```

- [ ] **Step 4: Put the tab between Debuffs and Timeline**

`RecapPanel.cs`:
- `Views` becomes `{ "WHO_CARRIED.tab.scoreboard", "WHO_CARRIED.tab.awards", "WHO_CARRIED.tab.sources", "WHO_CARRIED.tab.debuffs", "WHO_CARRIED.tab.support", "WHO_CARRIED.tab.timeline", "WHO_CARRIED.tab.defense", "WHO_CARRIED.tab.decks" }`.
- The tab children become:

```csharp
        tabs.AddChild(Safe(k, 3, pads[3], () => DebuffsTab.Create(k, view, live, pads[3])));
        tabs.AddChild(Safe(k, 4, pads[4], () => SupportTab.Create(k, view, live)));
        tabs.AddChild(Safe(k, 5, pads[5], () => TimelineTab.Create(k, view, live, pads[5])));
        tabs.AddChild(Safe(k, 6, pads[6], () => DefenseTab.Create(k, view, live)));
        tabs.AddChild(Safe(k, 7, pads[7], () => DecksTab.Create(k, view, cards, live, pads[7])));
```

- The class summary's "seven views" becomes "eight views".

`DevPreview.cs` `TabNames` and `Replay.cs` `Views` both become `{ "scoreboard", "awards", "sources", "debuffs", "support", "timeline", "defense", "decks" }`. Both find the Timeline by name, so nothing else moves. Search both files for other hard-coded tab indices (`CurrentTab = `, `pads[`), and shift any that point at Timeline, Defense or Decks up by one.

README's "What it shows" list: add after Debuffs:

```markdown
- **Support:** the energy, cards, block, buffs and draws each player gave their teammates.
```

and change "its seven views" in the folder table to "its eight views".

- [ ] **Step 5: Sample support in the dev preview**

In `DevPreview.BuildSample`, after `var rng = new Random(7);`:

```csharp
        var help = new Random(11); // its own stream, so the other sample numbers don't change
```

and inside the fight loop, just before `stats.EndFight();`:

```csharp
                // Co-op help. In a solo preview these are gifts to yourself, which don't count: the tab shows its hint.
                stats.RecordSupport(P(1).NetId, P(0).NetId, SupportKind.Energy, help.Next(0, 2));
                stats.RecordSupport(P(2).NetId, P(1).NetId, SupportKind.Block, help.Next(0, 12) * act);
                stats.RecordSupport(P(0).NetId, P(2).NetId, SupportKind.Buffs, help.Next(0, 3));
                stats.RecordSupport(P(2).NetId, P(0).NetId, SupportKind.Cards, fight % 2);
                stats.RecordSupport(P(3).NetId, P(1).NetId, SupportKind.Draws, help.Next(0, 3));
```

- [ ] **Step 6: Strings**

`eng.json`, each in alphabetical position:

```json
  "WHO_CARRIED.empty.support": "Help given to teammates shows here. Nothing yet.",
  "WHO_CARRIED.support.block": "{0} block",
  "WHO_CARRIED.support.buffs": "{0} buff stacks",
  "WHO_CARRIED.support.cards": "{0} cards",
  "WHO_CARRIED.support.draws": "{0} draws",
  "WHO_CARRIED.support.energy": "{0} energy",
  "WHO_CARRIED.support.gave": "Gave teammates",
  "WHO_CARRIED.support.hint": "What each player gave their teammates. What players give themselves isn't counted.",
  "WHO_CARRIED.support.solo": "Support is what players give their teammates in co-op.",
  "WHO_CARRIED.tab.support": "Support",
```

`zhs.json`, same keys:

```json
  "WHO_CARRIED.empty.support": "给予队友的支援会显示在这里。目前还没有。",
  "WHO_CARRIED.support.block": "{0} 点格挡",
  "WHO_CARRIED.support.buffs": "{0} 层增益",
  "WHO_CARRIED.support.cards": "{0} 张卡牌",
  "WHO_CARRIED.support.draws": "{0} 次抽牌",
  "WHO_CARRIED.support.energy": "{0} 点能量",
  "WHO_CARRIED.support.gave": "给队友的支援",
  "WHO_CARRIED.support.hint": "每位玩家给予队友的帮助。给自己的不计入。",
  "WHO_CARRIED.support.solo": "支援是联机时玩家给予队友的帮助。",
  "WHO_CARRIED.tab.support": "支援",
```

- [ ] **Step 7: Build and run the tests**

Run: `"C:/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release`
Expected: Build succeeded, 0 errors.
Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: all PASS, `LocalizationTests` included.

- [ ] **Step 8: Commit**

```bash
git add src/WhoCarried/UI src/WhoCarried/Localization README.md
git commit -m "Add a Support tab: what each player gave their teammates" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Support on the exported image

**Files:**
- Modify: `src/WhoCarried/UI/SummaryCard.cs`

**Interfaces:**
- Consumes: `SupportTab.Plates` (Task 8), `RecapView.HasSupport` (Task 4), keys `WHO_CARRIED.tab.support` and `WHO_CARRIED.support.hint` (Task 8).

- [ ] **Step 1: Add the section**

In `SummaryCard.Create`, directly after the Debuffs section's `if` block and before the Defense one:

```csharp
        // Only when someone gave a teammate something: a solo run, or a co-op run with no gifts, leaves it out.
        if (view.HasSupport)
            body.AddChild(Section(k, Loc.Text("WHO_CARRIED.tab.support"), GameArt.Get(GameArt.Energy),
                SupportTab.Plates(k, view, Inner, 2, 104, null, compact: true), Loc.Text("WHO_CARRIED.support.hint")));
```

- [ ] **Step 2: Build and run the tests**

Run: `"C:/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release`
Expected: Build succeeded, 0 errors.
Run: `"C:/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: all PASS.

- [ ] **Step 3: Commit**

```bash
git add src/WhoCarried/UI/SummaryCard.cs
git commit -m "Put support on the exported image when there's any" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Changelog and the 1.2.0 change note

**Files:**
- Modify: `CHANGELOG.md`
- Modify (outside the repo, untracked): `E:\Projects\Who-Carried-workshop\changenote-1.2.0-draft.txt`

- [ ] **Step 1: CHANGELOG**

Under `## [Unreleased]` → `### Added`, after the Simplified Chinese entry:

```markdown
- **Support**, a new tab: the energy, cards, block, buffs and draws each player gave their teammates (Believe In You, Glimpse Beyond, Rally, Blaze, Huddle Up…). Help a player gives themselves isn't counted. On the exported image when anyone gave a teammate anything. Five awards go with it: **Battery**, **Care package**, **Bodyguard**, **Coach** and **Playmaker**, and the Awards tab takes a third row when it needs one.
```

- [ ] **Step 2: Draft change note**

Read `E:\Projects\Who-Carried-workshop\changenote-1.2.0-draft.txt`, and add the same news in its existing style (plain Steam text, no Markdown).

- [ ] **Step 3: Commit (the repo file only)**

```bash
git add CHANGELOG.md
git commit -m "Changelog: support stats" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Check it in the game

Nothing here is automated. The user plays the co-op parts; report each result with its `events.log` lines.

- [ ] **Step 1: Deploy.** Close the game, then run `powershell -File tools/deploy.ps1`. In the game's mod settings, make sure the local copy is the one enabled, not the Workshop's. After launch, check `godot.log`'s `Loading assembly DLL` lines, and `events.log` for `card effects: ok` in the GameCompat line.
- [ ] **Step 2: Previews.** Write `4` to `preview.flag` in the data folder (`%APPDATA%\SlayTheSpire2\WhoCarried`), launch through Steam (`E:\Steam\steam.exe -applaunch 2868840`), wait for the screenshots, close the game and delete the flag. Check the support tab (now `preview-5-support.png`): four plates, five stats each, no overflow. The export image has a Support section and the Awards tab fits. Repeat with `1`: the Support tab shows the solo line, and the export has no Support section. Repeat with `IRONCLAD,SILENT,DEFECT,REGENT,NECROBINDER` for five plates. Repeat once with the game in Simplified Chinese.
- [ ] **Step 3: Energy.** In co-op: Believe In You on a teammate, Energy Surge, an Energy Potion thrown at a teammate. Each gives a `gave N energy to` line naming the card or potion's player. The recipient's own energy gains write nothing.
- [ ] **Step 4: Cards.** Glimpse Beyond gives `gave 1 cards to` lines per Soul, per teammate. A player's own Shivs write nothing.
- [ ] **Step 5: Block.** Rally or Demonic Shield give `gave N block to`. A player's own Defend writes nothing.
- [ ] **Step 6: Buffs.** Blaze on a teammate gives `gave N buffs to`. A player's own Inflame writes nothing.
- [ ] **Step 7: Draws.** Huddle Up gives `gave 1 draws to` lines. The start-of-turn hand draw writes nothing.
- [ ] **Step 8: Noise.** Count `support: no giver` lines over a whole vanilla fight. More than a handful means a common gain has no giver: note what `source` they name and report it before release.
- [ ] **Step 9: Replay.** Put `replay.flag` in the data folder and launch. The replayed Support tab matches the live one.

---

## Self-review

- **Spec coverage:** counters and self-rule (T1); giver order and the no-guess rule (T2, T7); log lines and replay (T3); view rows and `HasSupport` (T4); five awards, order, minimums, co-op only (T5); Awards grid (T6); five hooks and GameCompat fallback (T7); tab, placement, empty states, icons (T8); image section only when `HasSupport` (T9); localization in T5 and T8; changelog and draft note (T10); in-game checks (T11).
- **Names used across tasks:** `SupportKind`, `RecordSupport`, `Given`, `SupportCredit.Giver`, `SupportLine` / `SupportWord`, `SupportRow` / `Support` / `HasSupport`, `AwardGrid.For` / `Layout`, `SupportTab.Plates`, `GameArt.DrawPile`: each is defined once and used under that name.
