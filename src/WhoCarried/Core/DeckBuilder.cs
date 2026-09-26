namespace WhoCarried.Core;

/// <summary>One card in a player's deck, as plain facts.</summary>
public sealed record DeckCard(string Id, string Label, string Type, string Rarity, int UpgradeLevel);

/// <summary>All copies of one card id in a deck, with the damage that card id dealt this run.</summary>
public sealed record DeckEntry(string Id, string Label, string Type, string Rarity, int Count, int UpgradedCount, int Damage);

/// <param name="Gold">Gold the player earned over the run (see <see cref="DefenseTotals.Gold"/>); 0 = none or unknown.</param>
public sealed record DeckView(ulong PlayerId, string PlayerLabel, string ColorHex, string? IconKey, int CardCount,
                              IReadOnlyList<DeckEntry> Entries, int Gold = 0);

public static class DeckBuilder
{
    /// <param name="history">The game's per-floor totals, for each player's gold; null when there are none.</param>
    public static IReadOnlyList<DeckView> Build(RunStats stats, IEnumerable<PlayerInfo> players,
                                                IReadOnlyDictionary<ulong, IReadOnlyList<DeckCard>>? decks,
                                                IReadOnlyDictionary<ulong, DefenseTotals>? history = null)
    {
        return players.Select(p =>
        {
            IReadOnlyList<DeckCard> cards = decks != null && decks.TryGetValue(p.NetId, out IReadOnlyList<DeckCard>? deck)
                ? deck
                : Array.Empty<DeckCard>();
            PlayerTotals? totals = stats.Get(p.NetId);
            List<DeckEntry> entries = cards
                .GroupBy(c => c.Id)
                .Select(g =>
                {
                    DeckCard first = g.First();
                    int damage = CardDamage(totals, g.Key);
                    return new DeckEntry(g.Key, first.Label, first.Type, first.Rarity, g.Count(),
                        g.Count(c => c.UpgradeLevel > 0), damage);
                })
                .OrderBy(e => TypeOrder(e.Type))
                .ThenByDescending(e => e.Damage)
                .ThenBy(e => e.Label, StringComparer.Ordinal)
                .ToList();
            return new DeckView(p.NetId, $"{p.Name} · {p.Character}", p.ColorHex,
                string.IsNullOrEmpty(p.CharacterId) ? null : p.CharacterId, cards.Count, entries,
                history?.GetValueOrDefault(p.NetId)?.Gold ?? 0);
        }).ToList();
    }

    public static int TypeOrder(string type) => type switch { "Attack" => 0, "Skill" => 1, "Power" => 2, _ => 3 };

    /// <summary>Damage dealt by a card id: its own hits plus any pet attacks it triggered ("Osty via Unleash").</summary>
    private static int CardDamage(PlayerTotals? totals, string cardId)
    {
        if (totals == null) return 0;
        string own = new SourceRef(SourceKind.Card, cardId, "").Key;
        string viaSuffix = $"{Attribution.ViaSeparator}{cardId}";
        return totals.Sources
            .Where(kv => kv.Key == own || (kv.Key.StartsWith("Pet:", StringComparison.Ordinal) && kv.Key.EndsWith(viaSuffix, StringComparison.Ordinal)))
            .Sum(kv => kv.Value.Amount);
    }
}
