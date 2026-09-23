using System.Globalization;
using WhoCarried.Localization;

namespace WhoCarried.Core;

/// <param name="Value">Damage dealt (HP removed).</param>
/// <param name="Bonus">Extra damage teammates dealt thanks to this player's debuffs; 0 = none.</param>
/// <param name="BlockRemoved">Enemy block knocked off, shown under the damage; not part of <paramref name="Value"/>.</param>
/// <param name="Award">The player's headline award localization key (scoreboard rows only); "" for none.</param>
/// <param name="Badges">The game's end-of-run badges for this player (scoreboard rows only), best first.</param>
/// <param name="ArtKey">A damage source's picture (see <see cref="SourceArt"/>); null for none.</param>
public sealed record BarRow(string Label, string SubLabel, int Value, double Fraction, double? Share, string ColorHex,
                            string? IconKey = null, int Bonus = 0, int BlockRemoved = 0, string Award = "",
                            IReadOnlyList<BadgeInfo>? Badges = null, string? ArtKey = null)
{
    public IReadOnlyList<BadgeInfo> BadgeList => Badges ?? Array.Empty<BadgeInfo>();
}

/// <summary>A card a player created during fights, and how many.</summary>
public sealed record CreatedCard(string Label, int Count);

/// <summary>A player's damage from one kind of source ("Card", "Orb", "Relic", … "Other"), with its biggest source.</summary>
public sealed record KindTotal(string Kind, int Amount, int Sources, string TopLabel, string? TopArtKey);

public sealed record SourcesView(string PlayerLabel, IReadOnlyList<BarRow> Rows, string ColorHex = RecapBuilder.GreyHex,
                                 string? IconKey = null, IReadOnlyList<CreatedCard>? Created = null,
                                 IReadOnlyList<KindTotal>? Kinds = null)
{
    public IReadOnlyList<CreatedCard> CreatedCards => Created ?? Array.Empty<CreatedCard>();

    /// <summary>Damage by kind of source, biggest first (every source counted, not just the top few).</summary>
    public IReadOnlyList<KindTotal> KindTotals => Kinds ?? Array.Empty<KindTotal>();
}

public sealed record TimelineSeries(string Label, string ColorHex, IReadOnlyList<int> Values, string? IconKey = null);

/// <param name="Prevented">HP this player's Weak (and similar) kept off the team.</param>
/// <param name="LowestHp">The lowest HP they lived through (0 = unknown), with their max HP then.</param>
/// <param name="PetTanked">HP this player's pets (Osty) lost to enemies; not part of <paramref name="Taken"/>.</param>
public sealed record DefenseRow(string Label, string ColorHex, int Taken, int Blocked, int Healed, string? IconKey = null,
                                int Prevented = 0, string Character = "", int LowestHp = 0, int LowestHpMax = 0,
                                int PetTanked = 0);

/// <summary>The run at a glance, for the top bar: floor reached, ascension, play time and seed.</summary>
public sealed record RunFacts(int Floor, int Ascension, long Seconds, string Seed, int Act = 0);

/// <param name="LowestHp">The lowest HP the player ended a floor on (0 = unknown), with their max HP then.</param>
public sealed record DefenseTotals(int Taken, int Healed, int LowestHp = 0, int LowestHpMax = 0);

public sealed record Highlight(string Label, string Value, string Sub);

/// <summary>One fight on the timeline, in run order.</summary>
/// <param name="Room">"monster", "elite", "boss", "unknown" (an event fight), or "" when not known.</param>
public sealed record FightPoint(int Act, int Floor, string Label, string Room = "");

public sealed record RecapView(
    string Header,
    IReadOnlyList<BarRow> Overview,
    IReadOnlyList<SourcesView> Sources,
    IReadOnlyList<TimelineSeries> Timeline,
    IReadOnlyList<int> FightActs,
    IReadOnlyList<int> ActStarts,
    IReadOnlyList<FightPoint> FightPoints,
    IReadOnlyList<DefenseRow> Defense,
    IReadOnlyList<Highlight> Highlights,
    bool? Victory,
    IReadOnlyList<DeckView> Decks,
    DebuffsView Debuffs,
    string BonusNote,
    string PreventedNote,
    IReadOnlyList<Award> Awards,
    IReadOnlyList<PlayerBadges>? Badges,
    RunFacts? Facts = null)
{
    /// <summary>False while the run is still going: the game hands out badges only when it ends.</summary>
    public bool BadgesKnown => Badges != null;
}

/// <summary>Turns counted stats into exactly what the panel and the exported card display. No game or Godot types.</summary>
public static class RecapBuilder
{
    public const int TopSources = 6;
    public const string GreyHex = "8a8a8a";
    public const string UnattributedLabel = "Unattributed";
    public const string OtherPrefix = "Other";
    public const string NoValue = "—";

    /// <param name="badgeText">A badge's name and description from its id and rarity (the game's localized text).</param>
    public static RecapView Build(
        RunStats stats,
        IReadOnlyList<PlayerInfo> players,
        IReadOnlyDictionary<ulong, DefenseTotals> defense,
        string header,
        bool? victory = null,
        IReadOnlyDictionary<ulong, IReadOnlyList<DeckCard>>? decks = null,
        Func<string, string, (string Title, string Description)>? badgeText = null,
        RunFacts? facts = null)
    {
        // Players sharing a colour (two Ironclads) get shades of it; every view below is built from these.
        players = PlayerShades.Assign(players);

        // "Damage dealt" everywhere is HP removed; block knocked off is its own smaller number.
        Dictionary<ulong, int> dealt = players.ToDictionary(p => p.NetId, p => stats.Get(p.NetId)?.DamageDealt ?? 0);
        int team = dealt.Values.Sum();
        int unattributed = stats.Get(null)?.DamageDealt ?? 0;
        int max = Math.Max(dealt.Values.DefaultIfEmpty(0).Max(), unattributed);
        List<PlayerInfo> byDamage = players.OrderByDescending(p => dealt[p.NetId]).ToList();
        IReadOnlyList<Award> awards = AwardBuilder.Build(stats, byDamage, defense);
        Dictionary<ulong, List<BadgeInfo>> badges = players.ToDictionary(p => p.NetId, p => Badges(stats.Get(p.NetId), badgeText));

        var overview = byDamage
            .Select(p => new BarRow(p.Name, p.Character, dealt[p.NetId], Fraction(dealt[p.NetId], max),
                team == 0 ? 0 : (double)dealt[p.NetId] / team, p.ColorHex, IconOf(p),
                stats.Get(p.NetId)?.DebuffBonus.Values.Sum(b => b.Amount) ?? 0,
                stats.Get(p.NetId)?.BlockRemoved ?? 0,
                AwardBuilder.Headline(awards, p.NetId),
                badges[p.NetId]))
            .ToList();
        if (unattributed > 0)
            overview.Add(new BarRow(UnattributedLabel, "", unattributed, Fraction(unattributed, max), null, GreyHex,
                BlockRemoved: stats.Get(null)?.BlockRemoved ?? 0));

        var sources = byDamage
            .Select(p => new SourcesView($"{p.Name} · {p.Character}", SourceRows(stats.Get(p.NetId), p.ColorHex),
                p.ColorHex, IconOf(p), Created(stats.Get(p.NetId)), Kinds(stats.Get(p.NetId))))
            .ToList();
        if (unattributed > 0)
            sources.Add(new SourcesView(UnattributedLabel, SourceRows(stats.Get(null), GreyHex)));

        var timeline = players
            .Select(p => new TimelineSeries(p.Name, p.ColorHex,
                stats.Fights.Select(f => f.DamageByPlayer.GetValueOrDefault(RunStats.KeyFor(p.NetId))).ToList(),
                IconOf(p)))
            .ToList();
        List<int> fightActs = stats.Fights.Select(f => f.Act).ToList();
        List<int> actStarts = Enumerable.Range(1, Math.Max(0, fightActs.Count - 1))
            .Where(i => fightActs[i] != fightActs[i - 1])
            .ToList();
        List<FightPoint> fightPoints = stats.Fights.Select(f => new FightPoint(f.Act, f.Floor, f.Label, f.Room)).ToList();

        var defenseRows = players
            .Select(p =>
            {
                (int lowHp, int lowMax) = AwardBuilder.Lowest(stats.Get(p.NetId), defense.GetValueOrDefault(p.NetId)) ?? (0, 0);
                return new DefenseRow(p.Name, p.ColorHex,
                    defense.GetValueOrDefault(p.NetId)?.Taken ?? 0,
                    stats.Get(p.NetId)?.Blocked ?? 0,
                    defense.GetValueOrDefault(p.NetId)?.Healed ?? 0,
                    IconOf(p),
                    stats.Get(p.NetId)?.DebuffPrevented.Values.Sum(b => b.Amount) ?? 0,
                    p.Character, lowHp, lowMax,
                    stats.Get(p.NetId)?.PetTanked ?? 0);
            })
            .ToList();

        return new RecapView(header, overview, sources, timeline, fightActs, actStarts, fightPoints, defenseRows,
            Highlights(stats, players, team), victory, DeckBuilder.Build(stats, byDamage, decks),
            DebuffBuilder.Build(stats, byDamage),
            Note(stats, players, t => t.DebuffBonus, "WHO_CARRIED.summary.bonus_note"),
            Note(stats, players, t => t.DebuffPrevented, "WHO_CARRIED.summary.prevented_note"),
            awards,
            // The game hands out badges when the run ends; until then there's nothing to show.
            stats.Finished
                ? byDamage.Select(p => new PlayerBadges(p.NetId, p.Name, p.ColorHex, IconOf(p), badges[p.NetId])).ToList()
                : null,
            facts);
    }

    /// <summary>A player's badges with their names, best rarity first (the game's order within a rarity).</summary>
    private static List<BadgeInfo> Badges(PlayerTotals? totals, Func<string, string, (string Title, string Description)>? text) =>
        (totals?.Badges ?? new List<EarnedBadge>())
            .Select(b =>
            {
                (string title, string description) = text?.Invoke(b.Id, b.Rarity) ?? (LogReplay.Pretty(b.Id), "");
                return new BadgeInfo(b.Id, b.Rarity, title, description);
            })
            .OrderBy(b => b.RarityOrder)
            .ToList();

    /// <summary>"A", "A and B", "A, B and C".</summary>
    public static string JoinAnd(IReadOnlyList<string> words) => words.Count switch
    {
        0 => "",
        1 => words[0],
        _ => Loc.Text("WHO_CARRIED.list.and", string.Join(Loc.Text("WHO_CARRIED.list.separator"), words.Take(words.Count - 1)), words[^1]),
    };

    /// <summary>
    /// Explains an extra number ("(+N)", "Prevented"), naming the debuffs behind it, biggest first; empty when none.
    /// </summary>
    private static string Note(RunStats stats, IReadOnlyList<PlayerInfo> players,
                               Func<PlayerTotals, Dictionary<string, SourceTotal>> pick, string format)
    {
        List<string> debuffs = players
            .SelectMany(p => stats.Get(p.NetId) is PlayerTotals t ? pick(t).Values : Enumerable.Empty<SourceTotal>())
            .Where(b => b.Amount > 0)
            .GroupBy(b => b.Label)
            .OrderByDescending(g => g.Sum(b => b.Amount))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .ToList();
        return debuffs.Count == 0 ? "" : Loc.Text(format, JoinAnd(debuffs));
    }

    private static List<Highlight> Highlights(RunStats stats, IReadOnlyList<PlayerInfo> players, int team)
    {
        int fights = stats.Fights.Count;
        var list = new List<Highlight>
        {
            new(Loc.Text("WHO_CARRIED.stat.team_damage"), Num(team), Loc.Text(fights == 1 ? "WHO_CARRIED.summary.fight_one" : "WHO_CARRIED.summary.fights", fights)),
        };

        (PlayerInfo Player, SourceTotal Source) top = players
            .SelectMany(p => (stats.Get(p.NetId)?.Sources.Values ?? Enumerable.Empty<SourceTotal>())
                .Select(s => (Player: p, Source: s)))
            .Where(x => x.Source.Amount > 0)
            .OrderByDescending(x => x.Source.Amount)
            .ThenBy(x => x.Source.Label, StringComparer.Ordinal)
            .FirstOrDefault();
        list.Add(top.Source == null
            ? new Highlight(Loc.Text("WHO_CARRIED.summary.top_source"), NoValue, "")
            : new Highlight(Loc.Text("WHO_CARRIED.summary.top_source"), top.Source.Label, $"{Num(top.Source.Amount)} · {top.Player.Name}"));

        HashSet<string> realKeys = players.Select(p => RunStats.KeyFor(p.NetId)).ToHashSet();
        int FightTotal(FightBucket f) => f.DamageByPlayer.Where(kv => realKeys.Contains(kv.Key)).Sum(kv => kv.Value);
        FightBucket? biggest = stats.Fights.OrderByDescending(FightTotal).FirstOrDefault();
        list.Add(biggest == null || FightTotal(biggest) == 0
            ? new Highlight(Loc.Text("WHO_CARRIED.summary.biggest_fight"), NoValue, "")
            : new Highlight(Loc.Text("WHO_CARRIED.summary.biggest_fight"), Num(FightTotal(biggest)), Loc.Text("WHO_CARRIED.summary.fight_act", biggest.Label, biggest.Act)));
        return list;
    }

    private static List<BarRow> SourceRows(PlayerTotals? totals, string colorHex)
    {
        if (totals == null) return new List<BarRow>();
        List<SourceTotal> ordered = totals.Sources.Values
            .OrderByDescending(s => s.Amount)
            .ThenByDescending(s => s.BlockRemoved)
            .ThenBy(s => s.Label, StringComparer.Ordinal)
            .ToList();
        int top = ordered.Count > 0 ? ordered[0].Amount : 0;
        Dictionary<SourceTotal, string> keys = totals.Sources.ToDictionary(kv => kv.Value, kv => kv.Key);
        List<BarRow> rows = ordered.Take(TopSources)
            .Select(s => new BarRow(s.Label, s.Kind.ToString(), s.Amount, Fraction(s.Amount, top), null, colorHex,
                BlockRemoved: s.BlockRemoved, ArtKey: SourceArt.Key(keys[s])))
            .ToList();
        List<SourceTotal> rest = ordered.Skip(TopSources).ToList();
        if (rest.Count > 0)
        {
            int sum = rest.Sum(s => s.Amount);
            rows.Add(new BarRow($"{OtherPrefix} ({rest.Count})", "", sum, Fraction(sum, top), null, GreyHex,
                BlockRemoved: rest.Sum(s => s.BlockRemoved)));
        }
        return rows;
    }

    /// <summary>Damage by kind of source, biggest first; monsters, "other" and unknown sources share one "Other".</summary>
    private static List<KindTotal> Kinds(PlayerTotals? totals)
    {
        if (totals == null) return new List<KindTotal>();
        static string KindOf(SourceKind kind) => kind switch
        {
            SourceKind.Card or SourceKind.Power or SourceKind.Relic or SourceKind.Potion or SourceKind.Orb or SourceKind.Pet => kind.ToString(),
            _ => "Other",
        };
        return totals.Sources
            .Where(kv => kv.Value.Amount > 0)
            .GroupBy(kv => KindOf(kv.Value.Kind))
            .Select(g =>
            {
                KeyValuePair<string, SourceTotal> top = g.OrderByDescending(kv => kv.Value.Amount).ThenBy(kv => kv.Value.Label, StringComparer.Ordinal).First();
                return new KindTotal(g.Key, g.Sum(kv => kv.Value.Amount), g.Count(), top.Value.Label, SourceArt.Key(top.Key));
            })
            .OrderByDescending(k => k.Amount)
            .ThenBy(k => k.Kind, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Cards the player created during fights, most first.</summary>
    private static List<CreatedCard> Created(PlayerTotals? totals) =>
        totals == null
            ? new List<CreatedCard>()
            : totals.CardsCreated.Values
                .Where(c => c.Amount > 0)
                .OrderByDescending(c => c.Amount)
                .ThenBy(c => c.Label, StringComparer.Ordinal)
                .Select(c => new CreatedCard(c.Label, c.Amount))
                .ToList();

    private static string? IconOf(PlayerInfo p) => string.IsNullOrEmpty(p.CharacterId) ? null : p.CharacterId;

    private static string Num(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static double Fraction(int value, int max) => max <= 0 ? 0 : Math.Clamp((double)value / max, 0, 1);
}
