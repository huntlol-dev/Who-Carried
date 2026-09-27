using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class HpCutTests
{
    private static readonly SourceRef FurCoat = new(SourceKind.Relic, "FUR_COAT", "Fur Coat");

    [Test]
    public static void OnlyHpTakenAwayCounts()
    {
        Check.Equal(149, HpCut.Amount(150, 1m), "to 1 HP");
        Check.Equal(40, HpCut.Amount(40, 0m), "to 0");
        Check.Equal(40, HpCut.Amount(40, -5m), "below 0 is 0");
        Check.Equal(0, HpCut.Amount(10, 999_999_999m), "raised");
        Check.Equal(0, HpCut.Amount(0, 1m), "already at 0");
    }

    [Test]
    public static void HpCutIsntDamage()
    {
        var s = new RunStats();
        s.BeginFight(1, 3, "Gremlin Gang");
        s.RecordHpCut(1, FurCoat, 149);
        s.RecordHpCut(1, FurCoat, 20);
        PlayerTotals t = s.Get(1)!;
        Check.Equal(169, t.HpCut["Relic:FUR_COAT"].Amount, "kept by source");
        Check.Equal(0, t.DamageDealt, "not damage");
        Check.Equal(0, t.Sources.Count, "not a damage source");
        Check.Equal(0, t.BiggestHit, "not a hit");
        Check.Equal(0, s.Fights[0].DamageByPlayer.Count, "not in the fight's damage");
    }

    [Test]
    public static void OlderSavesLoadWithNoHpCut()
    {
        string path = Path.Combine(Path.GetTempPath(), $"whocarried-hpcut-{Guid.NewGuid():N}.dat");
        File.WriteAllText(path, "{\"RunKey\":\"K\",\"Players\":{\"1\":{\"DamageDealt\":5}}}");
        try
        {
            RunStats loaded = RunStatsStore.Load(path)!;
            Check.Equal(0, loaded.Get(1)!.HpCut.Count, "empty, not null");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public static void AnHpCutReplaysAsHpCut()
    {
        string line = LogReplay.HpCutLine("Ash", FurCoat, 149, "GREMLIN_LEADER");
        Check.Equal("Ash cut 149 hp with Relic:FUR_COAT (Fur Coat) | target GREMLIN_LEADER", line, "line");
        string[] log = { "player 1 = Ash (The Regent) #d85a30", "[F3 A1] fight start: Gremlin Gang", "[F3 A1] " + line };
        PlayerTotals ash = LogReplay.Parse(log).Stats.Get(1)!;
        Check.Equal(149, ash.HpCut["Relic:FUR_COAT"].Amount, "HP cut");
        Check.Equal(0, ash.DamageDealt, "not damage");
    }

    [Test]
    public static void TheLineNamesEverySource()
    {
        string one = RecapBuilder.HpCutText(new[] { new HpCutPart("Fur Coat", 312) });
        Check.True(one.Contains("Fur Coat") && one.Contains("312"), "one source");
        string two = RecapBuilder.HpCutText(new[] { new HpCutPart("Fur Coat", 300), new HpCutPart("Axe", 12) });
        Check.True(two.Contains("Fur Coat") && two.Contains("Axe") && two.Contains("312"), "joined, total");
    }
}
