using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class SupportTests
{
    [Test]
    public static void SupportCountsForTheGiverOnly()
    {
        var s = new RunStats();
        s.RecordSupport(1, 2, SupportKind.Energy, 2);
        s.RecordSupport(1, 2, SupportKind.Energy, 1);
        s.RecordSupport(1, 3, SupportKind.Cards, 3);
        s.RecordSupport(1, 2, SupportKind.Block, 8);
        s.RecordSupport(1, 2, SupportKind.Buffs, 2);
        s.RecordSupport(1, 3, SupportKind.Draws, 4);
        PlayerTotals a = s.Get(1)!;
        Check.Equal(3, a.EnergyGiven, "energy");
        Check.Equal(3, a.CardsGiven, "cards");
        Check.Equal(8, a.BlockGiven, "block");
        Check.Equal(2, a.BuffsGiven, "buffs");
        Check.Equal(4, a.CardsDrawnForTeam, "draws");
        Check.Equal(3, a.Given(SupportKind.Energy), "Given(Energy)");
        Check.Equal(4, a.Given(SupportKind.Draws), "Given(Draws)");
        Check.True(s.Get(2) == null && s.Get(3) == null, "receiving isn't counted");
    }

    [Test]
    public static void HelpingYourselfOrGivingNothingIsntSupport()
    {
        var s = new RunStats();
        s.RecordSupport(1, 1, SupportKind.Energy, 5);
        s.RecordSupport(1, 2, SupportKind.Block, 0);
        s.RecordSupport(1, 2, SupportKind.Block, -3);
        Check.True(s.Get(1) == null, "nothing recorded");
    }

    [Test]
    public static void SupportSurvivesASaveAndLoad()
    {
        string path = Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "current_run.dat");
        var s = new RunStats { RunKey = "SEED:1" };
        s.RecordSupport(1, 2, SupportKind.Block, 12);
        s.RecordSupport(1, 2, SupportKind.Draws, 2);
        RunStatsStore.Save(s, path);
        PlayerTotals loaded = RunStatsStore.LoadIfResumable(path, "SEED:1")!.Get(1)!;
        Check.Equal(12, loaded.BlockGiven, "block");
        Check.Equal(2, loaded.CardsDrawnForTeam, "draws");
    }

    [Test]
    public static void StatsSavedBeforeSupportLoadWithNothingGiven()
    {
        string path = Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "current_run.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // What 1.1.0 wrote: no support fields at all.
        File.WriteAllText(path, "{ \"RunKey\": \"SEED:1\", \"Players\": { \"1\": { \"DamageDealt\": 9 } } }");
        PlayerTotals loaded = RunStatsStore.LoadIfResumable(path, "SEED:1")!.Get(1)!;
        Check.Equal(9, loaded.DamageDealt, "old stats kept");
        Check.Equal(0, loaded.EnergyGiven + loaded.CardsGiven + loaded.BlockGiven + loaded.BuffsGiven + loaded.CardsDrawnForTeam,
            "support starts at nothing");
    }
}
