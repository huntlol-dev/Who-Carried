using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class DebuffBuilderTests
{
    private static readonly PlayerInfo Alice = new(1, "Alice", "Ironclad", "d85a30", "IRONCLAD");
    private static readonly PlayerInfo Bob = new(2, "Bob", "The Tailor", "7f77dd");

    private static SourceRef D(string id) => new(SourceKind.Power, id, id.ToLowerInvariant());

    [Test]
    public static void DebuffsAreSortedByTotalWithABarPerPlayerWhoUsedThem()
    {
        var s = new RunStats();
        s.RecordDebuffApplied(1, D("VULN"), 5);
        s.RecordDebuffApplied(2, D("VULN"), 3);
        s.RecordDebuffApplied(2, D("WEAK"), 10);
        s.RecordDebuffApplied(1, D("POISON"), 2);
        DebuffsView v = DebuffBuilder.Build(s, new[] { Alice, Bob });
        Check.Equal("weak,vuln,poison", string.Join(",", v.Applied.Select(g => g.Label)), "order by total");
        DebuffGroup vuln = v.Applied[1];
        Check.Equal(8, vuln.Total, "vuln total");
        Check.Equal(2, vuln.Bars.Count, "both used vuln");
        Check.Equal(5, vuln.Bars[0].Amount, "alice vuln");
        Check.Near(1.0, vuln.Bars[0].Fraction, "leader is full width");
        Check.Near(0.6, vuln.Bars[1].Fraction, "bob relative to leader");
        Check.Equal("Bob", v.Applied[0].Bars.Single().PlayerLabel, "only bob applied weak, so only bob is listed");
        Check.Equal("power:VULN", vuln.IconKey, "icon key from power id");
        Check.Equal("IRONCLAD", vuln.Bars[0].IconKey, "bar carries character icon");
    }

    [Test]
    public static void EveryDebuffIsShown()
    {
        var s = new RunStats();
        for (int i = 1; i <= 8; i++) s.RecordDebuffApplied(1, D($"D{i}"), i);
        DebuffsView v = DebuffBuilder.Build(s, new[] { Alice });
        Check.Equal(8, v.Applied.Count, "all eight");
        Check.Equal(0, v.MoreApplied, "none hidden");
        Check.Equal("d8", v.Applied[0].Label, "biggest first");
    }

    [Test]
    public static void ADebuffThatOnlyPreventedDamageStillShows()
    {
        var s = new RunStats();
        s.RecordDebuffPrevented(2, D("WAIL"), 30);
        DebuffGroup wail = DebuffBuilder.Build(s, new[] { Alice, Bob }).Applied.Single();
        Check.Equal(30, wail.PreventedTotal, "prevented");
        Check.Equal("Bob", wail.Bars.Single().PlayerLabel, "bob's bar");
    }

    [Test]
    public static void CostsListWhatEnemyDebuffsCostEachPlayer()
    {
        var s = new RunStats();
        s.RecordDebuffCost(1, D("VULN"), RunStats.CostTaken, 40);
        s.RecordDebuffCost(1, D("VULN"), RunStats.CostTaken, 4);
        s.RecordDebuffCost(1, D("WEAK"), RunStats.CostDealt, 60);
        s.RecordDebuffCost(1, D("FRAIL"), RunStats.CostBlock, 0);
        DebuffsView v = DebuffBuilder.Build(s, new[] { Alice, Bob });
        Check.Equal("weak:dealt:60,vuln:taken:44", string.Join(",", v.Costs[0].Lines.Select(l => $"{l.Debuff}:{l.Effect}:{l.Amount}")), "alice");
        Check.Equal(0, v.Costs[1].Lines.Count, "bob paid nothing");
    }

    [Test]
    public static void StrengthLossNetsOutAndDisappearsAtZero()
    {
        var s = new RunStats();
        SourceRef loss = D("STRENGTH_LOSS");
        s.AdjustDebuffApplied(1, loss, 6);
        s.AdjustDebuffApplied(1, loss, -6);
        Check.True(!s.Get(1)!.DebuffsApplied.ContainsKey(loss.Key), "temporary loss netted out");
        s.AdjustDebuffApplied(1, loss, 3);
        Check.Equal(3, s.Get(1)!.DebuffsApplied[loss.Key].Amount, "permanent loss stays");
    }

    [Test]
    public static void ReceivedShowsTotalAndMostCommon()
    {
        var s = new RunStats();
        s.RecordDebuffReceived(1, D("WEAK"), 4);
        s.RecordDebuffReceived(1, D("FRAIL"), 6);
        s.RecordDebuffReceived(1, D("VULN"), 2);
        s.RecordDebuffReceived(1, D("POISON"), 1);
        DebuffsView v = DebuffBuilder.Build(s, new[] { Alice, Bob });
        Check.Equal(13, v.Received[0].Total, "alice total");
        Check.Equal("frail 6 · weak 4 · vuln 2", v.Received[0].Top, "top three");
        Check.Equal(0, v.Received[1].Total, "bob received nothing");
        Check.Equal("", v.Received[1].Top, "bob has no top list");
    }

    [Test]
    public static void NoDebuffsGivesAnEmptyView()
    {
        DebuffsView v = DebuffBuilder.Build(new RunStats(), new[] { Alice, Bob });
        Check.Equal(0, v.Applied.Count, "nothing applied");
        Check.Equal(0, v.MoreApplied, "nothing more");
        Check.Equal(2, v.Received.Count, "a received row per player");
    }

    [Test]
    public static void RecordingIgnoresZeroAndKeepsDamageSeparate()
    {
        var s = new RunStats();
        s.RecordDebuffApplied(1, D("WEAK"), 0);
        s.RecordDebuffApplied(1, D("WEAK"), -2);
        s.RecordDebuffApplied(1, D("WEAK"), 3);
        Check.Equal(3, s.Get(1)!.DebuffsApplied["Power:WEAK"].Amount, "only positive stacks count");
        Check.Equal(0, s.Get(1)!.DamageDealt, "debuffs are not damage");
        Check.Equal(0, s.Get(1)!.Sources.Count, "debuffs are not damage sources");
    }

    [Test]
    public static void RecapViewCarriesDebuffsInDamageOrder()
    {
        var s = new RunStats();
        s.RecordDamage(2, new SourceRef(SourceKind.Card, "X", "x"), 10);
        s.RecordDebuffApplied(1, D("WEAK"), 1);
        s.RecordDebuffApplied(2, D("WEAK"), 1);
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>(), "h");
        Check.Equal("Bob", v.Debuffs.Applied[0].Bars[0].PlayerLabel, "bob (more damage) first, like Overview");
        Check.Equal("Bob", v.Debuffs.Received[0].Label, "received rows in the same order");
        Check.Equal("Bob", v.Debuffs.Costs[0].Label, "cost rows in the same order");
    }

    [Test]
    public static void DebuffsSurviveSaveAndResume()
    {
        string path = Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "current_run.dat");
        var s = new RunStats { RunKey = "K" };
        s.RecordDebuffApplied(1, D("WEAK"), 3);
        s.RecordDebuffReceived(1, D("FRAIL"), 2);
        RunStatsStore.Save(s, path);
        RunStats? loaded = RunStatsStore.Load(path);
        Check.Equal(3, loaded!.Get(1)!.DebuffsApplied["Power:WEAK"].Amount, "applied restored");
        Check.Equal(2, loaded.Get(1)!.DebuffsReceived["Power:FRAIL"].Amount, "received restored");
    }
}
