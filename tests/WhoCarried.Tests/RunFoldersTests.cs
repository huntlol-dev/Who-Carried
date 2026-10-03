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
