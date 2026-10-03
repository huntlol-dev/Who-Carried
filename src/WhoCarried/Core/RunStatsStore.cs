using System.Text.Json;

namespace WhoCarried.Core;

public static class RunStatsStore
{
    public static void Save(RunStats stats, string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(stats, WhoCarriedJson.Default.RunStats));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>The saved stats whatever their state, or null if missing or unreadable.</summary>
    public static RunStats? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), WhoCarriedJson.Default.RunStats) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
