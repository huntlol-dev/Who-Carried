using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class AwardTests
{
    private static readonly PlayerInfo Alice = new(1, "Alice", "Ironclad", "d85a30", "IRONCLAD");
    private static readonly PlayerInfo Bob = new(2, "Bob", "The Silent", "7fff00", "SILENT");
    private static readonly SourceRef Bash = new(SourceKind.Card, "BASH", "Bash");
    private static readonly SourceRef Shiv = new(SourceKind.Card, "SHIV", "Shiv");
    private static readonly SourceRef Vuln = new(SourceKind.Power, "VULNERABLE_POWER", "Vulnerable");
    private static readonly SourceRef Weak = new(SourceKind.Power, "WEAK_POWER", "Weak");

    private static Award? Find(IReadOnlyList<Award> awards, string title) => awards.FirstOrDefault(a => a.Title == title);

    [Test]
    public static void EachAwardGoesToItsLeader()
    {
        var s = new RunStats();
        s.BeginFight(1, 2, "Jaw Worm");
        s.RecordDamage(1, Bash, 30);
        s.RecordDamage(2, Shiv, 5);
        s.EndFight();
        s.BeginFight(1, 3, "Cultist");
        s.RecordDamage(1, Bash, 12);
        s.RecordDamage(2, Shiv, 20);
        s.EndFight();
        s.BeginFight(1, 4, "Louse");
        s.RecordDamage(1, Bash, 40);
        s.EndFight();
        s.RecordDebuffBonus(2, Vuln, 50);
        s.RecordDebuffPrevented(2, Weak, 12);
        s.RecordBlocked(1, 80);
        s.RecordBlocked(2, 20);
        var defense = new Dictionary<ulong, DefenseTotals> { [1] = new(40, 0), [2] = new(90, 0) };

        IReadOnlyList<Award> a = AwardBuilder.Build(s, new[] { Alice, Bob }, defense);
        Check.Equal("Alice", Find(a, AwardBuilder.HeavyHitter)!.PlayerName, "heavy hitter");
        Check.Equal("40", Find(a, AwardBuilder.HeavyHitter)!.Value, "biggest hit");
        Check.Equal("damage in one hit, with Bash", Find(a, AwardBuilder.HeavyHitter)!.Detail, "names the card");
        Check.Equal("Bob", Find(a, AwardBuilder.Enabler)!.PlayerName, "enabler");
        Check.Equal("+50", Find(a, AwardBuilder.Enabler)!.Value, "bonus");
        Check.Equal("damage teammates gained from their Vulnerable", Find(a, AwardBuilder.Enabler)!.Detail, "names the debuff");
        Check.Equal("Bob", Find(a, AwardBuilder.Protector)!.PlayerName, "protector");
        Check.Equal("Alice", Find(a, AwardBuilder.Wall)!.PlayerName, "wall");
        Check.Equal("2 of 3", Find(a, AwardBuilder.FightLeader)!.Value, "Alice led two of three fights");
        Check.Equal("Alice", Find(a, AwardBuilder.Unscathed)!.PlayerName, "least damage taken");
        Check.Equal("Bob", Find(a, AwardBuilder.PunchingBag)!.PlayerName, "most damage taken");
        Check.True(Find(a, AwardBuilder.Clutch) == null, "nobody got low");
        Check.True(Find(a, AwardBuilder.CardFactory) == null, "no cards created");
    }

    [Test]
    public static void ClutchIsTheLowestShareOfMaxHpAndOnlyWhenItWasClose()
    {
        var s = new RunStats();
        s.RecordHp(1, 20, 80); // 25%
        s.RecordHp(2, 9, 30);  // 30%
        IReadOnlyList<Award> a = AwardBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>());
        Check.Equal("Alice", Find(a, AwardBuilder.Clutch)!.PlayerName, "25% beats 30%");
        Check.Equal("20 HP", Find(a, AwardBuilder.Clutch)!.Value, "value");

        // The game's end-of-floor HP counts too.
        var floors = new Dictionary<ulong, DefenseTotals> { [2] = new(0, 0, 7, 65) };
        Check.Equal("Bob", Find(AwardBuilder.Build(s, new[] { Alice, Bob }, floors), AwardBuilder.Clutch)!.PlayerName,
            "7 of 65 from the floor history");

        var comfy = new RunStats();
        comfy.RecordHp(1, 50, 80);
        Check.True(Find(AwardBuilder.Build(comfy, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>()), AwardBuilder.Clutch) == null,
            "62% isn't a close call");
    }

    [Test]
    public static void RecordHpKeepsTheLowestShareAndIgnoresDeath()
    {
        var s = new RunStats();
        Check.True(s.RecordHp(1, 40, 80), "first reading");
        Check.True(!s.RecordHp(1, 45, 80), "higher isn't a low");
        Check.True(s.RecordHp(1, 30, 90), "33% beats 50%");
        Check.True(!s.RecordHp(1, 0, 90), "dead doesn't count");
        Check.Equal(30, s.Get(1)!.LowestHp, "hp");
        Check.Equal(90, s.Get(1)!.LowestHpMax, "max");
    }

    [Test]
    public static void SoloRunsOnlyGetAwardsThatDontComparePlayers()
    {
        var s = new RunStats();
        s.RecordDamage(1, Bash, 25);
        s.RecordBlocked(1, 100);
        s.RecordHp(1, 3, 80);
        IReadOnlyList<Award> a = AwardBuilder.Build(s, new[] { Alice }, new Dictionary<ulong, DefenseTotals> { [1] = new(50, 0) });
        Check.Equal(string.Join(",", AwardBuilder.HeavyHitter, AwardBuilder.Clutch), string.Join(",", a.Select(x => x.Title)), "solo awards");
    }

    [Test]
    public static void SiegeBreakerAndJackOfAllTrades()
    {
        var s = new RunStats();
        s.RecordDamage(1, Bash, 10, blocked: 40);
        for (int i = 0; i < 6; i++) s.RecordDamage(2, new SourceRef(SourceKind.Card, $"C{i}", $"Card {i}"), 5, blocked: 1);
        IReadOnlyList<Award> a = AwardBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>());
        Check.Equal("Alice", Find(a, AwardBuilder.SiegeBreaker)!.PlayerName, "most block removed");
        Check.Equal("40", Find(a, AwardBuilder.SiegeBreaker)!.Value, "block removed");
        Check.Equal("Bob", Find(a, AwardBuilder.JackOfAllTrades)!.PlayerName, "six sources");
        Check.Equal("6", Find(a, AwardBuilder.JackOfAllTrades)!.Value, "count");
    }

    [Test]
    public static void FloorLowsSkipFloorsWherePlayersWentDown()
    {
        var lows = new FloorLows();
        lows.Add(1, 60, 75, 0);
        lows.Add(1, 5, 75, 55);  // 60 -> 5: a real close call
        lows.Add(1, 1, 75, 5);   // 5 HP, took 5: went down, revived at 1
        lows.Add(1, 30, 75, 0);
        Check.Equal((5, 75), lows.Get(1), "the revive doesn't count");
        Check.Equal((0, 0), lows.Get(2), "nothing recorded");
    }

    [Test]
    public static void TiesGoToTheHigherRankedPlayer()
    {
        var s = new RunStats();
        s.RecordBlocked(1, 30);
        s.RecordBlocked(2, 30);
        Check.Equal("Bob", Find(AwardBuilder.Build(s, new[] { Bob, Alice }, new Dictionary<ulong, DefenseTotals>()), AwardBuilder.Wall)!.PlayerName,
            "Bob is listed first");
    }

    [Test]
    public static void ScoreboardShowsHeadlineAwardAndBadgesOnceTheRunEnds()
    {
        var s = new RunStats();
        s.RecordDamage(1, Bash, 100);
        s.RecordDamage(2, Shiv, 60);
        s.RecordDebuffBonus(2, Vuln, 30);
        s.SetBadges(1, new[] { new EarnedBadge { Id = "KACHING", Rarity = "bronze" }, new EarnedBadge { Id = "PERFECT", Rarity = "gold" } });

        RecapView running = RecapBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>(), "h");
        Check.True(!running.BadgesKnown, "no badges mid-run");
        Check.Equal(AwardBuilder.HeavyHitter, running.Overview[0].Award, "Alice's headline");
        Check.Equal(AwardBuilder.Enabler, running.Overview[1].Award, "Bob's headline");

        s.Finished = true;
        RecapView done = RecapBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>(), "h",
            badgeText: (id, rarity) => ($"{id} title", $"{rarity} text"));
        Check.True(done.BadgesKnown, "badges after the run");
        Check.Equal("PERFECT,KACHING", string.Join(",", done.Overview[0].BadgeList.Select(b => b.Id)), "gold first");
        Check.Equal("PERFECT title", done.Overview[0].BadgeList[0].Title, "localized title");
        Check.Equal("badge-base:gold", done.Overview[0].BadgeList[0].BaseKey, "holder key");
        Check.Equal(0, done.Badges!.Single(p => p.Name == "Bob").Badges.Count, "Bob earned none");
    }

    [Test]
    public static void ReplayReadsHpLowsAndBadgesFromTheLog()
    {
        string[] log =
        {
            "Who Carried v0.1.0 - run SEED:1 started 2026-09-12 10:00",
            "player 11 = Moth (The Tailor) #5c350f",
            "[F2 A1] fight start: Toadpoles",
            "[F2 A1] Moth hp low 30/65",
            "[F2 A1] Moth hp low 7/65",
            "[F2 A1] fight end, saved",
            "[F2 A1] run ended: victory",
            "[F2 A1] Moth badge PERFECT (gold)",
            "[F2 A1] Moth badge ELITE (silver)",
        };
        RunStats stats = LogReplay.Parse(log).Stats;
        Check.Equal(7, stats.Get(11)!.LowestHp, "lowest");
        Check.Equal("PERFECT:gold,ELITE:silver", string.Join(",", stats.Get(11)!.Badges.Select(b => $"{b.Id}:{b.Rarity}")), "badges");
    }

    [Test]
    public static void RunHistoryReadsBadgesAndLowestHp()
    {
        const string json = """
        {
          "win": true, "start_time": 1,
          "map_point_history": [
            [ { "player_stats": [ { "player_id": 11, "current_hp": 52, "max_hp": 65 } ] },
              { "player_stats": [ { "player_id": 11, "current_hp": 7, "max_hp": 65 } ] },
              { "player_stats": [ { "player_id": 11, "current_hp": 0, "max_hp": 65 } ] } ]
          ],
          "players": [ { "id": 11, "character": "CHARACTER.X", "deck": [],
                         "badges": [ { "id": "PERFECT", "rarity": "gold" }, { "id": "KACHING", "rarity": "Bronze" } ] } ]
        }
        """;
        RunHistory.PlayerRecord p = RunHistory.Parse(json).Players.Single();
        Check.Equal(7, p.LowestHp, "lowest HP lived through");
        Check.Equal(65, p.LowestHpMax, "max");
        Check.Equal("PERFECT:gold,KACHING:bronze", string.Join(",", p.Badges!.Select(b => $"{b.Id}:{b.Rarity}")), "badges, rarity lower-cased");
    }

    [Test]
    public static void EachSupportAwardGoesToItsLeader()
    {
        var s = new RunStats();
        s.RecordSupport(1, 2, SupportKind.Energy, 3);
        s.RecordSupport(2, 1, SupportKind.Energy, 1);
        s.RecordSupport(1, 2, SupportKind.Cards, 3);
        s.RecordSupport(2, 1, SupportKind.Block, 12);
        s.RecordSupport(2, 1, SupportKind.Buffs, 3);
        s.RecordSupport(1, 2, SupportKind.Draws, 4);
        IReadOnlyList<Award> a = AwardBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>());
        Check.Equal("Alice", Find(a, AwardBuilder.Battery)!.PlayerName, "battery");
        Check.Equal("3", Find(a, AwardBuilder.Battery)!.Value, "energy given");
        Check.Equal("energy given to teammates", Find(a, AwardBuilder.Battery)!.Detail, "battery detail");
        Check.Equal("Alice", Find(a, AwardBuilder.CarePackage)!.PlayerName, "care package");
        Check.Equal("Bob", Find(a, AwardBuilder.Bodyguard)!.PlayerName, "bodyguard");
        Check.Equal("12", Find(a, AwardBuilder.Bodyguard)!.Value, "block given");
        Check.Equal("Bob", Find(a, AwardBuilder.Coach)!.PlayerName, "coach");
        Check.Equal("Alice", Find(a, AwardBuilder.Playmaker)!.PlayerName, "playmaker");
    }

    [Test]
    public static void SupportAwardsNeedARealAmount()
    {
        var s = new RunStats();
        s.RecordSupport(1, 2, SupportKind.Energy, AwardBuilder.MinEnergyGiven - 1);
        s.RecordSupport(1, 2, SupportKind.Cards, AwardBuilder.MinCardsGiven - 1);
        s.RecordSupport(1, 2, SupportKind.Block, AwardBuilder.MinBlockGiven - 1);
        s.RecordSupport(1, 2, SupportKind.Buffs, AwardBuilder.MinBuffsGiven - 1);
        s.RecordSupport(1, 2, SupportKind.Draws, AwardBuilder.MinDrawsGiven - 1);
        IReadOnlyList<Award> a = AwardBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>());
        foreach (string title in new[] { AwardBuilder.Battery, AwardBuilder.CarePackage, AwardBuilder.Bodyguard, AwardBuilder.Coach, AwardBuilder.Playmaker })
            Check.True(Find(a, title) == null, $"{title} below its minimum");
    }

    [Test]
    public static void SoloRunsGetNoSupportAwards()
    {
        var s = new RunStats();
        s.RecordSupport(1, 2, SupportKind.Energy, 9); // a player who isn't in the run
        IReadOnlyList<Award> a = AwardBuilder.Build(s, new[] { Alice }, new Dictionary<ulong, DefenseTotals>());
        Check.True(Find(a, AwardBuilder.Battery) == null, "no teammates, no Battery");
    }

    [Test]
    public static void SupportAwardsComeStraightAfterProtectorAndTiesGoToTheHigherRank()
    {
        var s = new RunStats();
        s.RecordDebuffPrevented(2, Weak, 12);
        s.RecordBlocked(1, 80);
        s.RecordSupport(1, 2, SupportKind.Energy, 5);
        s.RecordSupport(2, 1, SupportKind.Energy, 5);
        IReadOnlyList<Award> a = AwardBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>());
        List<string> order = a.Select(x => x.Title).ToList();
        Check.True(order.IndexOf(AwardBuilder.Protector) < order.IndexOf(AwardBuilder.Battery), "after Protector");
        Check.True(order.IndexOf(AwardBuilder.Battery) < order.IndexOf(AwardBuilder.Wall), "before Wall");
        Check.Equal("Alice", Find(a, AwardBuilder.Battery)!.PlayerName, "a tie goes to the higher-ranked player");
        Check.Equal(AwardBuilder.Battery, AwardBuilder.Headline(a, 1), "Alice's headline is her support award, not Wall");
    }

    [Test]
    public static void UpToTenAwardsLayOutAsTheyAlwaysDid()
    {
        // The old rule: one row up to five, else two; steps of 1130 / columns, 226 at most; cards 20 narrower.
        foreach (int count in Enumerable.Range(1, 10))
        {
            int rows = count <= 5 ? 1 : 2, columns = (count + rows - 1) / rows;
            float step = Math.Min(226, 1130f / columns);
            AwardGrid.Layout g = AwardGrid.For(count);
            Check.Equal(columns, g.Columns, $"{count} awards: columns");
            Check.Near(step, g.Step, $"{count} awards: step", 0.001);
            Check.Near(step - 20, g.Width, $"{count} awards: width", 0.001);
        }
    }

    [Test]
    public static void ManyAwardsFitTheSpreadBothWays()
    {
        foreach (int count in Enumerable.Range(11, 6))
        {
            AwardGrid.Layout g = AwardGrid.For(count);
            int rows = (count + g.Columns - 1) / g.Columns;
            Check.True(rows == 3, $"{count} awards: three rows");
            Check.True(g.Columns * g.Step - AwardGrid.GapX <= AwardGrid.AreaW + 0.01, $"{count} awards fit across");
            Check.True((rows - 1) * g.RowStep + g.Width * HandLayout.Aspect <= AwardGrid.AreaH + 0.01, $"{count} awards fit down");
        }
    }
}
