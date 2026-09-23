using Godot;
using MegaCrit.Sts2.Core.Models;
using WhoCarried.Core;
using WhoCarried.Game;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// Rebuilds the recap of the run recorded in events.log (the most recent one) with today's rules, if replay.flag
/// exists; both are in the mod's data folder (<see cref="Tracker.DataDir"/>). Decks, damage taken, healing, end-of-floor
/// HP and badges come from the game's own saved run; the player's own block from current_run.dat. Opens it in the full
/// recap, screenshots each view and the exported image into the data folder, then closes. Inert otherwise.
/// </summary>
internal static class Replay
{
    private static readonly string[] Views = { "scoreboard", "awards", "sources", "debuffs", "support", "timeline", "defense", "decks" };
    private static readonly Dictionary<string, string?> Titles = new();

    public static void StartIfFlagged(string dataDir)
    {
        if (!File.Exists(Path.Combine(dataDir, "replay.flag"))) return;
        Later.Run(10, () => Run(dataDir));
    }

    private static void Run(string dataDir)
    {
        try
        {
            LogReplay.Result log = LogReplay.Parse(File.ReadAllLines(Path.Combine(dataDir, "events.log")), Title);
            string start = log.RunKey.Contains(':') ? log.RunKey[(log.RunKey.LastIndexOf(':') + 1)..] : "";
            string? historyPath = FindHistory(start);
            RunHistory? history = historyPath == null ? null : RunHistory.Parse(File.ReadAllText(historyPath));
            Tracker.Note($"replay: run {log.RunKey}, {log.Stats.Fights.Count} fights, history {historyPath ?? "not found"}");

            // The player's own block isn't in the log; the saved stats of the same run have it.
            RunStats? saved = RunStatsStore.Load(Path.Combine(dataDir, "current_run.dat"));
            if (saved != null && saved.RunKey == log.RunKey)
                foreach ((string key, PlayerTotals totals) in saved.Players)
                    if (log.Stats.Players.TryGetValue(key, out PlayerTotals? rebuilt)) rebuilt.Blocked = totals.Blocked;

            var records = history?.Players.ToDictionary(p => p.Id) ?? new Dictionary<ulong, RunHistory.PlayerRecord>();
            List<PlayerInfo> players = log.Players.Select(p => p with
            {
                CharacterId = records.TryGetValue(p.NetId, out RunHistory.PlayerRecord? r) ? Entry(r.CharacterId) : "",
            }).ToList();
            Dictionary<ulong, DefenseTotals> defense = records.Values.ToDictionary(r => r.Id,
                r => new DefenseTotals(r.Taken, r.Healed, r.LowestHp, r.LowestHpMax));
            // The game's saved run has everyone's badges (logs from before badges were recorded don't).
            if (history != null)
            {
                log.Stats.Finished = true;
                foreach (RunHistory.PlayerRecord r in records.Values)
                    log.Stats.SetBadges(r.Id, r.Badges ?? Array.Empty<EarnedBadge>());
            }
            Dictionary<ulong, IReadOnlyList<DeckCard>> decks = records.Values.ToDictionary(r => r.Id,
                r => (IReadOnlyList<DeckCard>)r.Deck.Select(c => Card(c.CardId, c.Upgrades)).ToList());

            // Logs from before fights recorded their room: take it from the saved run's map, by floor.
            foreach (FightBucket fight in log.Stats.Fights.Where(f => f.Room.Length == 0))
                if (history?.Rooms?.GetValueOrDefault(fight.Floor) is "monster" or "elite" or "boss" or "unknown")
                    fight.Room = history.Rooms[fight.Floor];

            bool victory = history?.Win ?? log.Victory ?? false;
            int floors = history?.Floors ?? log.Stats.Fights.LastOrDefault()?.Floor ?? 0;
            string header = victory ? Loc.Text("WHO_CARRIED.result.victory_floor", floors) : Loc.Text("WHO_CARRIED.result.defeat_floor", floors);
            string seed = history?.Seed is { Length: > 0 } s ? s : log.RunKey.Split(':')[0];
            var facts = new RunFacts(floors, history?.Ascension ?? 0, history?.RunTime ?? 0, seed);
            RecapView view = RecapBuilder.Build(log.Stats, players, defense, header, victory, decks, GameReader.BadgeText, facts);
            DateTime date = history != null && history.StartTime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(history.StartTime).LocalDateTime
                : DateTime.Now;

            Func<string?, Texture2D?> icons = GameReader.WithPowerIcons(id => GameReader.CharacterById(id) is CharacterModel c ? GameReader.CharacterIcon(c) : null);
            Climb.IgnoreHover = true;
            PanelHandle handle = RecapUi.ShowView(view, icons, new CardVisuals((_, cardId) => CardModelFor(cardId)));
            Capture(0);

            void Capture(int index)
            {
                if (!GodotObject.IsInstanceValid(handle.Tabs)) return; // someone closed the replay: stop quietly
                if (index >= Views.Length)
                {
                    Export(view, icons, date, victory, dataDir);
                    return;
                }
                handle.Tabs.CurrentTab = index;
                if (Views[index] == "timeline" && view.FightPoints.Count > 0)
                {
                    // Show the readout on the run's biggest single-player hit, like the mockup.
                    int peak = Enumerable.Range(0, view.FightPoints.Count).OrderByDescending(i => view.Timeline.Max(s => s.Values[i])).First();
                    Later.Run(0.6, () => TimelineTab.PreviewShowFight?.Invoke(peak));
                }
                Later.Run(0.9, () =>
                {
                    Image screen = ((SceneTree)Engine.GetMainLoop()).Root.GetTexture().GetImage();
                    screen.SavePng(Path.Combine(dataDir, $"replay-{index + 1}-{Views[index]}.png"));
                    Capture(index + 1);
                });
            }
        }
        catch (Exception e)
        {
            Tracker.LogError("replay", e);
            RecapUi.Hide();
        }
    }

    private static void Export(RecapView view, Func<string?, Texture2D?> icons, DateTime date, bool victory, string dataDir)
    {
        string path = Path.Combine(dataDir, $"replay-{Views.Length + 1}-export.png");
        PngExporter.Save(SummaryCard.Create(view, icons, date), SummaryCard.Width, path, error =>
        {
            Tracker.Note(error == null ? $"replay exported {path}" : $"replay export failed: {error}");
            Tracker.Note("replay done");
            RecapUi.Hide();
        });
    }

    /// <summary>The game's saved run for this start time, in any profile.</summary>
    private static string? FindHistory(string startTime)
    {
        if (startTime.Length == 0) return null;
        string root = OS.GetUserDataDir(); // the game's saves: %APPDATA%\SlayTheSpire2 on Windows, its equivalents elsewhere
        try
        {
            return Directory.Exists(root)
                ? Directory.EnumerateFiles(root, $"{startTime}.run", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>"CHARACTER.NECROBINDER" → "NECROBINDER".</summary>
    private static string Entry(string id) => id.Contains('.') ? id[(id.IndexOf('.') + 1)..] : id;

    private static CardModel? CardModelFor(string entry)
    {
        try { return ModelDb.GetByIdOrNull<CardModel>(ModelId.Deserialize($"CARD.{entry}")); }
        catch (Exception) { return null; }
    }

    private static DeckCard Card(string id, int upgrades)
    {
        string entry = Entry(id);
        CardModel? model = CardModelFor(entry);
        return model == null
            ? new DeckCard(entry, LogReplay.Pretty(entry), "Other", "Common", upgrades)
            : new DeckCard(entry, GameText.Title(model.TitleLocString, entry), model.Type.ToString(), model.Rarity.ToString(), upgrades);
    }

    /// <summary>A card's display name from its id, for "Osty via Unleash".</summary>
    private static string? Title(string entry)
    {
        if (Titles.TryGetValue(entry, out string? cached)) return cached;
        CardModel? model = CardModelFor(entry);
        string? title = model == null ? null : GameText.Title(model.TitleLocString, entry);
        Titles[entry] = title;
        return title;
    }
}
