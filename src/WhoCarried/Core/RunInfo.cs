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
