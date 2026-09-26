using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class GoldTests
{
    private static readonly PlayerInfo Alice = new(1, "Alice", "Ironclad", "d85a30");
    private static readonly PlayerInfo Bob = new(2, "Bob", "The Tailor", "7f77dd");

    [Test]
    public static void RunHistorySumsGoldEarnedPerPlayer()
    {
        const string json = """
        {
          "win": false, "start_time": 1,
          "map_point_history": [
            [ { "player_stats": [ { "player_id": 11, "gold_gained": 0, "current_gold": 99 },
                                  { "player_id": 12, "gold_gained": 0, "current_gold": 99 } ] },
              { "player_stats": [ { "player_id": 11, "gold_gained": 14, "gold_spent": 50 },
                                  { "player_id": 12, "gold_gained": 15 } ] } ],
            [ { "player_stats": [ { "player_id": 11, "gold_gained": 100, "gold_stolen": 20 } ] } ]
          ],
          "players": [ { "id": 11, "character": "CHARACTER.X", "deck": [] },
                       { "id": 12, "character": "CHARACTER.Y", "deck": [] } ]
        }
        """;
        Dictionary<ulong, RunHistory.PlayerRecord> players = RunHistory.Parse(json).Players.ToDictionary(p => p.Id);
        Check.Equal(114, players[11].Gold, "gained across acts; spending and theft don't take away");
        Check.Equal(15, players[12].Gold, "each player's own");
    }

    [Test]
    public static void DecksCarryEachPlayersGold()
    {
        var history = new Dictionary<ulong, DefenseTotals> { [1] = new(0, 0, Gold: 1072) };
        IReadOnlyList<DeckView> decks = DeckBuilder.Build(new RunStats(), new[] { Alice, Bob }, null, history);
        Check.Equal(1072, decks.Single(d => d.PlayerId == 1).Gold, "alice");
        Check.Equal(0, decks.Single(d => d.PlayerId == 2).Gold, "not in the history");
    }

    [Test]
    public static void RecapDecksTakeGoldFromTheHistoryTotals()
    {
        var history = new Dictionary<ulong, DefenseTotals> { [2] = new(10, 0, Gold: 651) };
        RecapView v = RecapBuilder.Build(new RunStats(), new[] { Alice, Bob }, history, "h");
        Check.Equal(651, v.Decks.Single(d => d.PlayerId == 2).Gold, "bob");
        Check.Equal(0, v.Decks.Single(d => d.PlayerId == 1).Gold, "alice");
    }
}
