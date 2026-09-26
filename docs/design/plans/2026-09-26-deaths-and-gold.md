# Deaths and gold — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show each co-op player's deaths as a line on their scoreboard card, and each player's gold earned in the Decks tab.

**Architecture:** Deaths are counted live from a new `Hook.AfterDeath` patch, straight into `RunStats`, except for the kills the game does to end a run that's already won or being abandoned. Gold comes from the game's own per-floor history, which the Defense tab already reads (`GameReader.Defense` live, `RunHistory.Parse` for replays). It travels on `DefenseTotals` into `DeckView`. The UI adds one line to `ScoreboardTab.PlayerCard` and one item to the Decks tab header and the export's deck columns.

**Tech Stack:** C# / .NET 9, Harmony, the game's GodotSharp API, the repo's console test runner.

**Spec:** [Deaths on the card, gold in the Decks tab](../specs/2026-09-26-deaths-and-gold-design.md).

## Global constraints

- Work on a branch, `feat/deaths-and-gold`, made from `main` before Task 1. Commit after each task as the steps say. Don't push.
- Build and test with `C:\Program Files\dotnet\dotnet.exe`: the x86 dotnet first on PATH can't see the x64 .NET 9 SDK. In bash: `"/c/Program Files/dotnet/dotnet.exe"`.
- Every new on-screen English string gets its `zhs.json` entry in the same change, using the game's own Chinese terms (金币 for gold).
- A sentence is one localization template: never split it into translated fragments or colour a number inside it. Singular and plural counts use separate keys (`…_one`).
- Every real player death counts, the wipe that loses the run included. Not counted: kills after the run has ended (`RunStats.Finished`: the game kills everyone after the Architect win), kills while the run is being abandoned (`RunManager.Instance.IsAbandoned`), and deaths something stopped (`wasRemovalPrevented`).
- Nothing is hard-coded per character. Pets (Osty) and enemies are never counted as deaths.
- The damage number, caption, chips and all font sizes on the card stay as they are. Only the gaps between lines change: chips 0.7 → 0.5 em, bonus line 0.45 → 0.3 em, deaths line 0.3 em.
- The recap never runs game commands: the new patch only reads.
- Log lines are the replay's input: a counted death is exactly `{where} {name} died`, and a skipped one is `{where} {name} killed as the run ends, not counted`.

## Review focus

1. **Winning the run** makes the game force-kill every player after the Architect (`RunManager.WinRun` → `GuaranteeKillAllPlayers`). No card may gain a death from it. `WinRun` reports the victory first, so Task 3 skips deaths once `RunStats.Finished` is set. The same routine ends an abandoned run, before the run's end is reported, so Task 3 also skips deaths while `RunManager.Instance.IsAbandoned`. This is game glue with no unit test: a reviewer checks both guards, and a won solo run confirms the first (see "Still open" at the end).
2. **A death stopped by Fairy in a Bottle** reaches `Hook.AfterDeath` with `wasRemovalPrevented = true` and must not count. Task 3's patch skips it; a reviewer checks the condition.
3. **Osty dying** goes through `CreatureCmd.Kill` too; `creature.IsPlayer` is false for pets, so it must not count. Task 3's patch checks `IsPlayer` and `Player`; a reviewer checks the condition.
4. **Cards without a deaths line beside cards with one:** the text box centres its lines, so numbers sit at different heights. That's expected. Nothing may overflow the box or run into the badges on 5-player cards in Chinese. Task 5 checks it.
5. **A save from before this change** has no `Deaths` field and must load as 0, not fail. Task 1 tests it.

## Files

| File | Change |
|---|---|
| `src/WhoCarried/Core/RunStats.cs` | `PlayerTotals.Deaths`; `RunStats.RecordDeath` |
| `src/WhoCarried/Core/LogReplay.cs` | `Died` line text and its parse |
| `src/WhoCarried/Core/RecapBuilder.cs` | `BarRow.Deaths`; `DefenseTotals.Gold`; pass history totals to `DeckBuilder` |
| `src/WhoCarried/Core/RunHistory.cs` | Sum `gold_gained` into `PlayerRecord.Gold` |
| `src/WhoCarried/Core/DeckBuilder.cs` | `DeckView.Gold`; `Build` takes the history totals |
| `src/WhoCarried/Game/Tracker.cs` | `OnPlayerDied`: count a death, or skip it once the run is over |
| `src/WhoCarried/Game/Patches.cs` | `AfterDeathPatch` |
| `src/WhoCarried/ModEntry.cs` | Register `AfterDeathPatch` |
| `src/WhoCarried/Game/GameReader.cs` | Sum live `GoldGained` into `DefenseTotals.Gold` |
| `src/WhoCarried/UI/Replay.cs` | Pass `PlayerRecord.Gold` through |
| `src/WhoCarried/UI/GameArt.cs` | `Gold` icon |
| `src/WhoCarried/UI/ScoreboardTab.cs` | The deaths line; tighter gaps |
| `src/WhoCarried/UI/DecksTab.cs` | Gold in the header row and in the export's columns |
| `src/WhoCarried/UI/DevPreview.cs` | Sample deaths and gold |
| `src/WhoCarried/Localization/eng.json`, `zhs.json` | Three keys |
| `tests/WhoCarried.Tests/DeathsTests.cs` | New |
| `tests/WhoCarried.Tests/GoldTests.cs` | New |
| `README.md`, `CHANGELOG.md`, `docs/README.md` | Document it |

---

### Task 1: Deaths in Core

**Files:**
- Modify: `src/WhoCarried/Core/RunStats.cs`, `src/WhoCarried/Core/LogReplay.cs`, `src/WhoCarried/Core/RecapBuilder.cs`
- Create: `tests/WhoCarried.Tests/DeathsTests.cs`

**Interfaces:**
- Produces: `int PlayerTotals.Deaths { get; set; }`; `void RunStats.RecordDeath(ulong playerId, int count = 1)`; `const string LogReplay.Died = "died"`; `BarRow(..., int Deaths = 0)` as the record's last parameter, filled for scoreboard rows.

- [ ] **Step 1: Make the branch**

```bash
git checkout -b feat/deaths-and-gold
```

- [ ] **Step 2: Write the failing tests**

Create `tests/WhoCarried.Tests/DeathsTests.cs`:

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class DeathsTests
{
    private static readonly PlayerInfo Alice = new(1, "Alice", "Ironclad", "d85a30");
    private static readonly PlayerInfo Bob = new(2, "Bob", "The Tailor", "7f77dd");
    private static readonly SourceRef Strike = new(SourceKind.Card, "STRIKE", "Strike");

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "current_run.json");

    [Test]
    public static void DeathsAddUpAndIgnoreNothing()
    {
        var s = new RunStats();
        s.RecordDeath(1);
        s.RecordDeath(1, 2);
        s.RecordDeath(2, 0);
        s.RecordDeath(2, -1);
        Check.Equal(3, s.Get(1)!.Deaths, "deaths add up");
        Check.True(s.Get(2) == null, "zero or fewer adds nothing");
    }

    [Test]
    public static void DeathsSurviveASave()
    {
        string path = TempFile();
        var s = new RunStats { RunKey = "SEED:123" };
        s.RecordDeath(1, 2);
        RunStatsStore.Save(s, path);
        Check.Equal(2, RunStatsStore.Load(path)!.Get(1)!.Deaths, "deaths");
    }

    [Test]
    public static void SaveFromBeforeDeathsLoadsWithNone()
    {
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "RunKey": "SEED:123", "Players": { "1": { "DamageDealt": 5 } } }""");
        RunStats? loaded = RunStatsStore.Load(path);
        Check.Equal(5, loaded!.Get(1)!.DamageDealt, "older fields still read");
        Check.Equal(0, loaded.Get(1)!.Deaths, "no deaths");
    }

    [Test]
    public static void ReplayCountsDeathsButNotTheKillsThatEndARun()
    {
        string[] log =
        {
            "Who Carried v1.2.0 - run SEED:1 started 2026-09-26 10:00",
            "player 11 = Moth (The Tailor) #5c350f",
            "player 12 = Jo (The Necrobinder) #d4537e",
            "[F2 A1] fight start: Toadpoles",
            "[F2 A1] Moth died",
            "[F2 A1] fight end, saved",
            "[F5 A1] fight start: Gremlins",
            "[F5 A1] Jo died",
            "[F5 A1] Moth died",
            "[F5 A1] fight end, saved",
            "[F52 A4] run ended: victory",
            "[F52 A4] Moth killed as the run ends, not counted",
            "[F52 A4] Jo killed as the run ends, not counted",
        };
        RunStats stats = LogReplay.Parse(log).Stats;
        Check.Equal(2, stats.Get(11)!.Deaths, "moth");
        Check.Equal(1, stats.Get(12)!.Deaths, "jo");
    }

    [Test]
    public static void ScoreboardRowsCarryDeaths()
    {
        var s = new RunStats();
        s.RecordDamage(1, Strike, 30);
        s.RecordDamage(2, Strike, 10);
        s.RecordDeath(2, 2);
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>(), "h");
        Check.Equal(0, v.Overview.Single(r => r.Label == "Alice").Deaths, "alice");
        Check.Equal(2, v.Overview.Single(r => r.Label == "Bob").Deaths, "bob");
    }
}
```

- [ ] **Step 3: Run them to see them fail**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- DeathsTests`
Expected: the build fails with `'RunStats' does not contain a definition for 'RecordDeath'` (and `Deaths`).

- [ ] **Step 4: Add the count to `RunStats`**

In `src/WhoCarried/Core/RunStats.cs`, in `PlayerTotals`, after the `LowestHpMax` property:

```csharp
    /// <summary>
    /// Times this player died, the wipe that loses a run included. The kills the game does to end a run that's already
    /// over (after a win, or when abandoning) aren't deaths.
    /// </summary>
    public int Deaths { get; set; }
```

In `RunStats`, after `RecordHp`:

```csharp
    /// <summary>A player died (see <see cref="PlayerTotals.Deaths"/>).</summary>
    public void RecordDeath(ulong playerId, int count = 1)
    {
        if (count <= 0) return;
        GetOrAdd(KeyFor(playerId)).Deaths += count;
    }
```

- [ ] **Step 5: Read deaths back from the log**

In `src/WhoCarried/Core/LogReplay.cs`, after the `Badge` regex:

```csharp
    /// <summary>The word after a player's name on a death that counts (see <see cref="RunStats.RecordDeath"/>).</summary>
    public const string Died = "died";
    private static readonly Regex Death = new(Where + @"(.+?) " + Died + "$", RegexOptions.Compiled);
```

The line for a kill that isn't counted ends in "not counted", so this pattern never matches it.

In `Parse`, after the `Badge` branch (before the loop's closing brace):

```csharp
            else if ((m = Death.Match(line)).Success)
            {
                if (Who(m.Groups[3].Value) is ulong id) stats.RecordDeath(id);
            }
```

Update the class summary's list of stats an older log never recorded: add "deaths" after "support given to teammates".

- [ ] **Step 6: Put deaths on the scoreboard rows**

In `src/WhoCarried/Core/RecapBuilder.cs`, add a param line to `BarRow`'s doc comment and the parameter at the end of the record:

```csharp
/// <param name="Deaths">Times the player died this run (scoreboard rows only); 0 = none.</param>
public sealed record BarRow(string Label, string SubLabel, int Value, double Fraction, double? Share, string ColorHex,
                            string? IconKey = null, int Bonus = 0, int BlockRemoved = 0, string Award = "",
                            IReadOnlyList<BadgeInfo>? Badges = null, string? ArtKey = null, int Deaths = 0)
```

In `Build`, the `overview` rows become:

```csharp
        var overview = byDamage
            .Select(p => new BarRow(p.Name, p.Character, dealt[p.NetId], Fraction(dealt[p.NetId], max),
                team == 0 ? 0 : (double)dealt[p.NetId] / team, p.ColorHex, IconOf(p),
                stats.Get(p.NetId)?.DebuffBonus.Values.Sum(b => b.Amount) ?? 0,
                stats.Get(p.NetId)?.BlockRemoved ?? 0,
                AwardBuilder.Headline(awards, p.NetId),
                badges[p.NetId],
                Deaths: stats.Get(p.NetId)?.Deaths ?? 0))
            .ToList();
```

- [ ] **Step 7: Run the tests**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- DeathsTests`
Expected: `5/5 passed`.

Then the whole suite: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: every test passes (the last line reads `N/N passed`).

- [ ] **Step 8: Commit**

```bash
git add src/WhoCarried/Core/RunStats.cs src/WhoCarried/Core/LogReplay.cs src/WhoCarried/Core/RecapBuilder.cs tests/WhoCarried.Tests/DeathsTests.cs
git commit -m "feat: count deaths in run stats, saves and replays"
```

---

### Task 2: Gold earned, from the game's history to the decks

**Files:**
- Modify: `src/WhoCarried/Core/RecapBuilder.cs`, `src/WhoCarried/Core/RunHistory.cs`, `src/WhoCarried/Core/DeckBuilder.cs`, `src/WhoCarried/Game/GameReader.cs`, `src/WhoCarried/UI/Replay.cs`
- Create: `tests/WhoCarried.Tests/GoldTests.cs`

**Interfaces:**
- Produces: `DefenseTotals(int Taken, int Healed, int LowestHp = 0, int LowestHpMax = 0, int Gold = 0)`; `RunHistory.PlayerRecord(..., IReadOnlyList<EarnedBadge>? Badges = null, int Gold = 0)`; `DeckView(..., IReadOnlyList<DeckEntry> Entries, int Gold = 0)`; `DeckBuilder.Build(RunStats stats, IEnumerable<PlayerInfo> players, IReadOnlyDictionary<ulong, IReadOnlyList<DeckCard>>? decks, IReadOnlyDictionary<ulong, DefenseTotals>? history = null)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/GoldTests.cs`:

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class GoldTests
{
    private static readonly PlayerInfo Alice = new(1, "Alice", "Ironclad", "d85a30");
    private static readonly PlayerInfo Bob = new(2, "Bob", "The Tailor", "7f77dd");

    [Test]
    public static void RunHistorySumsGoldEarnedPerPlayer()
    {
        const string json = """
        {
          "win": false, "start_time": 1,
          "map_point_history": [
            [ { "player_stats": [ { "player_id": 11, "gold_gained": 0, "current_gold": 99 },
                                  { "player_id": 12, "gold_gained": 0, "current_gold": 99 } ] },
              { "player_stats": [ { "player_id": 11, "gold_gained": 14, "gold_spent": 50 },
                                  { "player_id": 12, "gold_gained": 15 } ] } ],
            [ { "player_stats": [ { "player_id": 11, "gold_gained": 100, "gold_stolen": 20 } ] } ]
          ],
          "players": [ { "id": 11, "character": "CHARACTER.X", "deck": [] },
                       { "id": 12, "character": "CHARACTER.Y", "deck": [] } ]
        }
        """;
        Dictionary<ulong, RunHistory.PlayerRecord> players = RunHistory.Parse(json).Players.ToDictionary(p => p.Id);
        Check.Equal(114, players[11].Gold, "gained across acts; spending and theft don't take away");
        Check.Equal(15, players[12].Gold, "each player's own");
    }

    [Test]
    public static void DecksCarryEachPlayersGold()
    {
        var history = new Dictionary<ulong, DefenseTotals> { [1] = new(0, 0, Gold: 1072) };
        IReadOnlyList<DeckView> decks = DeckBuilder.Build(new RunStats(), new[] { Alice, Bob }, null, history);
        Check.Equal(1072, decks.Single(d => d.PlayerId == 1).Gold, "alice");
        Check.Equal(0, decks.Single(d => d.PlayerId == 2).Gold, "not in the history");
    }

    [Test]
    public static void RecapDecksTakeGoldFromTheHistoryTotals()
    {
        var history = new Dictionary<ulong, DefenseTotals> { [2] = new(10, 0, Gold: 651) };
        RecapView v = RecapBuilder.Build(new RunStats(), new[] { Alice, Bob }, history, "h");
        Check.Equal(651, v.Decks.Single(d => d.PlayerId == 2).Gold, "bob");
        Check.Equal(0, v.Decks.Single(d => d.PlayerId == 1).Gold, "alice");
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- GoldTests`
Expected: the build fails: `DefenseTotals` has no parameter named `Gold`, and `PlayerRecord` and `DeckView` have no `Gold`.

- [ ] **Step 3: Carry gold on the history totals**

In `src/WhoCarried/Core/RecapBuilder.cs`, replace `DefenseTotals` and its doc comment:

```csharp
/// <summary>
/// Everything the recap takes from the game's own per-floor history, per player (named for the Defense tab it first
/// served).
/// </summary>
/// <param name="LowestHp">The lowest HP the player ended a floor on (0 = unknown), with their max HP then.</param>
/// <param name="Gold">Gold earned over the run: every gain the game recorded, not starting gold; nothing taken off for spending.</param>
public sealed record DefenseTotals(int Taken, int Healed, int LowestHp = 0, int LowestHpMax = 0, int Gold = 0);
```

In `Build`, pass the totals to the deck builder: `DeckBuilder.Build(stats, byDamage, decks)` becomes `DeckBuilder.Build(stats, byDamage, decks, defense)`.

- [ ] **Step 4: Put gold on each deck**

In `src/WhoCarried/Core/DeckBuilder.cs`, replace the `DeckView` record:

```csharp
/// <param name="Gold">Gold the player earned over the run (see <see cref="DefenseTotals.Gold"/>); 0 = none or unknown.</param>
public sealed record DeckView(ulong PlayerId, string PlayerLabel, string ColorHex, string? IconKey, int CardCount,
                              IReadOnlyList<DeckEntry> Entries, int Gold = 0);
```

Change `Build`'s signature and its `return new DeckView(...)`:

```csharp
    /// <param name="history">The game's per-floor totals, for each player's gold; null when there are none.</param>
    public static IReadOnlyList<DeckView> Build(RunStats stats, IEnumerable<PlayerInfo> players,
                                                IReadOnlyDictionary<ulong, IReadOnlyList<DeckCard>>? decks,
                                                IReadOnlyDictionary<ulong, DefenseTotals>? history = null)
```

```csharp
            return new DeckView(p.NetId, $"{p.Name} · {p.Character}", p.ColorHex,
                string.IsNullOrEmpty(p.CharacterId) ? null : p.CharacterId, cards.Count, entries,
                history?.GetValueOrDefault(p.NetId)?.Gold ?? 0);
```

- [ ] **Step 5: Read gold from a saved run**

In `src/WhoCarried/Core/RunHistory.cs`:

1. Add `int Gold = 0` as `PlayerRecord`'s last parameter, and to its doc comment `/// <param name="Gold">Gold earned: the sum of every floor's gold_gained.</param>`. Add "gold earned" to the list in the class summary.
2. In `Parse`, next to `var healed = ...`: `var gold = new Dictionary<ulong, int>();`
3. Inside the `player_stats` loop, after the `taken[id]` line: `gold[id] = gold.GetValueOrDefault(id) + Int(p, "gold_gained");`
4. In `players.Add(new PlayerRecord(...))`, after the badges argument: `, gold.GetValueOrDefault(id)`.

- [ ] **Step 6: Run the tests**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- GoldTests`
Expected: `3/3 passed`.

- [ ] **Step 7: Read gold from the live run and replays**

In `src/WhoCarried/Game/GameReader.cs`, `Defense(IRunState run)`: update the summary to "Damage taken, HP healed, the lowest end-of-floor HP and gold earned per player, from the game's own per-floor history". Then add the gold tally:

```csharp
        var gold = new Dictionary<ulong, int>();
```

Inside the `foreach (var stats in point.PlayerStats)` loop, after the `taken[...]` line:

```csharp
                gold[stats.PlayerId] = gold.GetValueOrDefault(stats.PlayerId) + stats.GoldGained;
```

And the return:

```csharp
        return taken.Keys.Union(healed.Keys).Union(gold.Keys)
            .ToDictionary(id => id, id => new DefenseTotals(taken.GetValueOrDefault(id), healed.GetValueOrDefault(id),
                lows.Get(id).Hp, lows.Get(id).Max, gold.GetValueOrDefault(id)));
```

In `src/WhoCarried/UI/Replay.cs`, the `defense` dictionary:

```csharp
            Dictionary<ulong, DefenseTotals> defense = records.Values.ToDictionary(r => r.Id,
                r => new DefenseTotals(r.Taken, r.Healed, r.LowestHp, r.LowestHpMax, r.Gold));
```

- [ ] **Step 8: Build the mod and run the whole suite**

Run: `"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release --nologo -v q`
Expected: `Build succeeded`, 0 errors.

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: every test passes.

- [ ] **Step 9: Commit**

```bash
git add src/WhoCarried/Core/RecapBuilder.cs src/WhoCarried/Core/RunHistory.cs src/WhoCarried/Core/DeckBuilder.cs src/WhoCarried/Game/GameReader.cs src/WhoCarried/UI/Replay.cs tests/WhoCarried.Tests/GoldTests.cs
git commit -m "feat: total each player's gold earned from the run history"
```

---

### Task 3: Count deaths in the game

**Files:**
- Modify: `src/WhoCarried/Game/Tracker.cs`, `src/WhoCarried/Game/Patches.cs`, `src/WhoCarried/ModEntry.cs`

**Interfaces:**
- Consumes: `RunStats.RecordDeath(ulong, int = 1)` and `LogReplay.Died` from Task 1.
- Produces: `Tracker.OnPlayerDied(Player player)`; `AfterDeathPatch`.

There are no Core tests for this task: it's game glue, like the rest of `Game/`. The build is the check here. Task 5 checks it in the game. Real deaths, a wipe and a win are checked in real runs (see "Still open" at the end).

What the game does, from its decompiled v0.111 code:
- Every death goes through `CreatureCmd.Kill`, which raises `Hook.AfterDeath`. When the last player dies, `Kill` then ends the run as a defeat, so the wipe's deaths arrive while the run is still going.
- `RunManager.WinRun` (the Architect's last option) calls `OnEnded(isVictory: true)`, which the tracker's `RunEndedPatch` turns into `RunStats.Finished = true`, and only then force-kills every player with `GuaranteeKillAllPlayers`.
- Abandoning sets `RunManager.IsAbandoned = true` and then calls `GuaranteeKillAllPlayers`, before the run's end is reported.

- [ ] **Step 1: Count a death, or skip it once the run is over**

In `src/WhoCarried/Game/Tracker.cs`, add after `NoteHp`:

```csharp
    /// <summary>
    /// A player died: a real death, not one something like Fairy in a Bottle stopped. Counted straight away, the wipe
    /// that loses a run included. To end a run that's over, the game kills every player: after the Architect win,
    /// which has already reported the run's end, and when abandoning, which is marked first. Those aren't deaths.
    /// </summary>
    public static void OnPlayerDied(Player player)
    {
        if (_stats.Finished || RunManager.Instance.IsAbandoned)
        {
            _log?.Write($"{Where} {NameOf(player.NetId)} killed as the run ends, not counted");
            return;
        }
        _stats.RecordDeath(player.NetId);
        _log?.Write($"{Where} {NameOf(player.NetId)} {LogReplay.Died}");
        Touch();
    }
```

`RunManager` comes from `MegaCrit.Sts2.Core.Runs`, which `Tracker.cs` already imports (`RecordBadges` reads `IsAbandoned` too). The stats are saved at the end of each fight and when the run ends, as they already are for damage.

- [ ] **Step 2: The patch**

In `src/WhoCarried/Game/Patches.cs`, after `DoomKillPatch`:

```csharp
/// <summary>
/// Every creature's death once the game has decided on it. <c>wasRemovalPrevented</c> is true when something stopped
/// the death (Fairy in a Bottle); pets and enemies aren't players. What's left is a player dying.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDeath))]
internal static class AfterDeathPatch
{
    private static void Prefix(Creature creature, bool wasRemovalPrevented)
    {
        try
        {
            if (!wasRemovalPrevented && creature.IsPlayer && creature.Player is Player player) Tracker.OnPlayerDied(player);
        }
        catch (Exception e) { Tracker.LogError("AfterDeath", e); }
    }
}
```

In `src/WhoCarried/ModEntry.cs`, add `typeof(AfterDeathPatch),` to `PatchClasses` after `typeof(DoomKillPatch),`.

- [ ] **Step 3: Build**

Run: `"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release --nologo -v q`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/WhoCarried/Game/Tracker.cs src/WhoCarried/Game/Patches.cs src/WhoCarried/ModEntry.cs
git commit -m "feat: count player deaths, but not the kills that end a finished run"
```

---

### Task 4: Show them

**Files:**
- Modify: `src/WhoCarried/UI/GameArt.cs`, `src/WhoCarried/UI/ScoreboardTab.cs`, `src/WhoCarried/UI/DecksTab.cs`, `src/WhoCarried/UI/DevPreview.cs`, `src/WhoCarried/Localization/eng.json`, `src/WhoCarried/Localization/zhs.json`

**Interfaces:**
- Consumes: `BarRow.Deaths` (Task 1), `DeckView.Gold` (Task 2), `RunStats.RecordDeath` (Task 1), `DefenseTotals.Gold` (Task 2).
- Produces: `GameArt.Gold`; keys `WHO_CARRIED.stat.deaths`, `WHO_CARRIED.stat.deaths_one`, `WHO_CARRIED.decks.gold`.

- [ ] **Step 1: The strings**

In `src/WhoCarried/Localization/eng.json`, after `"WHO_CARRIED.decks.distinct"`:

```json
  "WHO_CARRIED.decks.gold": "{0} gold earned",
```

After `"WHO_CARRIED.stat.damage_amount"`:

```json
  "WHO_CARRIED.stat.deaths": "Died {0} times",
  "WHO_CARRIED.stat.deaths_one": "Died once",
```

In `src/WhoCarried/Localization/zhs.json`, at the same places:

```json
  "WHO_CARRIED.decks.gold": "获得 {0} 金币",
```

```json
  "WHO_CARRIED.stat.deaths": "阵亡 {0} 次",
  "WHO_CARRIED.stat.deaths_one": "阵亡 1 次",
```

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- Localization`
Expected: all `LocalizationTests` pass (matching keys and placeholders).

- [ ] **Step 2: The gold icon**

In `src/WhoCarried/UI/GameArt.cs`, add `Gold = "gold"` to the constants (after `DrawPile = "draw_pile"`, so that line ends `DrawPile = "draw_pile", Gold = "gold";`). In `Paths`, after the `DrawPile` entry:

```csharp
        [Gold] = "res://images/packed/sprite_fonts/gold_icon.png",
```

This is the coin the game puts inline in card text. `GameArt.Get` returns null if it's missing, and the text still shows without it.

- [ ] **Step 3: The deaths line on the card**

In `src/WhoCarried/UI/ScoreboardTab.cs`, `PlayerCard`:

1. The fields become:

```csharp
        private readonly Label _share, _block, _bonusValue, _bonusText, _deathsText;
        private readonly Control _shareChip, _bonusLine, _deathsLine;
```

2. In the constructor, the chips' gap `body.AddChild(Pad(chips, em * 0.7f));` becomes `body.AddChild(Pad(chips, em * 0.5f));`, and `_bonusLine = Pad(bonus, em * 0.45f);` becomes `_bonusLine = Pad(bonus, em * 0.3f);`.

3. After `body.AddChild(_bonusLine);`:

```csharp
            // Co-op deaths the team came back from, under the bonus damage; hidden for a player who never went down.
            HBoxContainer deaths = k.Row(em * 0.3f);
            deaths.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            deaths.AddChild(Kit.Center(k.Pic(GameArt.Get(GameArt.Skull), em * 1.2f, em * 1.2f)));
            _deathsText = k.Text("", em * 0.92f, new Color("e8e2d4"));
            deaths.AddChild(Kit.Center(_deathsText));
            _deathsLine = Pad(deaths, em * 0.3f);
            body.AddChild(_deathsLine);
```

4. In `Update`, before `Face.SetLeader(rank == 0);`:

```csharp
            _deathsText.Text = row.Deaths == 1
                ? Loc.Text("WHO_CARRIED.stat.deaths_one")
                : Loc.Text("WHO_CARRIED.stat.deaths", Kit.Num(row.Deaths));
            _deathsLine.Visible = row.Deaths > 0;
```

- [ ] **Step 4: Gold in the Decks header and the export's columns**

In `src/WhoCarried/UI/DecksTab.cs`, `Counts`: between the type-chip loop and the `counts.AddChild(k.Gap(6, 0));` that comes before the "different" note, add:

```csharp
            if (deck.Gold > 0)
            {
                counts.AddChild(k.Gap(6, 0));
                HBoxContainer gold = k.Row(5);
                gold.AddChild(Kit.Center(k.Pic(GameArt.Get(GameArt.Gold), 22, 22)));
                gold.AddChild(Kit.Center(k.Text(Loc.Text("WHO_CARRIED.decks.gold", Kit.Num(deck.Gold)), 18, RecapTheme.Gold, true, Ink.Soft)));
                counts.AddChild(Kit.Center(gold));
            }
```

In `List` (the saved image's column), after `column.AddChild(k.Swatch(accent, width, 2, 0));`:

```csharp
        if (deck.Gold > 0)
        {
            HBoxContainer gold = k.Row(4);
            gold.AddChild(Kit.Center(k.Pic(GameArt.Get(GameArt.Gold), 15, 15)));
            gold.AddChild(Kit.Center(k.Text(Loc.Text("WHO_CARRIED.decks.gold", Kit.Num(deck.Gold)), 14, RecapTheme.Gold)));
            column.AddChild(gold);
        }
```

- [ ] **Step 5: Sample deaths and gold for the preview**

In `src/WhoCarried/UI/DevPreview.cs`, `BuildSample`, after `stats.RecordHp(P(2).NetId, 6, 72);`:

```csharp
        // Co-op deaths: two for one player, one for another, none for the rest (so both card layouts show).
        if (players.Count > 1) stats.RecordDeath(P(1).NetId, 2);
        if (players.Count > 3) stats.RecordDeath(P(3).NetId);
```

Replace the `defense` dictionary. Gold gets its own random stream, so the other sample numbers don't change:

```csharp
        var purse = new Random(13);
        Dictionary<ulong, DefenseTotals> defense = players.ToDictionary(
            p => p.NetId, _ => new DefenseTotals(rng.Next(150, 400), rng.Next(60, 200), Gold: purse.Next(450, 1100)));
```

- [ ] **Step 6: Build and run the whole suite**

Run: `"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release --nologo -v q`
Expected: `Build succeeded`, 0 errors.

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: every test passes.

- [ ] **Step 7: Commit**

```bash
git add src/WhoCarried/UI/GameArt.cs src/WhoCarried/UI/ScoreboardTab.cs src/WhoCarried/UI/DecksTab.cs src/WhoCarried/UI/DevPreview.cs src/WhoCarried/Localization/eng.json src/WhoCarried/Localization/zhs.json
git commit -m "feat: show deaths on the card and gold earned in the Decks tab"
```

---

### Task 5: Check it in the game, against the "before" screenshots

The "before" screenshots from the current build are in the session scratchpad's `before/` folder: the 4-player English preview, taken 2026-09-26.

**Each preview run:** the game must be closed (`tools/deploy.ps1` refuses otherwise).
1. Write the flag: `%APPDATA%\SlayTheSpire2\WhoCarried\preview.flag`, ASCII, containing the case below.
2. Touch a marker file in the scratchpad.
3. Launch: `E:\Steam\steam.exe -applaunch 2868840`.
4. Wait until `preview-15-podium-focus.png` in the data folder is newer than the marker (about 35 s).
5. Close the game: `taskkill //IM SlayTheSpire2.exe //F`. The preview doesn't quit it.
6. Copy the data folder's `preview-*.png` into `after-<case>/` in the scratchpad.
7. Delete `preview.flag` when all cases are done.

- [ ] **Step 1: Deploy**

Run: `powershell -File tools/deploy.ps1`
Expected: `Build succeeded` and `Deployed to E:\Games\steamapps\common\Slay the Spire 2\mods\WhoCarried`. The game loads this local copy; the Workshop copy is disabled in the game's mod settings.

- [ ] **Step 2: 4 players, English (flag `4`)**

After the run, check that `godot.log` in `%APPDATA%\SlayTheSpire2\logs` reads `[WhoCarried] loaded v1.2.0: 21/21 patches applied`.

Look at `preview-1-scoreboard.png` beside `before/preview-1-scoreboard.png`:
- Mika (2 deaths) shows the skull and "Died 2 times" under "+… bonus damage"; Jo (1 death) shows "Died once". Sam and Ash show no deaths line.
- The damage numbers are the same size as before. No line touches the text box's edge or the badges below it.

`preview-8-decks.png`: the coin and "… gold earned" sit after the type chips, before "… different". `preview-10-export.png`: the same card lines, and a "… gold earned" line under each deck column's name.

- [ ] **Step 3: 5 players, Chinese (flag `IRONCLAD,SILENT,REGENT,NECROBINDER,DEFECT zhs`)**

The narrowest cards, in the language with the widest text. In `preview-1-scoreboard.png`, "阵亡 2 次" and "阵亡 1 次" fit on one line without touching the text box's edge. In `preview-8-decks.png` and `preview-10-export.png`, "获得 … 金币" shows.

If the line crowds the box, tighten the gaps further (chips to 0.4 em, bonus and deaths lines to 0.25 em) and rerun Steps 2 and 3. Don't shrink fonts.

- [ ] **Step 4: 1 player, back in English (flag `1 eng`)**

The preview's language option changes the game's language, so this run also puts it back to English. `preview-1-scoreboard.png` shows no deaths line: the solo card matches `before/`, apart from the slightly tighter gaps. Confirm the game's menu is in English afterwards, and tell the owner if it isn't.

- [ ] **Step 5: Replay the last real run**

Put `replay.flag` in the data folder, launch, wait for `replay-8-export.png` to be newer than a marker, close the game, and delete the flag. `events.log` must show `replay: run …` with no `ERROR`. A real run from before this change has no deaths, so no card shows a deaths line. Gold comes from the game's saved history, so the Decks tab shows each player's gold.

- [ ] **Step 6: Report**

Send the owner the before and after scoreboard, the Decks tab, and the 5-player Chinese scoreboard. This task changes no files; if Step 3 needed tighter gaps, commit that change:

```bash
git add src/WhoCarried/UI/ScoreboardTab.cs
git commit -m "fix: tighten the card's line gaps to fit the deaths line"
```

---

### Task 6: Document it

**Files:**
- Modify: `README.md`, `CHANGELOG.md`, `docs/README.md`

- [ ] **Step 1: README**

In `README.md`, under **What it shows**:
- The **Scoreboard** bullet: after "the bonus damage their Vulnerable set up for teammates.", add "A skull line counts how many times each player died, the wipe that loses a run included."
- The **Decks** bullet becomes: "**Decks:** everyone's final deck, with the damage each card dealt, and the gold each player earned over the run."

- [ ] **Step 2: Changelog**

In `CHANGELOG.md`, under `## [Unreleased]`, add an `### Added` section above `### Changed`:

```markdown
### Added

- Deaths on each player's scoreboard card: a skull line counts how many times they died, the wipe that loses a run included. Winning or abandoning a run doesn't add one, and deaths from before this update can't be recovered.
- Gold earned per player in the Decks tab and the exported image, from the game's own run history, so it's there for co-op guests and reloaded runs too.
```

- [ ] **Step 3: Docs index**

In `docs/README.md`, add a row at the end of the spec table:

```markdown
| [Deaths and gold](design/specs/2026-09-26-deaths-and-gold-design.md) | [plan](design/plans/2026-09-26-deaths-and-gold.md) | A skull line on the card for co-op deaths, and each player's gold earned in the Decks tab |
```

- [ ] **Step 4: Commit**

```bash
git add README.md CHANGELOG.md docs/README.md docs/design/specs/2026-09-26-deaths-and-gold-design.md docs/design/plans/2026-09-26-deaths-and-gold.md
git commit -m "docs: deaths on the card and gold earned in the Decks tab"
```

## Still open after this plan

**Real runs (need the owner).** The preview only shows sample numbers; these check the counting itself:
- **A won run** (solo is enough): after the Architect, `events.log` shows a `killed as the run ends, not counted` line for each player, and no card gains a death.
- **A lost run** (solo is enough): the fatal death logs `died` and the card reads "Died once".
- **Co-op:** a teammate who dies and is revived shows "Died once" on their card straight away, and a wipe adds one death to every card.
