using System.Text.Json;

namespace WhoCarried.Core;

/// <summary>
/// The parts of the game's own saved run (saves/history/&lt;start time&gt;.run) the recap needs: result, floors, and
/// per player the character, final deck, damage taken, HP healed, lowest HP after a floor, gold earned, and badges.
/// </summary>
/// <param name="Rooms">Each floor's map room ("monster", "elite", "boss", "unknown", "shop"…), by floor number.</param>
public sealed record RunHistory(bool Win, int Floors, long StartTime, IReadOnlyList<RunHistory.PlayerRecord> Players,
                                IReadOnlyDictionary<int, string>? Rooms = null, long RunTime = 0, int Ascension = 0,
                                string Seed = "")
{
    /// <param name="CharacterId">"CHARACTER.NECROBINDER" style id.</param>
    /// <param name="Deck">Card ids ("CARD.STRIKE_SILENT") with their upgrade level.</param>
    /// <param name="LowestHp">The lowest HP (as a share of max HP) the player ended a floor on and lived; 0 = none.</param>
    /// <param name="Gold">Gold earned: the sum of every floor's gold_gained.</param>
    public sealed record PlayerRecord(ulong Id, string CharacterId, IReadOnlyList<(string CardId, int Upgrades)> Deck, int Taken,
                                      int Healed, int LowestHp = 0, int LowestHpMax = 0, IReadOnlyList<EarnedBadge>? Badges = null,
                                      int Gold = 0);

    public static RunHistory Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        bool win = root.TryGetProperty("win", out JsonElement w) && w.ValueKind == JsonValueKind.True;
        long start = root.TryGetProperty("start_time", out JsonElement s) && s.TryGetInt64(out long st) ? st : 0;

        var taken = new Dictionary<ulong, int>();
        var healed = new Dictionary<ulong, int>();
        var gold = new Dictionary<ulong, int>();
        var lows = new FloorLows();
        var rooms = new Dictionary<int, string>();
        int floors = 0;
        bool first = true;
        if (root.TryGetProperty("map_point_history", out JsonElement acts))
        {
            foreach (JsonElement act in acts.EnumerateArray())
            foreach (JsonElement point in act.EnumerateArray())
            {
                floors++;
                if (point.TryGetProperty("map_point_type", out JsonElement type) && type.ValueKind == JsonValueKind.String)
                    rooms[floors] = type.GetString() ?? "";
                if (point.TryGetProperty("player_stats", out JsonElement stats))
                {
                    foreach (JsonElement p in stats.EnumerateArray())
                    {
                        ulong id = p.GetProperty("player_id").GetUInt64();
                        taken[id] = taken.GetValueOrDefault(id) + Int(p, "damage_taken");
                        gold[id] = gold.GetValueOrDefault(id) + Int(p, "gold_gained");
                        // The run's first point records the starting HP as "healed" (0 -> start HP); that isn't healing.
                        if (!first) healed[id] = healed.GetValueOrDefault(id) + Int(p, "hp_healed");
                        lows.Add(id, Int(p, "current_hp"), Int(p, "max_hp"), Int(p, "damage_taken"));
                    }
                }
                first = false;
            }
        }

        var players = new List<PlayerRecord>();
        if (root.TryGetProperty("players", out JsonElement list))
        {
            foreach (JsonElement p in list.EnumerateArray())
            {
                ulong id = p.GetProperty("id").GetUInt64();
                var deck = new List<(string, int)>();
                if (p.TryGetProperty("deck", out JsonElement cards))
                    foreach (JsonElement c in cards.EnumerateArray())
                        deck.Add((c.GetProperty("id").GetString() ?? "", Int(c, "current_upgrade_level")));
                var badges = new List<EarnedBadge>();
                if (p.TryGetProperty("badges", out JsonElement earned) && earned.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement b in earned.EnumerateArray())
                        badges.Add(new EarnedBadge
                        {
                            Id = b.TryGetProperty("id", out JsonElement bid) ? bid.GetString() ?? "" : "",
                            Rarity = b.TryGetProperty("rarity", out JsonElement r) ? (r.GetString() ?? "").ToLowerInvariant() : "",
                        });
                (int lowHp, int lowMax) = lows.Get(id);
                players.Add(new PlayerRecord(id, p.TryGetProperty("character", out JsonElement ch) ? ch.GetString() ?? "" : "",
                    deck, taken.GetValueOrDefault(id), healed.GetValueOrDefault(id), lowHp, lowMax,
                    badges.Where(b => b.Id.Length > 0).ToList(), gold.GetValueOrDefault(id)));
            }
        }
        long runTime = root.TryGetProperty("run_time", out JsonElement rt) && rt.TryGetInt64(out long t) ? t : 0;
        string seed = root.TryGetProperty("seed", out JsonElement sd) && sd.ValueKind == JsonValueKind.String ? sd.GetString() ?? "" : "";
        return new RunHistory(win, floors, start, players, rooms, runTime, Int(root, "ascension"), seed);
    }

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.TryGetInt32(out int i) ? i : 0;
}
