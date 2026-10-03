namespace WhoCarried.Core;

/// <summary>Plain-text debug log, one line per event. Never throws on write.</summary>
public sealed class EventLog
{
    private readonly object _lock = new();

    public EventLog(string path) => FilePath = path;

    public string FilePath { get; }

    /// <summary>The last launch's log, kept when <see cref="Reset"/> starts a new one ("mod.previous.log" for "mod.log").</summary>
    public string PreviousPath => Path.ChangeExtension(FilePath, ".previous" + Path.GetExtension(FilePath));

    /// <summary>Starts a fresh log (mod.log, at each launch); the last one is kept as <see cref="PreviousPath"/>. Run logs are never reset.</summary>
    public void Reset(string header)
    {
        lock (_lock)
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            try
            {
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 0) File.Copy(FilePath, PreviousPath, overwrite: true);
            }
            catch (Exception)
            {
                // keeping the old log is a nicety; the new one matters more
            }
            File.WriteAllText(FilePath, header + Environment.NewLine);
        }
    }

    public void Write(string line)
    {
        try
        {
            lock (_lock) File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch (Exception)
        {
            // logging must never break the game
        }
    }
}
