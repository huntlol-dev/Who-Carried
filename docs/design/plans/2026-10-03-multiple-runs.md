# Keeping every run — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every run gets its own folder under `runs\`, so a run picked up again carries on with all its counts however many runs were played in between.

**Architecture:** `Core/RunFolders` owns `runs\`: naming, creating, finding the unfinished run to continue, pruning to 100, and picking a run for replay. `Core/OldRunFiles` moves today's `current_run.dat`/`events.log` pairs into it once. `Core/RunInfo` is the `run.json` record. `Game/Tracker` wires them in: `mod.log` until a run starts, then that run's folder. `UI/Replay` picks its run from `replay.flag`. Everything with rules lives in Core and is unit-tested; the game-side parts are thin.

**Tech Stack:** C# / .NET 9, System.Text.Json source generation (`WhoCarriedJson`), the game's sts2.dll API, and the repo's console test runner.

**Spec:** [Keeping every run](../specs/2026-10-03-multiple-runs-design.md). Read it first: it has the reasons, the edge-case table and the manual test cases (M1–M14).

## Global constraints

- Work on `feat/multiple-runs`, which already exists (its commits so far are the spec). Commit after each task as its steps say. Don't push.
- Build and test with `C:\Program Files\dotnet\dotnet.exe`; the x86 dotnet first on PATH can't see the x64 .NET 9 SDK. In bash:
  - build: `"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release`
  - all tests: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
  - some tests: add `-- <part of the test name>`, e.g. `-- RunFolders`
- `src/WhoCarried/Core` stays free of Godot and game types: the test project compiles it directly.
- **No new on-screen strings.** Nothing goes into `eng.json` or `zhs.json`.
- Nothing throws into the game. Every file operation in `Tracker` and `Replay` is caught, and a failure only ever costs the file, never the counting or the recap.
- **Nothing the owner has is ever overwritten or deleted except by `Prune`, and `Prune` only touches folders whose names look like run folders.** The owner plays on this machine; their real run files are in `%APPDATA%\SlayTheSpire2\WhoCarried\`. Unit tests use temp folders only. Don't deploy or launch the game before Task 8, which has its own rules.
- The names are fixed: folder `runs`, files `stats.dat`, `events.log`, `run.json`, `mod.log`, `mod.previous.log`; keep **100** runs; folder names `yyyy-MM-dd_HHmm_SEED` with `-2`, `-3`… on a clash.
- `run.json` is serialized by `WhoCarriedJson` with its default naming (PascalCase), like `stats.dat`. The spec's field names (`format`, `runKey`…) are written `Format`, `RunKey`….
- Commit messages end with a `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` paragraph, as the commit steps show.

## Review focus

1. **A stray folder or file in `runs\`** (the player made a `backup` folder, or dropped a zip there). `Prune`, `Newest` and `FindResumable` must ignore anything not named like a run folder, and must never delete it. Task 1 tests `PruneLeavesFoldersThatArentRuns` and `NewestIgnoresFoldersThatArentRuns`.
2. **A `replay.flag` naming a path instead of a folder** (`..\..\something`, `C:\x`). `Named` must refuse anything that isn't a plain run-folder name, so replay never reads outside `runs\`. Task 1 tests `NamedRefusesPathsAndUnknownNames`.
3. **A partial migration.** The first move worked, the second failed (a file locked by an editor). The next launch must pick up what's left without touching what already moved. Task 4 tests `MoveRetriesWhatIsLeft`.
4. **A guest's old log and stats with different start times.** They must land in one folder. Task 4 tests `MovePairsAGuestsLogAndStats` with the real keys from the owner's run of the 29th.
5. **Two unfinished runs with the same seed** (a custom seed abandoned outside the game, then played again). Loading a save must continue the one played most recently, not an arbitrary one. Task 2 tests `LoadedSaveTakesTheMostRecentlyPlayedOfTheSameSeed`.

## Files

| File | Change |
|---|---|
| `src/WhoCarried/Core/RunFolders.cs` | New: the `runs\` folder (names, create, find resumable, prune, newest, named) |
| `src/WhoCarried/Core/OldRunFiles.cs` | New: the one-time move of today's files (the spec's `RunFolders.MoveOldFiles`, in its own file) |
| `src/WhoCarried/Core/RunInfo.cs` | New: the `run.json` record, its save/load, and the `mods:` log line |
| `src/WhoCarried/Core/WhoCarriedJson.cs` | Registers `RunInfo` |
| `src/WhoCarried/Core/LogReplay.cs` | `RunKeyOf`: a log's run key without parsing the whole run |
| `src/WhoCarried/Core/RunStatsStore.cs` | `LoadIfResumable` and `Seed` go (moved into `RunFolders`) |
| `src/WhoCarried/Core/EventLog.cs` | Doc comments only |
| `src/WhoCarried/Game/GameReader.cs` | `ReadRunInfo(…)`: reads build, profile, language, local player, loaded mods, players |
| `src/WhoCarried/Game/Tracker.cs` | `mod.log`, the migration, a folder per run, `run.json`, pruning |
| `src/WhoCarried/UI/Replay.cs` | Picks the run from `replay.flag` |
| `tests/WhoCarried.Tests/RunFoldersTests.cs` | New |
| `tests/WhoCarried.Tests/OldRunFilesTests.cs` | New |
| `tests/WhoCarried.Tests/RunInfoTests.cs` | New |
| `tests/WhoCarried.Tests/RunStatsStoreTests.cs` | Resume tests move to `RunFoldersTests` |
| `tests/WhoCarried.Tests/EventLogTests.cs` | One test for keeping the previous log |
| `README.md`, `docs/README.md`, `docs/localization.md`, `CHANGELOG.md` | Docs |
| `docs/design/specs/2026-10-03-multiple-runs-design.md` | Two migration details settled while planning (Task 4) |

---

### Task 1: RunFolders: names, create, newest, named, prune

**Files:**
- Create: `src/WhoCarried/Core/RunFolders.cs`
- Test: `tests/WhoCarried.Tests/RunFoldersTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces (used by Tasks 2, 4, 5, 6):
  - `public sealed class RunFolders(string root)` with `string Root`
  - `public const string StatsFile = "stats.dat", LogFile = "events.log", InfoFile = "run.json"; public const int Keep = 100;`
  - `public static string Seed(string runKey)`: the part before the last `:` (the whole key if there's none)
  - `public static long StartTime(string runKey)`: the Unix seconds after the last `:`, or 0
  - `public static string Name(string runKey, DateTime fallbackLocal)`: `yyyy-MM-dd_HHmm_SEED`, the time from the key's start time, or `fallbackLocal` when it has none
  - `public static bool IsRunFolderName(string name)`
  - `public static DateTime LastPlayed(string folder)`: UTC; the newest last-write time of its files, or the folder's own when it's empty
  - `public string Create(string runKey, DateTime now)`: makes the folder (with `-2`… on a clash), returns its full path; can throw
  - `public string? Newest()`, `public string? Named(string name)`: never throw
  - `public int Prune(int keep, string? active, Action<string>? onError = null)`: returns how many folders were deleted; never throws

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/RunFoldersTests.cs`:

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class RunFoldersTests
{
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "runs");

    private static string Local(long unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime.ToString("yyyy-MM-dd_HHmm");

    /// <summary>A run folder with one file, last written at <paramref name="playedUtc"/>.</summary>
    private static string MakeRun(string root, string name, DateTime playedUtc)
    {
        string folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, RunFolders.LogFile);
        File.WriteAllText(file, "log");
        File.SetLastWriteTimeUtc(file, playedUtc);
        return folder;
    }

    [Test]
    public static void SeedAndStartTimeComeFromTheKey()
    {
        Check.Equal("CEKDYSK8A9AB", RunFolders.Seed("CEKDYSK8A9AB:1790705897"), "seed");
        Check.Equal(1790705897L, RunFolders.StartTime("CEKDYSK8A9AB:1790705897"), "start");
        Check.Equal("SEED", RunFolders.Seed("SEED"), "key without a start time");
        Check.Equal(0L, RunFolders.StartTime("SEED"), "no start time");
        Check.Equal(0L, RunFolders.StartTime("SEED:notanumber"), "bad start time");
    }

    [Test]
    public static void NameIsTheStartTimeThenTheSeed()
    {
        Check.Equal(Local(1790705897) + "_CEKDYSK8A9AB", RunFolders.Name("CEKDYSK8A9AB:1790705897", DateTime.Now), "from the key");
        var fallback = new DateTime(2026, 10, 3, 19, 5, 0, DateTimeKind.Local);
        Check.Equal("2026-10-03_1905_SEED", RunFolders.Name("SEED:0", fallback), "no start time: the fallback");
        Check.Equal("2026-10-03_1905_A_B", RunFolders.Name("A/B:0", fallback), "unsafe characters become _");
        Check.Equal("2026-10-03_1905_NOSEED", RunFolders.Name(":0", fallback), "empty seed");
    }

    [Test]
    public static void RecognisesRunFolderNames()
    {
        Check.True(RunFolders.IsRunFolderName("2026-09-29_1918_CEKDYSK8A9AB"), "plain");
        Check.True(RunFolders.IsRunFolderName("2026-09-29_1918_CEKDYSK8A9AB-2"), "with a clash suffix");
        Check.True(!RunFolders.IsRunFolderName("backup"), "a player's own folder");
        Check.True(!RunFolders.IsRunFolderName("2026-09-29_1918_"), "no seed");
        Check.True(!RunFolders.IsRunFolderName("..\\2026-09-29_1918_X"), "a path");
    }

    [Test]
    public static void CreateMakesTheFolderAndAddsANumberOnAClash()
    {
        var runs = new RunFolders(NewRoot());
        string first = runs.Create("SEED:1790705897", DateTime.Now);
        string second = runs.Create("SEED:1790705897", DateTime.Now);
        string third = runs.Create("SEED:1790705897", DateTime.Now);
        Check.True(Directory.Exists(first), "first made");
        Check.Equal(Local(1790705897) + "_SEED", Path.GetFileName(first), "first name");
        Check.Equal(Local(1790705897) + "_SEED-2", Path.GetFileName(second), "second name");
        Check.Equal(Local(1790705897) + "_SEED-3", Path.GetFileName(third), "third name");
    }

    [Test]
    public static void LastPlayedIsTheNewestFileInTheFolder()
    {
        string root = NewRoot();
        string folder = MakeRun(root, "2026-09-29_1918_A", new DateTime(2026, 9, 29, 18, 0, 0, DateTimeKind.Utc));
        string stats = Path.Combine(folder, RunFolders.StatsFile);
        File.WriteAllText(stats, "{}");
        File.SetLastWriteTimeUtc(stats, new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc));
        Check.Equal(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc), RunFolders.LastPlayed(folder), "newest file");
    }

    [Test]
    public static void NewestIsTheRunPlayedMostRecently()
    {
        string root = NewRoot();
        MakeRun(root, "2026-09-27_2140_OLD", new DateTime(2026, 9, 27, 21, 0, 0, DateTimeKind.Utc));
        // Started earlier, but played last (picked up again after the other).
        string resumed = MakeRun(root, "2026-09-20_1000_RESUMED", new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc));
        Check.Equal(resumed, new RunFolders(root).Newest(), "newest");
    }

    [Test]
    public static void NewestIgnoresFoldersThatArentRuns()
    {
        string root = NewRoot();
        string run = MakeRun(root, "2026-09-27_2140_A", new DateTime(2026, 9, 27, 21, 0, 0, DateTimeKind.Utc));
        MakeRun(root, "backup", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        Check.Equal(run, new RunFolders(root).Newest(), "the run, not the newer backup folder");
    }

    [Test]
    public static void NewestOfNothingIsNull()
    {
        Check.Equal<string?>(null, new RunFolders(NewRoot()).Newest(), "no runs folder at all");
    }

    [Test]
    public static void NamedFindsARunByItsFolderName()
    {
        string root = NewRoot();
        string run = MakeRun(root, "2026-09-27_2140_TB20BJ6WUGJ2", DateTime.UtcNow);
        Check.Equal(run, new RunFolders(root).Named("2026-09-27_2140_TB20BJ6WUGJ2"), "by name");
        Check.Equal(run, new RunFolders(root).Named("  2026-09-27_2140_TB20BJ6WUGJ2\r\n"), "whitespace around it is fine");
    }

    [Test]
    public static void NamedRefusesPathsAndUnknownNames()
    {
        string root = NewRoot();
        MakeRun(root, "2026-09-27_2140_A", DateTime.UtcNow);
        var runs = new RunFolders(root);
        Check.Equal<string?>(null, runs.Named("2026-09-27_2140_MISSING"), "not there");
        Check.Equal<string?>(null, runs.Named("..\\2026-09-27_2140_A"), "relative path");
        Check.Equal<string?>(null, runs.Named("../2026-09-27_2140_A"), "relative path, forward slash");
        Check.Equal<string?>(null, runs.Named(Path.Combine(root, "2026-09-27_2140_A")), "absolute path");
        Check.Equal<string?>(null, runs.Named(""), "empty");
    }

    [Test]
    public static void PruneDeletesTheLeastRecentlyPlayed()
    {
        string root = NewRoot();
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 5; i++) MakeRun(root, $"2026-09-0{i + 1}_1000_S{i}", start.AddDays(i));
        int deleted = new RunFolders(root).Prune(3, active: null);
        Check.Equal(2, deleted, "deleted");
        Check.True(!Directory.Exists(Path.Combine(root, "2026-09-01_1000_S0")), "oldest gone");
        Check.True(!Directory.Exists(Path.Combine(root, "2026-09-02_1000_S1")), "second oldest gone");
        Check.True(Directory.Exists(Path.Combine(root, "2026-09-03_1000_S2")), "third kept");
        Check.True(Directory.Exists(Path.Combine(root, "2026-09-05_1000_S4")), "newest kept");
    }

    [Test]
    public static void PruneNeverDeletesTheActiveRun()
    {
        string root = NewRoot();
        string active = MakeRun(root, "2026-09-01_1000_ACTIVE", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        MakeRun(root, "2026-09-02_1000_B", new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc));
        MakeRun(root, "2026-09-03_1000_C", new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc));
        new RunFolders(root).Prune(2, active);
        Check.True(Directory.Exists(active), "active kept although it's the oldest");
        Check.True(Directory.Exists(Path.Combine(root, "2026-09-03_1000_C")), "newest kept");
        Check.True(!Directory.Exists(Path.Combine(root, "2026-09-02_1000_B")), "next oldest gone instead");
    }

    [Test]
    public static void PruneLeavesFoldersThatArentRuns()
    {
        string root = NewRoot();
        string mine = MakeRun(root, "backup", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        MakeRun(root, "2026-09-02_1000_B", new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc));
        new RunFolders(root).Prune(1, active: null);
        Check.True(Directory.Exists(mine), "the player's own folder is never deleted");
    }

    [Test]
    public static void PruneBelowTheLimitOrWithNoFolderDoesNothing()
    {
        string root = NewRoot();
        Check.Equal(0, new RunFolders(root).Prune(RunFolders.Keep, active: null), "no runs folder");
        MakeRun(root, "2026-09-02_1000_B", DateTime.UtcNow);
        Check.Equal(0, new RunFolders(root).Prune(RunFolders.Keep, active: null), "below the limit");
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- RunFolders`
Expected: the build fails with `The type or namespace name 'RunFolders' could not be found`.

- [ ] **Step 3: Write `RunFolders`**

Create `src/WhoCarried/Core/RunFolders.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace WhoCarried.Core;

/// <summary>
/// The runs\ folder: one folder per run, named yyyy-MM-dd_HHmm_SEED (with -2, -3… on a clash), holding the run's
/// stats.dat, events.log and run.json. A run's files never move once written. Folders not named like a run are never
/// read or deleted: they're the player's.
/// </summary>
public sealed class RunFolders(string root)
{
    public const string StatsFile = "stats.dat";
    public const string LogFile = "events.log";
    public const string InfoFile = "run.json";

    /// <summary>How many runs are kept; past that, the least recently played go.</summary>
    public const int Keep = 100;

    // "2026-09-29_1918_" is 16 characters; the seed follows, then an optional "-2" for a clash.
    private const int PrefixLength = 16;
    private static readonly Regex RunName = new(@"^\d{4}-\d{2}-\d{2}_\d{4}_[^\\/]+$", RegexOptions.Compiled);
    private static readonly Regex ClashSuffix = new(@"-\d+$", RegexOptions.Compiled);

    public string Root { get; } = root;

    /// <summary>"SEED:1790705897" → "SEED". A key with no start time is all seed.</summary>
    public static string Seed(string runKey)
    {
        int colon = runKey.LastIndexOf(':');
        return colon < 0 ? runKey : runKey[..colon];
    }

    /// <summary>"SEED:1790705897" → 1790705897 (Unix seconds); 0 when there's none.</summary>
    public static long StartTime(string runKey)
    {
        int colon = runKey.LastIndexOf(':');
        return colon >= 0 && long.TryParse(runKey[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out long t) ? t : 0;
    }

    /// <summary>The folder name for a run: its start time (local), or <paramref name="fallbackLocal"/>, then its seed.</summary>
    public static string Name(string runKey, DateTime fallbackLocal)
    {
        long start = StartTime(runKey);
        DateTime when = start > 0 ? DateTimeOffset.FromUnixTimeSeconds(start).LocalDateTime : fallbackLocal;
        return when.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture) + "_" + SafeSeed(Seed(runKey));
    }

    public static bool IsRunFolderName(string name) => RunName.IsMatch(name);

    /// <summary>When the run was last played: the newest last-write time (UTC) of its files.</summary>
    public static DateTime LastPlayed(string folder)
    {
        DateTime newest = Directory.GetLastWriteTimeUtc(folder);
        bool any = false;
        foreach (string file in Directory.EnumerateFiles(folder))
        {
            DateTime written = File.GetLastWriteTimeUtc(file);
            if (!any || written > newest) newest = written;
            any = true;
        }
        return newest;
    }

    /// <summary>Makes a new run's folder and returns its full path. Throws if it can't.</summary>
    public string Create(string runKey, DateTime now)
    {
        string name = Name(runKey, now);
        string path = Path.Combine(Root, name);
        for (int n = 2; Directory.Exists(path); n++) path = Path.Combine(Root, $"{name}-{n}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>The run played most recently, or null when there's none.</summary>
    public string? Newest() => RunFoldersOrEmpty().OrderByDescending(SafeLastPlayed).FirstOrDefault();

    /// <summary>The run folder called <paramref name="name"/>, or null if there's none or it isn't a plain run-folder name.</summary>
    public string? Named(string name)
    {
        name = name.Trim();
        if (!IsRunFolderName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        string path = Path.Combine(Root, name);
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>
    /// Deletes the least recently played runs until <paramref name="keep"/> are left. Never deletes
    /// <paramref name="active"/>, which counts towards the ones kept. A folder that can't be deleted is reported and skipped.
    /// </summary>
    /// <returns>How many folders were deleted.</returns>
    public int Prune(int keep, string? active, Action<string>? onError = null)
    {
        List<string> folders = RunFoldersOrEmpty().OrderByDescending(SafeLastPlayed).ToList();
        bool IsActive(string folder) =>
            active != null && string.Equals(Path.GetFullPath(folder), Path.GetFullPath(active), StringComparison.OrdinalIgnoreCase);
        // The active run takes one of the places first, wherever it sorts.
        int kept = folders.Any(IsActive) ? 1 : 0, deleted = 0;
        foreach (string folder in folders)
        {
            if (IsActive(folder)) continue;
            if (kept < keep)
            {
                kept++;
                continue;
            }
            try
            {
                Directory.Delete(folder, recursive: true);
                deleted++;
            }
            catch (Exception e)
            {
                onError?.Invoke($"{Path.GetFileName(folder)}: {e.Message}");
            }
        }
        return deleted;
    }

    /// <summary>The run folders for this seed: "…_SEED" and "…_SEED-2" and so on.</summary>
    internal IEnumerable<string> FoldersFor(string seed)
    {
        string safe = SafeSeed(seed);
        return RunFoldersOrEmpty().Where(folder =>
        {
            string rest = Path.GetFileName(folder)[PrefixLength..];
            return rest == safe || ClashSuffix.Replace(rest, "") == safe;
        });
    }

    private IEnumerable<string> RunFoldersOrEmpty()
    {
        try
        {
            return Directory.Exists(Root)
                ? Directory.GetDirectories(Root).Where(d => IsRunFolderName(Path.GetFileName(d))).ToList()
                : new List<string>();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    private static DateTime SafeLastPlayed(string folder)
    {
        try { return LastPlayed(folder); }
        catch (Exception) { return DateTime.MinValue; }
    }

    private static string SafeSeed(string seed)
    {
        if (seed.Length == 0) return "NOSEED";
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(seed.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- RunFolders`
Expected: every `RunFoldersTests` test passes. Then run all tests (no filter): everything passes.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/RunFolders.cs tests/WhoCarried.Tests/RunFoldersTests.cs
git commit -m "feat: runs folder: names, create, newest, named, prune

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: RunFolders.FindResumable, and the players' case

**Files:**
- Modify: `src/WhoCarried/Core/RunFolders.cs` (add `Resumable` and `FindResumable`)
- Modify: `src/WhoCarried/Core/RunStatsStore.cs` (remove `LoadIfResumable` and `Seed`)
- Modify: `tests/WhoCarried.Tests/RunStatsStoreTests.cs` (keep only the save/load tests)
- Test: `tests/WhoCarried.Tests/RunFoldersTests.cs`

**Interfaces:**
- Consumes: Task 1's `RunFolders`, `RunStatsStore.Save/Load`.
- Produces (used by Task 5):
  - `public sealed record Resumable(string Folder, RunStats Stats);` nested in `RunFolders`
  - `public Resumable? FindResumable(string runKey, bool loadedFromSave)`: never throws; the returned stats already carry `runKey`

`Tracker` calls `RunStatsStore.LoadIfResumable` today. Removing it breaks the mod's build (not the tests' build) until Task 5 rewires `Tracker`. That's expected: this task only runs the tests, and Task 5 builds the mod.

- [ ] **Step 1: Write the failing tests**

Add these to `RunFoldersTests` (inside the class, after the prune tests):

```csharp
    private static RunStats Stats(string key, int damage = 9, bool finished = false)
    {
        var s = new RunStats { RunKey = key, Finished = finished };
        s.BeginFight(1, 2, "Cultist");
        s.RecordDamage(1, new SourceRef(SourceKind.Power, "POISON_POWER", "Poison"), damage);
        s.EndFight();
        return s;
    }

    /// <summary>Saves <paramref name="stats"/> into a new folder for its key, last played at <paramref name="playedUtc"/>.</summary>
    private static string SaveRun(RunFolders runs, RunStats stats, DateTime playedUtc)
    {
        string folder = runs.Create(stats.RunKey, DateTime.Now);
        string path = Path.Combine(folder, RunFolders.StatsFile);
        RunStatsStore.Save(stats, path);
        File.SetLastWriteTimeUtc(path, playedUtc);
        return folder;
    }

    [Test]
    public static void ResumesTheExactRunKey()
    {
        var runs = new RunFolders(NewRoot());
        string folder = SaveRun(runs, Stats("SEED:1789237698"), DateTime.UtcNow);
        RunFolders.Resumable? found = runs.FindResumable("SEED:1789237698", loadedFromSave: false);
        Check.True(found != null, "found");
        Check.Equal(folder, found!.Folder, "folder");
        Check.Equal(9, found.Stats.Get(1)!.DamageDealt, "stats");
    }

    [Test]
    public static void OtherSeedsAndFinishedRunsAreNotResumed()
    {
        var runs = new RunFolders(NewRoot());
        SaveRun(runs, Stats("OTHER:1789237698"), DateTime.UtcNow);
        SaveRun(runs, Stats("SEED:1789237698", finished: true), DateTime.UtcNow);
        Check.True(runs.FindResumable("SEED:1789237698", loadedFromSave: true) == null, "nothing to resume");
    }

    // In co-op a guest's new run is stamped with the guest's clock, but a loaded save carries the host's, so the
    // same run comes back with a different start time.
    [Test]
    public static void LoadedSaveResumesTheSameSeedDespiteANewStartTime()
    {
        var runs = new RunFolders(NewRoot());
        string folder = SaveRun(runs, Stats("SEED:1789237698"), DateTime.UtcNow);
        RunFolders.Resumable? found = runs.FindResumable("SEED:1789237700", loadedFromSave: true);
        Check.True(found != null, "resumed");
        Check.Equal(folder, found!.Folder, "same folder");
        Check.Equal("SEED:1789237700", found.Stats.RunKey, "takes the game's current key");
    }

    [Test]
    public static void NewRunOnTheSameSeedStartsFresh()
    {
        var runs = new RunFolders(NewRoot());
        SaveRun(runs, Stats("SEED:1789237698"), DateTime.UtcNow);
        Check.True(runs.FindResumable("SEED:1789240000", loadedFromSave: false) == null, "a new run isn't loaded from a save");
    }

    [Test]
    public static void LoadedSaveTakesTheMostRecentlyPlayedOfTheSameSeed()
    {
        var runs = new RunFolders(NewRoot());
        SaveRun(runs, Stats("SEED:1000000000", damage: 1), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        string recent = SaveRun(runs, Stats("SEED:1100000000", damage: 2), new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));
        RunFolders.Resumable? found = runs.FindResumable("SEED:1200000000", loadedFromSave: true);
        Check.Equal(recent, found?.Folder, "the one played last");
    }

    [Test]
    public static void AnExactKeyBeatsAMoreRecentSeedMatch()
    {
        var runs = new RunFolders(NewRoot());
        string exact = SaveRun(runs, Stats("SEED:1000000000"), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        SaveRun(runs, Stats("SEED:1100000000"), new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));
        Check.Equal(exact, runs.FindResumable("SEED:1000000000", loadedFromSave: true)?.Folder, "exact key");
    }

    [Test]
    public static void UnreadableStatsAreSkipped()
    {
        var runs = new RunFolders(NewRoot());
        string folder = runs.Create("SEED:1789237698", DateTime.Now);
        File.WriteAllText(Path.Combine(folder, RunFolders.StatsFile), "{ not json");
        Check.True(runs.FindResumable("SEED:1789237698", loadedFromSave: true) == null, "corrupt stats");
        Check.True(new RunFolders(NewRoot()).FindResumable("SEED:1", loadedFromSave: true) == null, "no runs folder");
    }

    // The players' report: a co-op run, a singleplayer run started in between, then back to co-op.
    [Test]
    public static void ARunInBetweenLosesNothing()
    {
        var runs = new RunFolders(NewRoot());

        RunStats coop = Stats("COOP:1790700000", damage: 100);
        string coopFolder = SaveRun(runs, coop, new DateTime(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc));

        RunStats solo = Stats("SOLO:1790703600", damage: 7);
        string soloFolder = SaveRun(runs, solo, new DateTime(2026, 10, 3, 19, 0, 0, DateTimeKind.Utc));
        string soloBefore = File.ReadAllText(Path.Combine(soloFolder, RunFolders.StatsFile));

        // Back to co-op: the game loads its save.
        RunFolders.Resumable? back = runs.FindResumable("COOP:1790700000", loadedFromSave: true);
        Check.True(back != null, "co-op found again");
        Check.Equal(coopFolder, back!.Folder, "co-op's own folder");
        back.Stats.BeginFight(1, 3, "Jaw Worm");
        back.Stats.RecordDamage(1, new SourceRef(SourceKind.Power, "POISON_POWER", "Poison"), 50);
        back.Stats.EndFight();
        back.Stats.Finished = true;
        RunStatsStore.Save(back.Stats, Path.Combine(back.Folder, RunFolders.StatsFile));

        RunStats? coopAfter = RunStatsStore.Load(Path.Combine(coopFolder, RunFolders.StatsFile));
        Check.Equal(150, coopAfter!.Get(1)!.DamageDealt, "both halves of the co-op run");
        Check.Equal(2, coopAfter.Fights.Count, "both fights");
        Check.Equal(soloBefore, File.ReadAllText(Path.Combine(soloFolder, RunFolders.StatsFile)), "singleplayer untouched");
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- RunFolders`
Expected: the build fails with `'RunFolders' does not contain a definition for 'FindResumable'`.

- [ ] **Step 3: Add `FindResumable`**

In `RunFolders`, after `Prune`:

```csharp
    /// <summary>An unfinished run to carry on with: its folder, and its stats already given the run's current key.</summary>
    public sealed record Resumable(string Folder, RunStats Stats);

    /// <summary>
    /// The unfinished run the game is starting again, or null. Only folders named for this run's seed are opened. A
    /// folder whose stats have exactly <paramref name="runKey"/> wins; when the game is loading a saved run, the seed
    /// alone is enough: a co-op guest's new run is stamped with the guest's clock, but the save it loads later carries
    /// the host's. Of several, the one played most recently. Never throws.
    /// </summary>
    public Resumable? FindResumable(string runKey, bool loadedFromSave)
    {
        string seed = Seed(runKey);
        var candidates = new List<(string Folder, RunStats Stats, DateTime Played)>();
        foreach (string folder in FoldersFor(seed))
        {
            RunStats? stats = RunStatsStore.Load(Path.Combine(folder, StatsFile));
            if (stats == null || stats.Finished) continue;
            candidates.Add((folder, stats, SafeLastPlayed(folder)));
        }
        var ordered = candidates.OrderByDescending(c => c.Played).ToList();
        var pick = ordered.FirstOrDefault(c => c.Stats.RunKey == runKey);
        if (pick.Folder == null && loadedFromSave) pick = ordered.FirstOrDefault(c => Seed(c.Stats.RunKey) == seed);
        if (pick.Folder == null) return null;
        pick.Stats.RunKey = runKey;
        return new Resumable(pick.Folder, pick.Stats);
    }
```

- [ ] **Step 4: Remove the old resume rule**

In `src/WhoCarried/Core/RunStatsStore.cs`, delete `LoadIfResumable` (with its doc comment) and the private `Seed` method. `Save` and `Load` stay as they are.

In `tests/WhoCarried.Tests/RunStatsStoreTests.cs`:
- In `RoundTripKeepsEverything`, replace `RunStatsStore.LoadIfResumable(path, "SEED:123")` with `RunStatsStore.Load(path)`.
- Delete `DifferentRunIsNotResumed`, `FinishedRunIsNotResumed`, `LoadedSaveResumesTheSameSeedDespiteANewStartTime`, `NewRunOnTheSameSeedStartsFresh` and `LoadedSaveOfAnotherSeedOrAFinishedRunStartsFresh`. `RunFoldersTests` covers each of them now.
- Replace `CorruptOrMissingFileIsIgnored` with:

```csharp
    [Test]
    public static void CorruptOrMissingFileIsIgnored()
    {
        string path = TempFile();
        Check.True(RunStatsStore.Load(path) == null, "missing file");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Check.True(RunStatsStore.Load(path) == null, "corrupt file");
    }
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: all tests pass, including the new `RunFoldersTests` ones. (The mod itself won't build until Task 5; don't build it here.)

- [ ] **Step 6: Commit**

```bash
git add src/WhoCarried/Core/RunFolders.cs src/WhoCarried/Core/RunStatsStore.cs tests/WhoCarried.Tests/RunFoldersTests.cs tests/WhoCarried.Tests/RunStatsStoreTests.cs
git commit -m "feat: find the unfinished run to carry on in its own folder

Moves the resume rule from RunStatsStore into RunFolders. The mod builds
again once Tracker uses it.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: RunInfo (run.json)

**Files:**
- Create: `src/WhoCarried/Core/RunInfo.cs`
- Modify: `src/WhoCarried/Core/WhoCarriedJson.cs`
- Test: `tests/WhoCarried.Tests/RunInfoTests.cs`

**Interfaces:**
- Consumes: `WhoCarriedJson`.
- Produces (used by Task 5):
  - `public sealed class RunInfo` with properties `int Format`, `string RunKey`, `string WhoCarried`, `string GameBuild`, `string Language`, `string LocalPlayer`, `string Profile`, `List<RunInfo.ModVersion> Mods`, `List<RunInfo.PlayerEntry> Players`, `string Written`
  - `public sealed class RunInfo.ModVersion { string Id; string Version }`
  - `public sealed class RunInfo.PlayerEntry { string Id; string Name; string CharacterId; string CharacterName; string Colour }`
  - `public const int CurrentFormat = 1; public const string Unknown = "unknown";`
  - `public static void Save(RunInfo info, string path)` (can throw), `public static RunInfo? Load(string path)` (never throws)
  - `public static string ModsLine(IEnumerable<ModVersion> mods)`: `"mods: A 1.0, B unknown"` or `"mods: none"`

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/RunInfoTests.cs`:

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class RunInfoTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), RunFolders.InfoFile);

    [Test]
    public static void RoundTripKeepsEverything()
    {
        string path = TempFile();
        var info = new RunInfo
        {
            RunKey = "CEKDYSK8A9AB:1790705897",
            WhoCarried = "1.4.0",
            GameBuild = "v0.111.0",
            Language = "zhs",
            LocalPlayer = "76561198007503242",
            Profile = "3",
            Mods = { new RunInfo.ModVersion { Id = "IntoTheSpireverse", Version = "1.2.0" } },
            Players =
            {
                new RunInfo.PlayerEntry
                {
                    Id = "76561198291203680", Name = "CenturionVII", CharacterId = "NECROBINDER",
                    CharacterName = "The Necrobinder", Colour = "ee82ee",
                },
            },
            Written = "2026-10-03T19:00:00",
        };
        RunInfo.Save(info, path);
        RunInfo? loaded = RunInfo.Load(path);
        Check.True(loaded != null, "loaded");
        Check.Equal(RunInfo.CurrentFormat, loaded!.Format, "format");
        Check.Equal("CEKDYSK8A9AB:1790705897", loaded.RunKey, "run key");
        Check.Equal("v0.111.0", loaded.GameBuild, "build");
        Check.Equal("zhs", loaded.Language, "language");
        Check.Equal("3", loaded.Profile, "profile");
        Check.Equal("1.2.0", loaded.Mods[0].Version, "mod version");
        Check.Equal("CenturionVII", loaded.Players[0].Name, "player name");
        Check.Equal("ee82ee", loaded.Players[0].Colour, "colour");
    }

    [Test]
    public static void MissingFieldsReadAsUnknown()
    {
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"RunKey\": \"SEED:1\" }");
        RunInfo? loaded = RunInfo.Load(path);
        Check.Equal("SEED:1", loaded!.RunKey, "run key");
        Check.Equal(RunInfo.Unknown, loaded.GameBuild, "build");
        Check.Equal(RunInfo.Unknown, loaded.Profile, "profile");
        Check.Equal(0, loaded.Mods.Count, "mods");
    }

    [Test]
    public static void UnreadableOrMissingFileGivesNull()
    {
        string path = TempFile();
        Check.True(RunInfo.Load(path) == null, "missing");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Check.True(RunInfo.Load(path) == null, "corrupt");
    }

    [Test]
    public static void ModsLineListsEachModAndItsVersion()
    {
        Check.Equal("mods: BaseLib 0.3.1, ZoneTheSpire unknown",
            RunInfo.ModsLine(new[]
            {
                new RunInfo.ModVersion { Id = "BaseLib", Version = "0.3.1" },
                new RunInfo.ModVersion { Id = "ZoneTheSpire", Version = RunInfo.Unknown },
            }), "two mods");
        Check.Equal("mods: none", RunInfo.ModsLine(Array.Empty<RunInfo.ModVersion>()), "none");
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- RunInfo`
Expected: the build fails with `The type or namespace name 'RunInfo' could not be found`.

- [ ] **Step 3: Write `RunInfo` and register it**

Create `src/WhoCarried/Core/RunInfo.cs`:

```csharp
using System.Text.Json;

namespace WhoCarried.Core;

/// <summary>
/// What a run was played with, saved as run.json in its folder: so a later run history can show an old run after mods
/// were turned off, updated, or the game changed branch. Rewritten each time the run is picked up again, so it
/// describes the latest session. A field the game wouldn't give is "unknown".
/// </summary>
public sealed class RunInfo
{
    /// <summary>Raised whenever the run folder's files change shape, so a later reader knows what it's reading.</summary>
    public const int CurrentFormat = 1;

    public const string Unknown = "unknown";

    public int Format { get; set; } = CurrentFormat;
    public string RunKey { get; set; } = "";
    public string WhoCarried { get; set; } = Unknown;

    /// <summary>The game's build, e.g. "v0.111.0"; tells the public branch from the beta.</summary>
    public string GameBuild { get; set; } = Unknown;

    /// <summary>The game's language when this was written ("eng", "zhs"…); stats.dat's text is in it.</summary>
    public string Language { get; set; } = Unknown;

    /// <summary>The local player's Steam id.</summary>
    public string LocalPlayer { get; set; } = Unknown;

    /// <summary>The game profile, 1–3.</summary>
    public string Profile { get; set; } = Unknown;

    public List<ModVersion> Mods { get; set; } = new();
    public List<PlayerEntry> Players { get; set; } = new();

    /// <summary>When this was written: local time, ISO 8601.</summary>
    public string Written { get; set; } = "";

    public sealed class ModVersion
    {
        public string Id { get; set; } = Unknown;
        public string Version { get; set; } = Unknown;
    }

    public sealed class PlayerEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string CharacterId { get; set; } = "";
        public string CharacterName { get; set; } = "";

        /// <summary>The player's colour on the recap, as hex without the #.</summary>
        public string Colour { get; set; } = "";
    }

    public static void Save(RunInfo info, string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(info, WhoCarriedJson.Default.RunInfo));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>The saved info, or null if missing or unreadable.</summary>
    public static RunInfo? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), WhoCarriedJson.Default.RunInfo) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The log line listing the loaded mods, written at each start and resume: "mods: BaseLib 0.3.1, …".</summary>
    public static string ModsLine(IEnumerable<ModVersion> mods)
    {
        string list = string.Join(", ", mods.Select(m => $"{m.Id} {m.Version}"));
        return "mods: " + (list.Length == 0 ? "none" : list);
    }
}
```

In `src/WhoCarried/Core/WhoCarriedJson.cs`, add `[JsonSerializable(typeof(RunInfo))]` after `[JsonSerializable(typeof(RunStats))]`.

- [ ] **Step 4: Run the tests to see them pass**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- RunInfo`
Expected: the four `RunInfoTests` pass. Then run all tests: everything passes.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/RunInfo.cs src/WhoCarried/Core/WhoCarriedJson.cs tests/WhoCarried.Tests/RunInfoTests.cs
git commit -m "feat: run.json records what a run was played with

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Moving today's files (OldRunFiles)

**Files:**
- Modify: `src/WhoCarried/Core/LogReplay.cs` (add `RunKeyOf`)
- Create: `src/WhoCarried/Core/OldRunFiles.cs`
- Modify: `docs/design/specs/2026-10-03-multiple-runs-design.md` ("Moving today's files")
- Test: `tests/WhoCarried.Tests/OldRunFilesTests.cs`

**Interfaces:**
- Consumes: `RunFolders` (`Root`, `Name`, `Seed`, `StatsFile`, `LogFile`), `RunStatsStore.Load`, `LogReplay.HeaderLine`/`ResumedLine` (in tests).
- Produces (used by Task 5):
  - `public static string LogReplay.RunKeyOf(IEnumerable<string> lines)`: the last header or "resumed" line's key; `""` if none
  - `public static (int Moved, string? Error) OldRunFiles.Move(string dataDir, RunFolders runs)`: never throws

Two details are settled here and written back into the spec in Step 6:
1. **"Never overwrite" is per file, not per folder.** A move into an existing folder is fine; a move onto an existing file isn't (`File.Move(…, overwrite: false)`). That keeps the spec's rollback case safe (the run's `stats.dat` is already there, so the root file stays) and lets a partial migration finish next launch, which a per-folder rule would block forever.
2. **The start-up check looks for any of the four old files**, not just `current_run.dat`. Otherwise a retry would never happen once `current_run.dat` itself had moved.

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/OldRunFilesTests.cs`:

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class OldRunFilesTests
{
    private static (string DataDir, RunFolders Runs) NewDataDir()
    {
        string dataDir = Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "WhoCarried");
        Directory.CreateDirectory(dataDir);
        return (dataDir, new RunFolders(Path.Combine(dataDir, "runs")));
    }

    private static void WriteStats(string path, string runKey) => RunStatsStore.Save(new RunStats { RunKey = runKey }, path);

    private static void WriteLog(string path, params string[] lines) => File.WriteAllLines(path, lines);

    private static string Header(string runKey) => LogReplay.HeaderLine("1.3.0", runKey, new DateTime(2026, 9, 29, 19, 18, 0));

    private static string FolderFor(RunFolders runs, string runKey) => Path.Combine(runs.Root, RunFolders.Name(runKey, DateTime.Now));

    [Test]
    public static void RunKeyOfTakesTheLastHeaderOrResumedLine()
    {
        Check.Equal("A:1", LogReplay.RunKeyOf(new[] { Header("A:1"), "player 1 = X (Y) #ffffff" }), "header");
        Check.Equal("A:2", LogReplay.RunKeyOf(new[] { Header("A:1"), LogReplay.ResumedLine("A:2", 3) }), "resumed wins");
        Check.Equal("", LogReplay.RunKeyOf(new[] { "effect sources: watching 3 hooks" }), "none");
    }

    [Test]
    public static void NothingToMoveDoesNothing()
    {
        (string dataDir, RunFolders runs) = NewDataDir();
        (int moved, string? error) = OldRunFiles.Move(dataDir, runs);
        Check.Equal(0, moved, "moved");
        Check.Equal<string?>(null, error, "error");
        Check.True(!Directory.Exists(runs.Root), "no runs folder made for nothing");
    }

    [Test]
    public static void MovesBothPairsIntoTheirOwnFolders()
    {
        (string dataDir, RunFolders runs) = NewDataDir();
        WriteStats(Path.Combine(dataDir, "current_run.dat"), "NEW:1790705897");
        WriteLog(Path.Combine(dataDir, "events.log"), Header("NEW:1790705897"), "line new");
        WriteStats(Path.Combine(dataDir, "current_run.previous.dat"), "OLD:1790545328");
        WriteLog(Path.Combine(dataDir, "events.previous.log"), Header("OLD:1790545328"), "line old");
        File.WriteAllText(Path.Combine(dataDir, "events.prev.log"), "a test helper's leftover");
        File.WriteAllText(Path.Combine(dataDir, "settings.json"), "{}");

        (int moved, string? error) = OldRunFiles.Move(dataDir, runs);

        Check.Equal(4, moved, "moved");
        Check.Equal<string?>(null, error, "error");
        string newer = FolderFor(runs, "NEW:1790705897"), older = FolderFor(runs, "OLD:1790545328");
        Check.Equal("NEW:1790705897", RunStatsStore.Load(Path.Combine(newer, RunFolders.StatsFile))!.RunKey, "current stats");
        Check.Equal("line new", File.ReadAllLines(Path.Combine(newer, RunFolders.LogFile))[1], "current log");
        Check.Equal("OLD:1790545328", RunStatsStore.Load(Path.Combine(older, RunFolders.StatsFile))!.RunKey, "previous stats");
        Check.Equal("line old", File.ReadAllLines(Path.Combine(older, RunFolders.LogFile))[1], "previous log");
        Check.True(!File.Exists(Path.Combine(dataDir, "current_run.dat")), "current stats gone from the root");
        Check.True(!File.Exists(Path.Combine(dataDir, "events.log")), "current log gone from the root");
        Check.True(File.Exists(Path.Combine(dataDir, "events.prev.log")), "other leftovers left alone");
        Check.True(File.Exists(Path.Combine(dataDir, "settings.json")), "settings left alone");
    }

    // The owner's run of 2026-09-29: they were a guest, so the log's header has their clock and the stats the host's.
    [Test]
    public static void MovePairsAGuestsLogAndStats()
    {
        (string dataDir, RunFolders runs) = NewDataDir();
        WriteStats(Path.Combine(dataDir, "current_run.dat"), "CEKDYSK8A9AB:1790705897");
        WriteLog(Path.Combine(dataDir, "events.log"), Header("CEKDYSK8A9AB:1790705894"), "line");

        OldRunFiles.Move(dataDir, runs);

        string folder = FolderFor(runs, "CEKDYSK8A9AB:1790705897");
        Check.True(File.Exists(Path.Combine(folder, RunFolders.StatsFile)), "stats in the folder named from the stats");
        Check.True(File.Exists(Path.Combine(folder, RunFolders.LogFile)), "log in the same folder");
        Check.Equal(1, Directory.GetDirectories(runs.Root).Length, "one folder, not two");
    }

    [Test]
    public static void ALogWithoutItsStatsGetsAFolderOfItsOwn()
    {
        (string dataDir, RunFolders runs) = NewDataDir();
        WriteStats(Path.Combine(dataDir, "current_run.dat"), "AAA:1790705897");
        WriteLog(Path.Combine(dataDir, "events.log"), Header("BBB:1790700000"), "line");

        OldRunFiles.Move(dataDir, runs);

        Check.True(File.Exists(Path.Combine(FolderFor(runs, "AAA:1790705897"), RunFolders.StatsFile)), "stats");
        Check.True(File.Exists(Path.Combine(FolderFor(runs, "BBB:1790700000"), RunFolders.LogFile)), "log, in its own run's folder");
    }

    [Test]
    public static void AFileWhoseRunCantBeReadStaysPut()
    {
        (string dataDir, RunFolders runs) = NewDataDir();
        File.WriteAllText(Path.Combine(dataDir, "current_run.dat"), "{ not json");
        WriteLog(Path.Combine(dataDir, "events.log"), "no header here");

        (int moved, _) = OldRunFiles.Move(dataDir, runs);

        Check.Equal(0, moved, "moved");
        Check.True(File.Exists(Path.Combine(dataDir, "current_run.dat")), "stats left");
        Check.True(File.Exists(Path.Combine(dataDir, "events.log")), "log left");
    }

    [Test]
    public static void NeverOverwritesAFileAlreadyThere()
    {
        (string dataDir, RunFolders runs) = NewDataDir();
        string folder = FolderFor(runs, "SEED:1790705897");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, RunFolders.StatsFile), "the newer copy");
        WriteStats(Path.Combine(dataDir, "current_run.dat"), "SEED:1790705897");

        (int moved, string? error) = OldRunFiles.Move(dataDir, runs);

        Check.Equal(0, moved, "moved");
        Check.True(error != null, "reported");
        Check.Equal("the newer copy", File.ReadAllText(Path.Combine(folder, RunFolders.StatsFile)), "kept");
        Check.True(File.Exists(Path.Combine(dataDir, "current_run.dat")), "the root file stays");
    }

    [Test]
    public static void MoveRetriesWhatIsLeft()
    {
        (string dataDir, RunFolders runs) = NewDataDir();
        WriteStats(Path.Combine(dataDir, "current_run.dat"), "SEED:1790705897");
        string log = Path.Combine(dataDir, "events.log");
        WriteLog(log, Header("SEED:1790705897"), "line");

        // A log held open without sharing can't be read or moved: the first launch moves only the stats.
        using (new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            (int first, _) = OldRunFiles.Move(dataDir, runs);
            Check.Equal(1, first, "first launch moves the stats");
            Check.True(File.Exists(log), "the log is still in the root");
        }

        (int second, string? again) = OldRunFiles.Move(dataDir, runs);
        Check.Equal(1, second, "next launch moves the log");
        Check.Equal<string?>(null, again, "no error");
        string folder = FolderFor(runs, "SEED:1790705897");
        Check.True(File.Exists(Path.Combine(folder, RunFolders.StatsFile)), "stats there");
        Check.True(File.Exists(Path.Combine(folder, RunFolders.LogFile)), "log there");
    }
}
```

(The locked-file test relies on Windows file sharing. The tests run on Windows here; if this suite is ever run on Linux, mark that test Windows-only.)

- [ ] **Step 2: Run the tests to see them fail**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- OldRunFiles`
Expected: the build fails with `'LogReplay' does not contain a definition for 'RunKeyOf'` and `The name 'OldRunFiles' does not exist`.

- [ ] **Step 3: Add `LogReplay.RunKeyOf`**

In `src/WhoCarried/Core/LogReplay.cs`, after `ResumedLine`:

```csharp
    /// <summary>The run a log belongs to: the key of its last header or "resumed" line, or "" if it has neither.</summary>
    public static string RunKeyOf(IEnumerable<string> lines)
    {
        string runKey = "";
        Match m;
        foreach (string line in lines)
            if ((m = Header.Match(line)).Success || (m = Resumed.Match(line)).Success)
                runKey = m.Groups[1].Value;
        return runKey;
    }
```

Also update `ResumedLine`'s doc comment: replace `<see cref="RunStatsStore.LoadIfResumable"/>` with `<see cref="RunFolders.FindResumable"/>`.

- [ ] **Step 4: Write `OldRunFiles`**

Create `src/WhoCarried/Core/OldRunFiles.cs`:

```csharp
namespace WhoCarried.Core;

/// <summary>
/// Moves the run files older versions kept in the data folder's root (current_run.dat with events.log, and the
/// ".previous" pair) into their runs\ folders, once. Each file's run comes from inside it; a stats file and a log go
/// together when their seeds match (a co-op guest's log header and stats can have different start times). A file whose
/// run can't be read stays where it is, and nothing already in runs\ is ever overwritten. What fails is tried again on
/// the next start. Never throws. Can come out once nearly everyone has been through it.
/// </summary>
public static class OldRunFiles
{
    private static readonly (string Stats, string Log)[] Pairs =
    {
        ("current_run.dat", "events.log"),
        ("current_run.previous.dat", "events.previous.log"),
    };

    /// <returns>How many files were moved, and the first failure (the rest are still tried).</returns>
    public static (int Moved, string? Error) Move(string dataDir, RunFolders runs)
    {
        if (!Pairs.Any(p => File.Exists(Path.Combine(dataDir, p.Stats)) || File.Exists(Path.Combine(dataDir, p.Log))))
            return (0, null);

        int moved = 0;
        string? error = null;
        foreach ((string statsName, string logName) in Pairs)
        {
            string statsPath = Path.Combine(dataDir, statsName), logPath = Path.Combine(dataDir, logName);
            string statsKey = File.Exists(statsPath) ? RunStatsStore.Load(statsPath)?.RunKey ?? "" : "";
            string logKey = File.Exists(logPath) ? LogKey(logPath) : "";
            // Together when they're the same run: the folder is named from the stats, which carry the latest key.
            if (statsKey.Length > 0 && logKey.Length > 0 && RunFolders.Seed(statsKey) == RunFolders.Seed(logKey))
                logKey = statsKey;
            if (statsKey.Length > 0) MoveOne(statsPath, statsKey, RunFolders.StatsFile);
            if (logKey.Length > 0) MoveOne(logPath, logKey, RunFolders.LogFile);
        }
        return (moved, error);

        void MoveOne(string source, string runKey, string fileName)
        {
            try
            {
                string folder = Path.Combine(runs.Root, RunFolders.Name(runKey, File.GetLastWriteTime(source)));
                Directory.CreateDirectory(folder);
                File.Move(source, Path.Combine(folder, fileName), overwrite: false);
                moved++;
            }
            catch (Exception e)
            {
                error ??= $"{Path.GetFileName(source)}: {e.Message}";
            }
        }
    }

    private static string LogKey(string path)
    {
        try { return LogReplay.RunKeyOf(File.ReadLines(path)); }
        catch (Exception) { return ""; }
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- OldRunFiles`
Expected: every `OldRunFilesTests` test passes. Then run all tests: everything passes.

- [ ] **Step 6: Write the two settled details back into the spec**

In `docs/design/specs/2026-10-03-multiple-runs-design.md`, section "Moving today's files":
- Replace step 1 with: `1. If none of the four old files is in the root, it does nothing. On every launch after the first, that's all it costs: four file checks.`
- Replace step 5 with: `5. **Nothing is ever overwritten.** A file already in the run's folder stays, and the root file stays where it is too; \`mod.log\` says so. That only happens if someone goes back to an older Who Carried mid-run, which in practice means local builds. A folder that's already there is fine, so a move that failed half-way finishes next launch.`

- [ ] **Step 7: Commit**

```bash
git add src/WhoCarried/Core/LogReplay.cs src/WhoCarried/Core/OldRunFiles.cs tests/WhoCarried.Tests/OldRunFilesTests.cs docs/design/specs/2026-10-03-multiple-runs-design.md
git commit -m "feat: move today's run files into their runs folders once

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Tracker and GameReader: mod.log, a folder per run, run.json, pruning

**Files:**
- Modify: `src/WhoCarried/Game/GameReader.cs` (add `ReadRunInfo(…)` and its helpers)
- Modify: `src/WhoCarried/Game/Tracker.cs` (fields, `Init`, `OnRunStarted`, `Save`, `StatsPath`)
- Modify: `src/WhoCarried/Core/EventLog.cs` (doc comments only)
- Test: `tests/WhoCarried.Tests/EventLogTests.cs`

**Interfaces:**
- Consumes: `RunFolders` (`Create`, `FindResumable`, `Prune`, `Keep`, `StatsFile`, `LogFile`, `InfoFile`), `RunFolders.Resumable`, `OldRunFiles.Move`, `RunInfo` (`Save`, `ModsLine`, `Unknown`), `LogReplay.HeaderLine/ResumedLine`, `EventLog`.
- Produces (used by Task 6): `Tracker.DataDir` unchanged; `RunFolders` lives at `Path.Combine(Tracker.DataDir, "runs")`.

The Game layer has no unit tests (it needs the running game). This task is checked by the `EventLog` test, the full test run and the mod's build; Task 8 checks it in the game.

- [ ] **Step 1: Write the failing `EventLog` test**

Add to `EventLogTests`:

```csharp
    [Test]
    public static void ResetKeepsTheLastLogAsPrevious()
    {
        string path = Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "mod.log");
        var log = new EventLog(path);
        log.Reset("launch 1");
        log.Write("line a");
        log.Reset("launch 2");
        Check.Equal(Path.ChangeExtension(path, ".previous.log"), log.PreviousPath, "mod.previous.log");
        Check.Equal("line a", File.ReadAllLines(log.PreviousPath)[1], "last launch kept");
        Check.Equal("launch 2", File.ReadAllLines(path)[0], "this launch fresh");
    }
```

- [ ] **Step 2: Run it**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- EventLog`
Expected: PASS. `EventLog.Reset` already keeps `.previous`; this pins the behaviour `mod.log` now depends on. (A test that passes first is fine here: it guards existing code that gains a new use.)

- [ ] **Step 3: Update `EventLog`'s doc comments**

In `src/WhoCarried/Core/EventLog.cs`:
- On `PreviousPath`: `/// <summary>The last launch's log, kept when <see cref="Reset"/> starts a new one ("mod.previous.log" for "mod.log").</summary>`
- On `Reset`: `/// <summary>Starts a fresh log (mod.log, at each launch); the last one is kept as <see cref="PreviousPath"/>. Run logs are never reset.</summary>`

- [ ] **Step 4: Add `GameReader.RunInfo`**

In `src/WhoCarried/Game/GameReader.cs`, add these usings (beside the existing ones, in order):

```csharp
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;
```

and these members (after `RunKey`):

```csharp
    /// <summary>
    /// What this run is being played with, for run.json. Every field is read on its own: whatever the game won't give
    /// (a branch without the call, a mod without a version) is "unknown", and this never throws.
    /// </summary>
    public static RunInfo ReadRunInfo(IRunState run, string runKey, string modVersion)
    {
        return new RunInfo
        {
            RunKey = runKey,
            WhoCarried = modVersion,
            GameBuild = Safe(() => ReleaseInfoManager.Instance.ReleaseInfo?.Version ?? ReleaseInfoManager.Instance.SemVer?.ToString()),
            Language = Safe(() => LocManager.Instance?.Language),
            LocalPlayer = Safe(() => PlatformUtil.GetLocalPlayerId(PlatformType.Steam).ToString(CultureInfo.InvariantCulture)),
            Profile = Safe(() => SaveManager.Instance.CurrentProfileId.ToString(CultureInfo.InvariantCulture)),
            Mods = LoadedMods(),
            Players = SafeList(() => Players(run).Select(p => new RunInfo.PlayerEntry
            {
                Id = p.NetId.ToString(CultureInfo.InvariantCulture),
                Name = p.Name,
                CharacterId = p.CharacterId,
                CharacterName = p.Character,
                Colour = p.ColorHex,
            }).ToList()),
            Written = DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Every loaded mod, by its manifest's id and version (the folder name when it has no manifest).</summary>
    private static List<RunInfo.ModVersion> LoadedMods() =>
        SafeList(() => ModManager.GetLoadedMods().Select(m => new RunInfo.ModVersion
        {
            Id = Safe(() => m.manifest?.id ?? Path.GetFileName(m.path)),
            Version = Safe(() => m.manifest?.version ?? m.version?.ToString()),
        }).ToList());

    private static string Safe(Func<string?> read)
    {
        try
        {
            string? value = read();
            return string.IsNullOrWhiteSpace(value) ? RunInfo.Unknown : value;
        }
        catch (Exception)
        {
            return RunInfo.Unknown;
        }
    }

    private static List<T> SafeList<T>(Func<List<T>> read)
    {
        try { return read(); }
        catch (Exception) { return new List<T>(); }
    }
```

- [ ] **Step 5: Rewire `Tracker`**

In `src/WhoCarried/Game/Tracker.cs`:

**Fields.** Replace `private static EventLog? _log;` with:

```csharp
    private static EventLog? _log;      // where lines go now: mod.log until a run starts, then that run's events.log
    private static EventLog? _modLog;
    private static RunFolders _runs = new("");
    private static string? _runDir;     // the run's folder; null before the first run, or if it couldn't be made
```

**Stats path.** Replace the `StatsPath` property and its comment with:

```csharp
    /// <summary>The run's stats.dat; null when there's no run folder (counting carries on in memory).</summary>
    private static string? StatsPath => _runDir == null ? null : Path.Combine(_runDir, RunFolders.StatsFile);
```

**`Note`'s comment:** `/// <summary>Appends a free-form line to the log: the run's events.log, or mod.log before a run starts.</summary>`

**`Init`.** Update the doc comment's last sentence to: `Files an older version left in data/ beside the DLL are moved over, and the single run older versions kept is moved into runs\.` Replace the body after `Directory.CreateDirectory` with:

```csharp
    public static void Init(string modDir)
    {
        _dataDir = Path.GetFullPath(Godot.ProjectSettings.GlobalizePath("user://WhoCarried"));
        string oldDir = Path.Combine(modDir, "data");
        (int moved, string? error) = DataFolderMove.Run(oldDir, _dataDir);
        try { Directory.CreateDirectory(_dataDir); }
        catch (Exception e) { Log.Error($"[WhoCarried] can't make {_dataDir}: {e.Message}"); }
        _runs = new RunFolders(Path.Combine(_dataDir, "runs"));
        (int runFiles, string? runError) = OldRunFiles.Move(_dataDir, _runs);

        _modLog = new EventLog(Path.Combine(_dataDir, "mod.log"));
        string started = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        try { _modLog.Reset($"Who Carried v{ModEntry.Version} - game started {started}"); }
        catch (Exception e) { Log.Warn($"[WhoCarried] can't start mod.log: {e.Message}"); }
        _log = _modLog;
        if (moved > 0) _modLog.Write($"moved {moved} files here from {oldDir}");
        if (error != null) Log.Warn($"[WhoCarried] moving files from {oldDir}: {error}");
        if (runFiles > 0) _modLog.Write($"moved {runFiles} run files into {_runs.Root}");
        if (runError != null) _modLog.Write($"moving run files into {_runs.Root}: {runError}");
    }
```

**`OnRunStarted`.** Replace the whole method with:

```csharp
    public static void OnRunStarted(IRunState run)
    {
        CardCreationSources.NewFight();
        _run = run;
        string key = GameReader.RunKey(run);
        bool loaded = _origin.TakeLoadedFromSave(GameReader.ReloadCount());
        RunFolders.Resumable? resumed = _runs.FindResumable(key, loaded);
        if (resumed != null)
        {
            _stats = resumed.Stats;
            _runDir = resumed.Folder;
        }
        else
        {
            _stats = new RunStats { RunKey = key };
            try { _runDir = _runs.Create(key, DateTime.Now); }
            catch (Exception e)
            {
                _runDir = null;
                LogError("making the run's folder", e); // goes to mod.log (or the last run's log): counting carries on in memory
            }
        }
        _log = _runDir != null ? new EventLog(Path.Combine(_runDir, RunFolders.LogFile)) : _modLog;
        _log?.Write(resumed == null
            ? LogReplay.HeaderLine(ModEntry.Version, key, DateTime.Now)
            : LogReplay.ResumedLine(key, _stats.Fights.Count));

        IReadOnlyList<PlayerInfo> players = GameReader.Players(run);
        _names = players.ToDictionary(p => p.NetId, p => p.Name);
        foreach (PlayerInfo p in players)
            _log?.Write($"player {p.NetId} = {p.Name} ({p.Character}) #{p.ColorHex}");

        WriteRunInfo(run, key);
        int pruned = _runs.Prune(RunFolders.Keep, _runDir, problem => _log?.Write($"can't delete an old run: {problem}"));
        if (pruned > 0) _log?.Write($"deleted {pruned} old runs, keeping the last {RunFolders.Keep}");
    }

    /// <summary>run.json, rewritten at each start and resume, and the "mods:" line in the log.</summary>
    private static void WriteRunInfo(IRunState run, string key)
    {
        RunInfo info = GameReader.ReadRunInfo(run, key, ModEntry.Version);
        _log?.Write(RunInfo.ModsLine(info.Mods));
        if (_runDir == null) return;
        try { RunInfo.Save(info, Path.Combine(_runDir, RunFolders.InfoFile)); }
        catch (Exception e) { LogError("run.json", e); }
    }
```

The old `.previous.dat` copying and `_log?.Reset(...)` are gone with it. A run's log is only ever added to: a new folder's `events.log` doesn't exist yet, so the header is its first line.

**`Save`.** Replace with:

```csharp
    private static void Save()
    {
        string? path = StatsPath;
        if (path == null) return; // no run folder: the recap still works from memory
        try { RunStatsStore.Save(_stats, path); }
        catch (Exception e) { LogError("save", e); }
    }
```

- [ ] **Step 6: Build and test**

Run: `"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release`
Expected: `Build succeeded` with no errors. If `RunStatsStore.LoadIfResumable` or `.previous` still appear anywhere, they're leftovers: `grep -rn "LoadIfResumable\|previous.dat" src` must print nothing.

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/WhoCarried/Game/GameReader.cs src/WhoCarried/Game/Tracker.cs src/WhoCarried/Core/EventLog.cs tests/WhoCarried.Tests/EventLogTests.cs
git commit -m "feat: each run in its own folder, mod.log outside runs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Replay picks its run from replay.flag

**Files:**
- Modify: `src/WhoCarried/UI/Replay.cs` (`StartIfFlagged`, `Run`, the class's doc comment)

**Interfaces:**
- Consumes: `RunFolders` (`Newest`, `Named`, `StatsFile`, `LogFile`), `Tracker.Note`, `RunStatsStore.Load`.
- Produces: nothing new.

The folder choice itself is unit-tested in Task 1 (`Newest`, `Named`). This task is the glue; it's checked by the build here and in the game in Task 8 (M9–M11).

- [ ] **Step 1: Pick the run**

In `src/WhoCarried/UI/Replay.cs`, replace `StartIfFlagged` with:

```csharp
    public static void StartIfFlagged(string dataDir)
    {
        string flag = Path.Combine(dataDir, "replay.flag");
        if (!File.Exists(flag)) return;
        string name = "";
        try { name = File.ReadAllText(flag).Trim(); }
        catch (Exception e) { Tracker.LogError("replay.flag", e); }
        Later.Run(10, () => Run(dataDir, name));
    }
```

Change `Run`'s signature to `private static void Run(string dataDir, string name)` and replace its first statement (`LogReplay.Result log = LogReplay.Parse(File.ReadAllLines(Path.Combine(dataDir, "events.log")), Title);`) with:

```csharp
            var runs = new RunFolders(Path.Combine(dataDir, "runs"));
            string? folder = name.Length > 0 ? runs.Named(name) : runs.Newest();
            if (folder == null)
            {
                // Never fall back to the newest run when one was named: that would quietly replay the wrong one.
                Tracker.Note(name.Length > 0 ? $"replay: no run called {name} in {runs.Root}" : $"replay: no runs in {runs.Root}");
                Tracker.Note("replay done");
                return;
            }
            Tracker.Note($"replay: {Path.GetFileName(folder)}");
            LogReplay.Result log = LogReplay.Parse(File.ReadAllLines(Path.Combine(folder, RunFolders.LogFile)), Title);
```

Replace the block that reads the saved stats:

```csharp
            // The player's own block isn't in the log; the saved stats of the same run have it.
            RunStats? saved = RunStatsStore.Load(Path.Combine(dataDir, "current_run.dat"));
            if (saved != null && saved.RunKey == log.RunKey)
```

with:

```csharp
            // The player's own block isn't in the log; the run's saved stats have it. Same folder, same run, even
            // when a guest's log and stats carry different start times.
            RunStats? saved = RunStatsStore.Load(Path.Combine(folder, RunFolders.StatsFile));
            if (saved != null)
```

Screenshots keep going to `dataDir` (unchanged).

- [ ] **Step 2: Update the class's doc comment**

Its first sentence becomes: `Rebuilds the recap of a recorded run with today's rules, if replay.flag exists in the mod's data folder (<see cref="Tracker.DataDir"/>): the run played most recently, or the run whose folder name (in runs\) the flag holds.` Replace `the player's own block from current_run.dat` with `the player's own block from the run's stats.dat`.

- [ ] **Step 3: Build and test**

Run: `"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release`
Expected: `Build succeeded`. `grep -rn "current_run\|\"events.log\"" src` prints only `OldRunFiles.cs` (and comments).

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/WhoCarried/UI/Replay.cs
git commit -m "feat: replay.flag replays the newest run or the one it names

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Docs

**Files:**
- Modify: `README.md`, `docs/README.md`, `docs/localization.md`, `CHANGELOG.md`

- [ ] **Step 1: README**

In `README.md`:
- Replace the **Files** bullet (line 32) with: `- **Files** live in the game's save folder under \`WhoCarried/\` (\`%APPDATA%\SlayTheSpire2\WhoCarried\` on Windows). Each run has its own folder in \`runs\\\`, named by when it started and its seed, holding its stats, its log (\`events.log\`) and \`run.json\` (the mods, game build and players it was played with). The last 100 runs are kept. Lines written outside a run go to \`mod.log\`, fresh each launch.`
- Replace the `replay.flag` bullet with: `- \`replay.flag\` rebuilds the recap of the run played most recently from its \`events.log\`, with the current rules. If the file holds a run's folder name (e.g. \`2026-09-27_2140_TB20BJ6WUGJ2\`), it rebuilds that run instead; a name that isn't there replays nothing and says so in \`mod.log\`.`

- [ ] **Step 2: docs/README.md**

Change `**Two later changes the docs don't show:**` to `**Three later changes the docs don't show:**`, and add a third bullet:

```markdown
- Each run has its own folder in `runs\` (`stats.dat`, `events.log`, `run.json`) instead of the single `current_run.dat` and `events.log`, and lines written outside a run go to `mod.log`. See [keeping every run](design/specs/2026-10-03-multiple-runs-design.md).
```

Add a row to the spec/plan table, at the end:

```markdown
| [Keeping every run](design/specs/2026-10-03-multiple-runs-design.md) | [plan](design/plans/2026-10-03-multiple-runs.md) | A folder per run, so a run picked up again keeps everything; what each run was played with, for a later run history |
```

- [ ] **Step 3: docs/localization.md**

Replace ``Wait for `preview done` in `events.log`.`` (lines 107–108) with ``Wait for `preview done` in `mod.log`.``

- [ ] **Step 4: CHANGELOG**

Add above `## [1.3.0] — 2026-09-27`:

```markdown
## [Unreleased]

### Fixed

- A run you leave and come back to keeps everything it counted, however many runs you play in between. Starting a singleplayer run in the middle of a co-op run used to wipe the co-op run's first part. Each run now has its own folder in `WhoCarried\runs\`, and the last 100 runs are kept. Your last two runs are moved there the first time the game starts with this version.
```

- [ ] **Step 5: Commit**

```bash
git add README.md docs/README.md docs/localization.md CHANGELOG.md
git commit -m "docs: keeping every run

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: In the game

This is the only task that touches the owner's real files and the game. **Ask the owner before Step 2**, every time, even if they said yes earlier in the session: they play on this machine.

The checks that need a real run played (M3–M8) and a second player (M13–M14) are the owner's, after this task; the list is in the spec. M12 is optional and not done here.

- [ ] **Step 1: Check the game isn't running**

Run: `tasklist //FI "IMAGENAME eq SlayTheSpire2.exe"`
Expected: `INFO: No tasks are running…`. If it's running, stop and ask the owner.

- [ ] **Step 2: Back up the data folder (M1)**

```bash
cp -r "$APPDATA/SlayTheSpire2/WhoCarried" "$APPDATA/SlayTheSpire2/WhoCarried-backup-$(date +%Y-%m-%d_%H%M)"
ls "$APPDATA/SlayTheSpire2/" | grep WhoCarried-backup
```

Expected: the backup folder is listed. Also note the sizes and SHA-256 of the four run files for comparison afterwards:

```bash
cd "$APPDATA/SlayTheSpire2/WhoCarried" && sha256sum current_run.dat events.log current_run.previous.dat events.previous.log
```

- [ ] **Step 3: Deploy and launch**

**Waiting on a launch.** `mod.log` is rewritten at each launch, so the last launch's lines (`replay done` included) are there until the mod starts. Every wait in this task therefore uses a marker file made just before the launch, and waits for `mod.log` to be newer than it as well as for the line. Foreground `sleep` is blocked in this harness, so wait with the Monitor tool and an until-loop:

```bash
MARK="<scratchpad>/launch.mark"   # the session's scratchpad directory
touch "$MARK"
# launch, then:
until [ "$APPDATA/SlayTheSpire2/WhoCarried/mod.log" -nt "$MARK" ] && grep -q "<line>" "$APPDATA/SlayTheSpire2/WhoCarried/mod.log"; do sleep 3; done
```

```bash
powershell -File tools/deploy.ps1
touch "$MARK"
"/e/Steam/steam.exe" -applaunch 2868840
```

Expected: `Deployed to …\mods\WhoCarried`. Wait with `<line>` = `game started` (the mod has started; give the main menu a few more seconds). Then check `%APPDATA%\SlayTheSpire2\logs\godot.log`: its `Loading assembly DLL` line must name `mods\WhoCarried\WhoCarried.dll`, not the Workshop copy. If it names the Workshop copy, stop and ask the owner to switch copies in the game's mod settings.

- [ ] **Step 4: Check the migration (M1)**

```bash
cd "$APPDATA/SlayTheSpire2/WhoCarried" && ls && ls runs/*/ && cat mod.log
sha256sum runs/*/stats.dat runs/*/events.log
```

Expected:
- No `current_run.dat`, `events.log`, `current_run.previous.dat` or `events.previous.log` in the root. `events.prev.log`, `settings.json`, the flags and the screenshots are still there.
- `runs\` has one folder for the run of the 27th (`…_TB20BJ6WUGJ2` or whichever seed `current_run.previous.dat` holds) and one for the run of the 29th (`2026-09-29_…_CEKDYSK8A9AB`), each with `stats.dat` and `events.log`.
- The hashes match Step 2's (each file is the same bytes, just moved).
- `mod.log` starts with `Who Carried v… - game started …` and says `moved 4 run files into …`.

- [ ] **Step 5: Replay the newest run (M9)**

Close the game (`taskkill //IM SlayTheSpire2.exe //F`). Write an empty flag and relaunch:

```bash
: > "$APPDATA/SlayTheSpire2/WhoCarried/replay.flag"
touch "$MARK"
"/e/Steam/steam.exe" -applaunch 2868840
```

Wait with `<line>` = `replay done`. Expected: `mod.log` says `replay: 2026-09-29_…_CEKDYSK8A9AB` (the newest), and `replay-1-scoreboard.png` … `replay-10-export.png` are fresh. Look at `replay-9-export.png`: four players, the run's numbers.

This launch is also **M2**: check `mod.previous.log` holds the last launch's lines (the `moved 4 run files` line) and `mod.log` is fresh. Also check the root still has no `current_run.dat` (no second migration).

- [ ] **Step 6: Replay a named run (M10)**

Close the game. Write the older run's folder name into the flag (take it from `ls runs`), e.g.:

```bash
printf '%s' "$(ls "$APPDATA/SlayTheSpire2/WhoCarried/runs" | sort | head -1)" > "$APPDATA/SlayTheSpire2/WhoCarried/replay.flag"
touch "$MARK"
"/e/Steam/steam.exe" -applaunch 2868840
```

Wait with `<line>` = `replay done`. Expected: `mod.log` names that older folder, and the screenshots show that run.

- [ ] **Step 7: Replay a name that isn't there (M11)**

Close the game. `printf '%s' '2020-01-01_0000_NOPE' > "$APPDATA/SlayTheSpire2/WhoCarried/replay.flag"`, `touch "$MARK"`, relaunch, and wait with `<line>` = `replay done`. Expected: `mod.log` says `replay: no run called 2020-01-01_0000_NOPE in …`, and no replay screenshots are newer than Step 6's.

- [ ] **Step 8: Clean up**

Close the game. Delete `replay.flag` (`rm "$APPDATA/SlayTheSpire2/WhoCarried/replay.flag"`), or replay runs at every launch. Keep the backup folder; tell the owner where it is and that they can delete it once they're happy.

- [ ] **Step 9: Update the session memory**

The memories that say to wait for `preview done` / `replay done` in `events.log` now mean `mod.log` (`owner-plays-on-this-machine`, `preview-screenshot-timing`, `who-carried-release-mechanics`, `launch-crash-triage`). Update each one's wording, and mark `multiple-runs-in-progress` as implemented, with the manual checks M3–M8 and M13–M14 left for the owner.

- [ ] **Step 10: Report to the owner**

Say what was checked (M1, M2, M9–M11), with the hashes matching and the replay screenshot, and hand over the cases for them: M3–M8 (play a run; the solo "run in between" via two profiles), and M13–M14 with a second player. No commit in this task (nothing in the repo changed).
