using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class RecapBuilderTests
{
    private static readonly PlayerInfo Alice = new(1, "Alice", "Ironclad", "d85a30");
    private static readonly PlayerInfo Bob = new(2, "Bob", "The Tailor", "7f77dd");
    private static readonly IReadOnlyDictionary<ulong, DefenseTotals> NoDefense = new Dictionary<ulong, DefenseTotals>();

    private static SourceRef Card(string id) => new(SourceKind.Card, id, id.ToLowerInvariant());

    [Test]
    public static void OverviewIsSortedWithSharesOverRealPlayers()
    {
        var s = new RunStats();
        s.RecordDamage(1, Card("A"), 30);
        s.RecordDamage(2, Card("A"), 70);
        s.RecordDamage(null, Card("A"), 10);
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, NoDefense, "h");
        Check.Equal("Bob", v.Overview[0].Label, "top row");
        Check.Equal("The Tailor", v.Overview[0].SubLabel, "character sub-label");
        Check.Near(0.7, v.Overview[0].Share!.Value, "bob share");
        Check.Near(0.3, v.Overview[1].Share!.Value, "alice share");
        Check.Near(1.0, v.Overview[0].Fraction, "bob fraction is the max");
        Check.Equal(RecapBuilder.UnattributedLabel, v.Overview[2].Label, "unattributed last");
        Check.True(v.Overview[2].Share == null, "unattributed has no share");
        Check.Equal(RecapBuilder.UnattributedLabel, v.Sources[2].PlayerLabel, "unattributed sources view");
    }

    [Test]
    public static void UnattributedOnlyAppearsWhenNonZero()
    {
        var s = new RunStats();
        s.RecordDamage(1, Card("A"), 5);
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, NoDefense, "h");
        Check.Equal(2, v.Overview.Count, "overview rows");
        Check.Equal(2, v.Sources.Count, "source views");
    }

    [Test]
    public static void SourcesShowTopSixPlusOther()
    {
        var s = new RunStats();
        for (int i = 1; i <= 9; i++) s.RecordDamage(1, Card($"C{i}"), i * 10);
        RecapView v = RecapBuilder.Build(s, new[] { Alice }, NoDefense, "h");
        IReadOnlyList<BarRow> rows = v.Sources[0].Rows;
        Check.Equal(7, rows.Count, "6 + other");
        Check.Equal("c9", rows[0].Label, "biggest first");
        Check.Equal("Other (3)", rows[6].Label, "other label");
        Check.Equal(60, rows[6].Value, "other = 30 + 20 + 10");
        Check.Equal(RecapBuilder.GreyHex, rows[6].ColorHex, "other is grey");
        Check.True(rows.All(r => r.Fraction <= 1.0), "fractions capped at 1");
        Check.Equal("Alice · Ironclad", v.Sources[0].PlayerLabel, "player label");
    }

    [Test]
    public static void TimelineHasOneValuePerFightAndMarksActStarts()
    {
        var s = new RunStats();
        s.BeginFight(1, 1, "f1"); s.RecordDamage(1, Card("A"), 10); s.EndFight();
        s.BeginFight(1, 2, "f2"); s.RecordDamage(2, Card("A"), 20); s.EndFight();
        s.BeginFight(2, 18, "f3"); s.RecordDamage(1, Card("A"), 30); s.EndFight();
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, NoDefense, "h");
        Check.Equal("10,0,30", string.Join(",", v.Timeline[0].Values), "alice series");
        Check.Equal("0,20,0", string.Join(",", v.Timeline[1].Values), "bob series");
        Check.Equal("1,1,2", string.Join(",", v.FightActs), "acts per fight");
        Check.Equal("2", string.Join(",", v.ActStarts), "act 2 starts at fight index 2");
        Check.Equal(3, v.FightPoints.Count, "one point per fight");
        Check.Equal(new FightPoint(2, 18, "f3"), v.FightPoints[2], "point carries act, floor and label");
    }

    [Test]
    public static void DefenseCombinesGameTotalsWithTrackedBlock()
    {
        var s = new RunStats();
        s.RecordBlocked(1, 40);
        var defense = new Dictionary<ulong, DefenseTotals> { [1] = new(Taken: 12, Healed: 6) };
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, defense, "h");
        Check.Equal(new DefenseRow("Alice", "d85a30", 12, 40, 6, Character: Alice.Character), v.Defense[0], "alice");
        Check.Equal(new DefenseRow("Bob", "7f77dd", 0, 0, 0, Character: Bob.Character), v.Defense[1], "bob defaults to zero");
    }

    [Test]
    public static void DefenseShowsWhatEachPlayersPetTankedApartFromTheirOwnDamageTaken()
    {
        // A 12 hit: 2 into Alice's block, 7 into Osty, the last 3 spill over to Alice.
        var s = new RunStats();
        s.RecordBlocked(1, 2);
        s.RecordPetTanked(1, 7);
        var defense = new Dictionary<ulong, DefenseTotals> { [1] = new(Taken: 3, Healed: 0) };
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, defense, "h");
        DefenseRow alice = v.Defense.Single(r => r.Label == "Alice");
        Check.Equal(7, alice.PetTanked, "Osty's 7");
        Check.Equal(3, alice.Taken, "only the spill-over is Alice's own");
        Check.Equal(2, alice.Blocked, "block counted once");
        Check.Equal(0, v.Defense.Single(r => r.Label == "Bob").PetTanked, "bob has no pet");
    }

    [Test]
    public static void EmptyRunDoesNotDivideByZero()
    {
        RecapView v = RecapBuilder.Build(new RunStats(), new[] { Alice, Bob }, NoDefense, "h");
        Check.True(v.Overview.All(r => r.Share == 0 && r.Fraction == 0), "all zero");
        Check.Equal(0, v.FightActs.Count, "no fights");
        Check.Equal("h", v.Header, "header passed through");
        Check.Equal(new Highlight("Team damage", "0", "0 fights"), v.Highlights[0], "team highlight");
        Check.Equal(RecapBuilder.NoValue, v.Highlights[1].Value, "no top source");
        Check.Equal(RecapBuilder.NoValue, v.Highlights[2].Value, "no biggest fight");
        Check.True(v.Victory == null, "in progress by default");
    }

    [Test]
    public static void IconKeysColorsAndResultFlowThrough()
    {
        var carol = new PlayerInfo(3, "Carol", "The Silent", "7fff00", "SILENT");
        var s = new RunStats();
        s.BeginFight(1, 1, "f1"); s.RecordDamage(3, Card("A"), 5); s.EndFight();
        s.RecordBlocked(3, 2);
        RecapView v = RecapBuilder.Build(s, new[] { carol }, NoDefense, "h", victory: true);
        Check.Equal("SILENT", v.Overview[0].IconKey, "overview icon");
        Check.Equal("SILENT", v.Sources[0].IconKey, "sources icon");
        Check.Equal("7fff00", v.Sources[0].ColorHex, "sources colour");
        Check.Equal("SILENT", v.Timeline[0].IconKey, "timeline icon");
        Check.Equal("SILENT", v.Defense[0].IconKey, "defense icon");
        Check.True(v.Sources[0].Rows[0].IconKey == null, "source rows carry no icon");
        Check.Equal<bool?>(true, v.Victory, "victory");
    }

    [Test]
    public static void HighlightsSummariseTheRun()
    {
        var s = new RunStats();
        s.BeginFight(1, 3, "Jaw Worm");
        s.RecordDamage(1, Card("BASH"), 30);
        s.RecordDamage(2, Card("ZAP"), 10);
        s.EndFight();
        s.BeginFight(2, 20, "Hexaghost");
        s.RecordDamage(2, Card("ZAP"), 70);
        s.EndFight();
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, NoDefense, "h");
        Check.Equal(new Highlight("Team damage", "110", "2 fights"), v.Highlights[0], "team");
        Check.Equal(new Highlight("Top source", "zap", "80 · Bob"), v.Highlights[1], "top source");
        Check.Equal(new Highlight("Biggest fight", "70", "Hexaghost · Act 2"), v.Highlights[2], "biggest fight");
    }

    [Test]
    public static void PlayersSharingAColourAreShadedInEveryView()
    {
        var alice = new PlayerInfo(1, "Alice", "Ironclad", "d85a30", "IRONCLAD");
        var bob = new PlayerInfo(2, "Bob", "Ironclad", "d85a30", "IRONCLAD");
        var s = new RunStats();
        s.BeginFight(1, 1, "f1"); s.RecordDamage(1, Card("A"), 5); s.RecordDamage(2, Card("A"), 9); s.EndFight();
        RecapView v = RecapBuilder.Build(s, new[] { alice, bob }, NoDefense, "h");
        string bobs = v.Overview.Single(r => r.Label == "Bob").ColorHex;
        Check.True(bobs != "d85a30", "Bob is shaded");
        Check.Equal("d85a30", v.Overview.Single(r => r.Label == "Alice").ColorHex, "Alice, first to join, keeps the colour");
        Check.Equal(bobs, v.Timeline.Single(t => t.Label == "Bob").ColorHex, "timeline");
        Check.Equal(bobs, v.Defense.Single(d => d.Label == "Bob").ColorHex, "defense");
        Check.Equal(bobs, v.Sources.Single(x => x.PlayerLabel.StartsWith("Bob")).ColorHex, "sources");
        Check.True(v.Sources.Single(x => x.PlayerLabel.StartsWith("Bob")).Rows.All(r => r.ColorHex == bobs), "source bars");
        Check.True(v.Awards.Where(a => a.PlayerName == "Bob").All(a => a.ColorHex == bobs), "awards");
    }
}
