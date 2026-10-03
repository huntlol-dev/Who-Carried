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

    // A guest's log and stats have different clocks, here minutes apart: left to its own key, the late one would get a second folder.
    [Test]
    public static void GuestLogLeftBehindJoinsItsStatsLater()
    {
        (string dataDir, RunFolders runs) = NewDataDir();
        WriteStats(Path.Combine(dataDir, "current_run.dat"), "CEKDYSK8A9AB:1790705897");
        string log = Path.Combine(dataDir, "events.log");
        WriteLog(log, Header("CEKDYSK8A9AB:1790705800"), "line");

        using (new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.None))
            OldRunFiles.Move(dataDir, runs);
        (int second, string? error) = OldRunFiles.Move(dataDir, runs);

        Check.Equal(1, second, "next launch moves the log");
        Check.Equal<string?>(null, error, "no error");
        string folder = FolderFor(runs, "CEKDYSK8A9AB:1790705897");
        Check.True(File.Exists(Path.Combine(folder, RunFolders.StatsFile)), "stats there");
        Check.True(File.Exists(Path.Combine(folder, RunFolders.LogFile)), "log joined them");
        Check.Equal(1, Directory.GetDirectories(runs.Root).Length, "one folder, not two");
    }

    [Test]
    public static void GuestStatsLeftBehindJoinTheirLogLater()
    {
        (string dataDir, RunFolders runs) = NewDataDir();
        string stats = Path.Combine(dataDir, "current_run.dat");
        WriteStats(stats, "CEKDYSK8A9AB:1790705897");
        WriteLog(Path.Combine(dataDir, "events.log"), Header("CEKDYSK8A9AB:1790705800"), "line");

        using (new FileStream(stats, FileMode.Open, FileAccess.Read, FileShare.None))
            OldRunFiles.Move(dataDir, runs);
        (int second, string? error) = OldRunFiles.Move(dataDir, runs);

        Check.Equal(1, second, "next launch moves the stats");
        Check.Equal<string?>(null, error, "no error");
        string folder = FolderFor(runs, "CEKDYSK8A9AB:1790705800");
        Check.True(File.Exists(Path.Combine(folder, RunFolders.StatsFile)), "stats joined the log");
        Check.True(File.Exists(Path.Combine(folder, RunFolders.LogFile)), "log there");
        Check.Equal(1, Directory.GetDirectories(runs.Root).Length, "one folder, not two");
    }
}
