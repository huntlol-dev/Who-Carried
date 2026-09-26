using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class CardGenerationReplayTests
{
    private static readonly string[] Headers = {
        "Who Carried v1.2.0 - run SEED:1 started 2026-09-26 12:00",
        "player 1 = Alice (Ironclad) #ff0000", "player 2 = Bob (Silent) #00ff00",
    };
    private static LogReplay.Result Replay(params string[] lines) => LogReplay.Parse(Headers.Concat(lines.Select(l => "[F2 A1] " + l)));

    [Test]
    public static void NewCreationAndLegacyGiftsReplayOnceEach()
    {
        var e = new CardGenerationEvent { Version = 1, Contributor = 1, Recipient = 2, Id = "SOUL", Label = "Soul", Count = 1, Effect = "SOULBOUND_POWER" };
        var r = Replay(LogReplay.CardGenerationLine(e), "Alice gave 3 cards to Bob | OLD");
        var t = r.Stats.Get(1)!;
        Check.Equal(1, t.CardsCreated["Card:SOUL"].Amount, "created");
        Check.Equal(1, t.CardGifts["Card:SOUL"].Amount, "recorded gifts");
        Check.Equal(4, t.CardsGiven, "legacy plus new");
    }

    [Test]
    public static void LabelsEscapeOnOneLineAndIdsDisambiguatePlayers()
    {
        const string label = "灵魂 | \"Soul\"\nline";
        string line = LogReplay.CardGenerationLine(new CardGenerationEvent { Version = 1, Contributor = 2, Recipient = 1, Id = "MOD-CARD", Label = label, Count = 2 });
        Check.True(!line.Contains('\n') && !line.Contains('\r'), "one log line");
        var r = LogReplay.Parse(new[] { Headers[0], Headers[1], "player 2 = Alice (Silent) #00ff00", "[F2 A1] " + line });
        Check.Equal(label, r.Stats.Get(2)!.CardsCreated["Card:MOD-CARD"].Label, "exact label");
        Check.Equal(2, r.Stats.Get(2)!.CardsGiven, "ID chooses giver");
        Check.True(r.Stats.Get(1) == null, "not first named Alice");
    }

    [Test]
    public static void BadAndFutureEventsAreIgnored()
    {
        string[] invalid = {
            "null", "{", "[]", "{}",
            "{\"v\":2,\"contributor\":1,\"id\":\"SOUL\",\"count\":1}",
            "{\"v\":1,\"contributor\":99,\"id\":\"SOUL\",\"count\":1}",
            "{\"v\":1,\"contributor\":1,\"recipient\":99,\"id\":\"SOUL\",\"count\":1}",
            "{\"v\":1,\"contributor\":1,\"id\":\"\",\"count\":1}",
            "{\"v\":1,\"contributor\":1,\"id\":\"SOUL\",\"count\":0}",
            "{\"v\":1,\"contributor\":1,\"id\":\"SOUL\",\"count\":-1}",
        };
        var r = Replay(invalid.Select(x => "card-generation " + x).ToArray());
        Check.Equal(0, r.Stats.Players.Count, "no invalid events counted");
        Check.Equal(0, Replay().Stats.Players.Count, "old logs invent nothing");
    }

    [Test]
    public static void SelfAndUnknownRecipientCreateWithoutGiftAndLabelFallsBack()
    {
        var r = Replay(
            "card-generation {\"v\":1,\"contributor\":1,\"recipient\":null,\"id\":\"SOUL\",\"count\":2}",
            "card-generation {\"v\":1,\"contributor\":1,\"recipient\":1,\"id\":\"SOUL\",\"label\":null,\"count\":1}",
            "card-generation-unresolved creator=2 recipient=2 card=SOUL");
        Check.Equal(3, r.Stats.Get(1)!.CardsCreated["Card:SOUL"].Amount, "three created");
        Check.Equal("SOUL", r.Stats.Get(1)!.CardsCreated["Card:SOUL"].Label, "fallback");
        Check.Equal(0, r.Stats.Get(1)!.CardsGiven, "no gifts");
    }
}
