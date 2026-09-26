using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>"Damage dealt" is HP removed; enemy block knocked off is kept as its own number alongside.</summary>
public static class DamageDealtTests
{
    private static readonly PlayerInfo Alice = new(1, "Alice", "Ironclad", "d85a30");
    private static readonly PlayerInfo Bob = new(2, "Bob", "The Silent", "7fff00");
    private static readonly SourceRef Bash = new(SourceKind.Card, "BASH", "Bash");
    private static readonly SourceRef Shiv = new(SourceKind.Card, "SHIV", "Shiv");

    [Test]
    public static void BlockRemovedIsCountedSeparately()
    {
        var s = new RunStats();
        s.BeginFight(1, 2, "Jaw Worm");
        s.RecordDamage(1, Bash, 8, blocked: 4);
        s.RecordDamage(1, Bash, 0, blocked: 5);
        s.EndFight();
        PlayerTotals a = s.Get(1)!;
        Check.Equal(8, a.DamageDealt, "damage dealt is HP only");
        Check.Equal(9, a.BlockRemoved, "block removed");
        Check.Equal(8, a.Sources["Card:BASH"].Amount, "source damage");
        Check.Equal(9, a.Sources["Card:BASH"].BlockRemoved, "source block removed");
        Check.Equal(8, s.Fights[0].DamageByPlayer["1"], "fight counts HP only");
    }

    [Test]
    public static void OverviewAndSourcesRankByDamageWithBlockAlongside()
    {
        var s = new RunStats();
        s.RecordDamage(1, Bash, 60, blocked: 40);
        s.RecordDamage(2, Shiv, 90, blocked: 0);
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>(), "h");
        BarRow bob = v.Overview[0];
        Check.Equal("Bob", bob.Label, "90 beats 60; block doesn't count");
        BarRow alice = v.Overview[1];
        Check.Equal(60, alice.Value, "damage dealt");
        Check.Equal(40, alice.BlockRemoved, "block removed");
        Check.Near(60.0 / 150, alice.Share!.Value, "share of damage dealt");
        Check.Equal(40, v.Sources.Single(x => x.PlayerLabel.StartsWith("Alice", StringComparison.Ordinal)).Rows[0].BlockRemoved,
            "source block removed");
        Check.Equal("150", v.Highlights[0].Value, "team damage");
    }

    [Test]
    public static void CreatedCardsFlowIntoSupportCreation()
    {
        var s = new RunStats();
        s.RecordCardCreated(2, new SourceRef(SourceKind.Card, "SHIV", "Shiv"), 3);
        s.RecordCardCreated(2, new SourceRef(SourceKind.Card, "SOUL", "Soul"), 5);
        s.RecordCardCreated(2, new SourceRef(SourceKind.Card, "SHIV", "Shiv"));
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>(), "h");
        CreationRow bob = v.Creation.Single(x => x.PlayerId == Bob.NetId);
        Check.Equal("Soul:5,Shiv:4", string.Join(",", bob.Cards.Select(c => $"{c.Label}:{c.Created}")), "most first");
        Check.Equal(0, v.Creation.Single(x => x.PlayerId == Alice.NetId).Cards.Count, "alice made none");
    }

    [Test]
    public static void PetAttacksCountOnTheCardThatTriggeredThem()
    {
        var s = new RunStats();
        var unleash = new SourceRef(SourceKind.Card, "UNLEASH", "Unleash");
        s.RecordDamage(1, Attribution.PetVia(new SourceRef(SourceKind.Pet, "OSTY", "Osty"), unleash), 30, blocked: 5);
        s.RecordDamage(1, unleash, 4);
        var decks = new Dictionary<ulong, IReadOnlyList<DeckCard>> { [1] = new[] { new DeckCard("UNLEASH", "Unleash", "Attack", "Common", 0) } };
        DeckView deck = DeckBuilder.Build(s, new[] { Alice }, decks)[0];
        Check.Equal(34, deck.Entries.Single().Damage, "own 4 + Osty's 30");
    }
}
