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

    // LastPlayed only reads timestamps, which Windows hands out even for a held file, so the dating is swapped for one that throws.
    [Test]
    public static void PruneKeepsAFolderItCantDate()
    {
        string root = NewRoot();
        string unknown = MakeRun(root, "2026-09-01_1000_UNKNOWN", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        MakeRun(root, "2026-09-02_1000_B", new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc));
        MakeRun(root, "2026-09-03_1000_C", new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc));
        var problems = new List<string>();
        DateTime? Dating(string folder) =>
            Path.GetFileName(folder).EndsWith("UNKNOWN") ? null : RunFolders.LastPlayed(folder);
        int deleted = new RunFolders(root).Prune(1, active: null, problems.Add, Dating);
        Check.Equal(1, deleted, "only the older dated one goes: the undated folder isn't counted towards keep");
        Check.True(Directory.Exists(unknown), "the folder it can't date is kept");
        Check.True(Directory.Exists(Path.Combine(root, "2026-09-03_1000_C")), "newest kept");
        Check.True(!Directory.Exists(Path.Combine(root, "2026-09-02_1000_B")), "oldest dated gone");
        Check.Equal(1, problems.Count, "reported once");
        Check.True(problems[0].StartsWith("2026-09-01_1000_UNKNOWN: "), "names the folder");
    }

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

    [Test]
    public static void ResumeSkipsStatsWithANullRunKey()
    {
        var runs = new RunFolders(NewRoot());
        string folder = runs.Create("SEED:1789237698", DateTime.Now);
        File.WriteAllText(Path.Combine(folder, RunFolders.StatsFile), "{\"RunKey\": null}");
        Check.True(runs.FindResumable("SEED:1789237698", loadedFromSave: true) == null, "damaged stats");
        Check.True(runs.FindResumable("SEED:1789237698", loadedFromSave: false) == null, "damaged stats, not from a save");
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
}
