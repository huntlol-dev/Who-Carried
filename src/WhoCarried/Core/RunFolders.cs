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
