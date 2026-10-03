using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class EventLogTests
{
    [Test]
    public static void ResetStartsFreshAndWriteAppends()
    {
        string path = Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "events.log");
        var log = new EventLog(path);
        log.Reset("header 1");
        log.Write("line a");
        log.Reset("header 2");
        log.Write("line b");
        string[] lines = File.ReadAllLines(path);
        Check.Equal(2, lines.Length, "line count");
        Check.Equal("header 2", lines[0], "header");
        Check.Equal("line b", lines[1], "line");
    }

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
}
