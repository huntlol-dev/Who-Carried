namespace WhoCarried.Core;

/// <summary>
/// Moves the run files older versions kept in the data folder's root (current_run.dat with events.log, and the
/// ".previous" pair) into their runs\ folders, once. Each file's run comes from inside it; a stats file and a log go
/// together when their seeds match (a co-op guest's log header and stats can have different start times). A file whose
/// partner already moved on an earlier start joins it there. A file whose run can't be read stays where it is, and
/// nothing already in runs\ is ever overwritten. What fails is tried again on the next start. Never throws. Can come
/// out once nearly everyone has been through it.
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
            if (statsKey.Length > 0) MoveOne(statsPath, statsKey, RunFolders.StatsFile, RunFolders.LogFile, File.Exists(logPath));
            if (logKey.Length > 0) MoveOne(logPath, logKey, RunFolders.LogFile, RunFolders.StatsFile, File.Exists(statsPath));
        }
        return (moved, error);

        void MoveOne(string source, string runKey, string fileName, string partnerName, bool partnerInRoot)
        {
            try
            {
                string? folder = partnerInRoot ? null : PartnersFolder(runs, runKey, fileName, partnerName);
                folder ??= Path.Combine(runs.Root, RunFolders.Name(runKey, File.GetLastWriteTime(source)));
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

    /// <summary>The most recently played folder of this run's seed that has its partner file but not this one, or null.</summary>
    private static string? PartnersFolder(RunFolders runs, string runKey, string fileName, string partnerName) =>
        runs.FoldersFor(RunFolders.Seed(runKey))
            .Where(f => File.Exists(Path.Combine(f, partnerName)) && !File.Exists(Path.Combine(f, fileName)))
            .OrderByDescending(RunFolders.SafeLastPlayed)
            .FirstOrDefault();

    private static string LogKey(string path)
    {
        try { return LogReplay.RunKeyOf(File.ReadLines(path)); }
        catch (Exception) { return ""; }
    }
}
