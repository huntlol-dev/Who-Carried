using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class CardCreationTests
{
    private static readonly SourceRef Soul = new(SourceKind.Card, "SOUL", "Soul");
    private static readonly PlayerInfo Alice = new(1, "Alice", "Ironclad", "d85a30", "IRONCLAD");
    private static readonly PlayerInfo Bob = new(2, "Bob", "Silent", "7fff00", "SILENT");

    [Test]
    public static void OnlyCardsMadeForTeammatesAreListed()
    {
        var s = new RunStats();
        s.RecordCardGeneration(1, 1, Soul, 5);
        var v = RecapBuilder.Build(s, new[] { Alice }, new Dictionary<ulong, DefenseTotals>(), "h");
        Check.True(!v.HasCardGifts && !v.HasSupport, "solo: nothing given");
        Check.Equal(0, v.CardGifts[0].Cards.Count, "cards made for yourself aren't listed");
        s.RecordCardGeneration(1, 2, Soul, 2);
        v = RecapBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>(), "h");
        Check.True(v.HasCardGifts && v.HasSupport, "a gift is support");
        Check.Equal("Card:SOUL:2", string.Join(",", v.CardGifts[0].Cards.Select(c => $"{c.Key}:{c.Given}")), "only the two given");
        Check.Equal(0, v.CardGifts[1].Cards.Count, "the recipient gave none");
        var empty = RecapBuilder.Build(new RunStats(), new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>(), "h");
        Check.True(!empty.HasCardGifts && !empty.HasSupport, "empty");
    }

    [Test]
    public static void GiftsKeepStableKeysAndOldSavesInventNone()
    {
        var s = new RunStats();
        s.RecordCardGeneration(1, 2, new SourceRef(SourceKind.Card, "B", "Twin"), 3);
        s.RecordCardGeneration(1, 2, new SourceRef(SourceKind.Card, "A", "Twin"), 3);
        s.RecordCardGeneration(1, 2, Soul, 4);
        // Saved before gifts were recorded card by card: a Cards total, but no cards to list.
        s.RecordCardCreated(2, Soul, 9);
        s.RecordSupport(2, 1, SupportKind.Cards, 7);
        s.RecordDamage(2, Soul, 10);
        var v = RecapBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>(), "h");
        Check.Equal((ulong)2, v.CardGifts[0].PlayerId, "scoreboard order");
        Check.Equal("Card:SOUL,Card:A,Card:B", string.Join(",", v.CardGifts[1].Cards.Select(c => c.Key)), "most first, then keys");
        Check.Equal(0, v.CardGifts[0].Cards.Count, "no invented gifts");
        Check.Equal(7, v.Support[0].Cards, "the old total stays");
    }

    [Test]
    public static void ReactionCreditRequiresEvidenceAndPreservesExplicitGifts()
    {
        Check.Equal((ulong?)1, CardCreationCredit.Resolve(2, 2, true, 1), "reaction applier");
        Check.Equal((ulong?)3, CardCreationCredit.Resolve(3, 2, true, 1), "direct gift wins");
        Check.Equal((ulong?)null, CardCreationCredit.Resolve(2, 2, true, null), "unowned reaction");
        Check.Equal((ulong?)2, CardCreationCredit.Resolve(2, 2, false, null), "self creation");
        Check.Equal((ulong?)null, CardCreationCredit.Resolve(null, 2, false, null), "enemy status");
        Check.Equal((ulong?)1, CardCreationCredit.Resolve(null, 2, true, 1), "known reaction");
    }

    [Test]
    public static void GiftsAreASubsetOfCreationAndCountOnce()
    {
        var s = new RunStats();
        s.RecordCardGeneration(1, 1, Soul, 2);
        s.RecordCardGeneration(1, 2, Soul, 2);
        Check.Equal(4, s.Get(1)!.CardsCreated[Soul.Key].Amount, "all four");
        Check.Equal(2, s.Get(1)!.CardGifts[Soul.Key].Amount, "two gifts");
        Check.Equal(2, s.Get(1)!.CardsGiven, "aggregate once");
        Check.True(s.Get(2) == null, "no recipient credit");
        Check.Equal(0, s.Get(1)!.DamageDealt, "not damage");
    }

    [Test]
    public static void UnknownRecipientsAndNonPositiveAmountsDontCreateGifts()
    {
        var s = new RunStats();
        s.RecordCardGeneration(1, 2, Soul, 0);
        s.RecordCardGeneration(1, 2, Soul, -2);
        Check.True(s.Get(1) == null, "no events");
        s.RecordCardGeneration(1, null, Soul);
        s.RecordCardGeneration(1, 1, Soul);
        Check.Equal(2, s.Get(1)!.CardsCreated[Soul.Key].Amount, "creation known");
        Check.Equal(0, s.Get(1)!.CardsGiven, "no self or unknown gift");
        Check.Equal(0, s.Get(1)!.CardGifts.Count, "no details");
    }

    [Test]
    public static void LegacySaveKeepsTotalsAndNewGiftsRoundTrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "current_run.dat");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(path, """
                {"RunKey":"SEED:1","Players":{"1":{"CardsGiven":7,"CardsCreated":{"Card:SOUL":{"Kind":"Card","Label":"Soul","Amount":9}}}}}
                """);
            var s = RunStatsStore.LoadIfResumable(path, "SEED:1")!;
            Check.Equal(0, s.Get(1)!.CardGifts.Count, "no invented history");
            s.RecordCardGeneration(1, 2, Soul);
            RunStatsStore.Save(s, path);
            var t = RunStatsStore.LoadIfResumable(path, "SEED:1")!.Get(1)!;
            Check.Equal(10, t.CardsCreated[Soul.Key].Amount, "old creation preserved");
            Check.Equal(8, t.CardsGiven, "old aggregate preserved");
            Check.Equal(1, t.CardGifts[Soul.Key].Amount, "new detail only");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public static void SameLabelsAndModdedIdsRemainDistinct()
    {
        var s = new RunStats();
        s.RecordCardGeneration(1, 2, new SourceRef(SourceKind.Card, "MOD-ONE", "Twin"));
        s.RecordCardGeneration(1, 2, new SourceRef(SourceKind.Card, "MOD-TWO", "Twin"), 2);
        Check.Equal(2, s.Get(1)!.CardsCreated.Count, "two types");
        Check.Equal(2, s.Get(1)!.CardGifts["Card:MOD-TWO"].Amount, "mod gift");
    }

    [Test]
    public static void CreationAndGiftAwardsKeepTheirThresholds()
    {
        var s = new RunStats();
        s.RecordCardGeneration(1, 1, Soul, AwardBuilder.MinCardsCreated);
        RecapView solo = RecapBuilder.Build(s, new[] { Alice }, new Dictionary<ulong, DefenseTotals>(), "h");
        Check.True(!solo.Awards.Any(a => a.Title == AwardBuilder.CardFactory), "factory remains co-op only");
        Check.True(!solo.Awards.Any(a => a.Title == AwardBuilder.CarePackage), "no solo gift award");
        s.RecordCardGeneration(1, 2, Soul, 2);
        RecapView pair = RecapBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>(), "h");
        Check.True(pair.Awards.Any(a => a.Title == AwardBuilder.CardFactory), "factory");
        Check.True(!pair.Awards.Any(a => a.Title == AwardBuilder.CarePackage), "two below threshold");
        s.RecordCardGeneration(1, 2, Soul);
        pair = RecapBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>(), "h");
        Check.Equal("Alice", pair.Awards.Single(a => a.Title == AwardBuilder.CarePackage).PlayerName, "giver wins");
    }
}
