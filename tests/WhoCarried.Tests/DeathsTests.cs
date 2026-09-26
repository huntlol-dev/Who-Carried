using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class DeathsTests
{
    private static readonly PlayerInfo Alice = new(1, "Alice", "Ironclad", "d85a30");
    private static readonly PlayerInfo Bob = new(2, "Bob", "The Tailor", "7f77dd");
    private static readonly SourceRef Strike = new(SourceKind.Card, "STRIKE", "Strike");

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "current_run.json");

    [Test]
    public static void DeathsAddUpAndIgnoreNothing()
    {
        var s = new RunStats();
        s.RecordDeath(1);
        s.RecordDeath(1, 2);
        s.RecordDeath(2, 0);
        s.RecordDeath(2, -1);
        Check.Equal(3, s.Get(1)!.Deaths, "deaths add up");
        Check.True(s.Get(2) == null, "zero or fewer adds nothing");
    }

    [Test]
    public static void DeathsSurviveASave()
    {
        string path = TempFile();
        var s = new RunStats { RunKey = "SEED:123" };
        s.RecordDeath(1, 2);
        RunStatsStore.Save(s, path);
        Check.Equal(2, RunStatsStore.Load(path)!.Get(1)!.Deaths, "deaths");
    }

    [Test]
    public static void SaveFromBeforeDeathsLoadsWithNone()
    {
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "RunKey": "SEED:123", "Players": { "1": { "DamageDealt": 5 } } }""");
        RunStats? loaded = RunStatsStore.Load(path);
        Check.Equal(5, loaded!.Get(1)!.DamageDealt, "older fields still read");
        Check.Equal(0, loaded.Get(1)!.Deaths, "no deaths");
    }

    [Test]
    public static void ReplayCountsDeathsButNotTheKillsThatEndARun()
    {
        string[] log =
        {
            "Who Carried v1.2.0 - run SEED:1 started 2026-09-26 10:00",
            "player 11 = Moth (The Tailor) #5c350f",
            "player 12 = Jo (The Necrobinder) #d4537e",
            "[F2 A1] fight start: Toadpoles",
            "[F2 A1] Moth died",
            "[F2 A1] fight end, saved",
            "[F5 A1] fight start: Gremlins",
            "[F5 A1] Jo died",
            "[F5 A1] Moth died",
            "[F5 A1] fight end, saved",
            "[F52 A4] run ended: victory",
            "[F52 A4] Moth killed as the run ends, not counted",
            "[F52 A4] Jo killed as the run ends, not counted",
        };
        RunStats stats = LogReplay.Parse(log).Stats;
        Check.Equal(2, stats.Get(11)!.Deaths, "moth");
        Check.Equal(1, stats.Get(12)!.Deaths, "jo");
    }

    [Test]
    public static void ScoreboardRowsCarryDeaths()
    {
        var s = new RunStats();
        s.RecordDamage(1, Strike, 30);
        s.RecordDamage(2, Strike, 10);
        s.RecordDeath(2, 2);
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>(), "h");
        Check.Equal(0, v.Overview.Single(r => r.Label == "Alice").Deaths, "alice");
        Check.Equal(2, v.Overview.Single(r => r.Label == "Bob").Deaths, "bob");
    }
}
