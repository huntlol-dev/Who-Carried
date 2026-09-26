using System.Globalization;
using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using WhoCarried.Core;
using WhoCarried.Localization;

namespace WhoCarried.Game;

/// <summary>Read-only queries against game state.</summary>
internal static class GameReader
{
    private static readonly string[] Palette = { "d85a30", "7f77dd", "1d9e75", "d4537e", "378add", "ba7517" };

    public static IReadOnlyList<PlayerInfo> Players(IRunState run)
    {
        var list = new List<PlayerInfo>();
        for (int i = 0; i < run.Players.Count; i++)
        {
            Player p = run.Players[i];
            list.Add(new PlayerInfo(p.NetId, PlayerName(p, i, run.Players.Count), CharacterName(p.Character),
                CharacterHex(p.Character, i), p.Character.Id.Entry));
        }
        return list;
    }

    /// <summary>The top-bar icon of the character with this id among the run's players (modded characters included).</summary>
    public static Texture2D? CharacterIcon(IRunState run, string? characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return null;
        CharacterModel? character = run.Players.Select(p => p.Character).FirstOrDefault(c => c.Id.Entry == characterId);
        return character == null ? null : CharacterIcon(character);
    }

    public static Texture2D? CharacterIcon(CharacterModel character)
    {
        try
        {
            if (character.IconTexture is Texture2D texture) return texture;
        }
        catch (Exception)
        {
            // not preloaded: try the conventional path below
        }
        try
        {
            string path = $"res://images/ui/top_panel/character_icon_{character.Id.Entry.ToLowerInvariant()}.png";
            if (ResourceLoader.Exists(path)) return ResourceLoader.Load<Texture2D>(path);
        }
        catch (Exception)
        {
            // fall back to the initials badge
        }
        return null;
    }

    /// <summary>
    /// A cached picture that can still be used. The game disposes pictures it unloads (after a fight, on the way to
    /// the menu), which kills our cached handle to the same picture; a dead one is looked up again. A cached "none"
    /// stays none.
    /// </summary>
    private static bool Cached(Dictionary<string, Texture2D?> cache, string key, out Texture2D? texture) =>
        cache.TryGetValue(key, out texture) && (texture == null || GodotObject.IsInstanceValid(texture));

    private static readonly Dictionary<string, Texture2D?> PowerIcons = new();

    /// <summary>A power's own icon (modded powers included), or null. Cached per id.</summary>
    public static Texture2D? PowerIcon(string powerId)
    {
        if (powerId == "STRENGTH_LOSS") powerId = "STRENGTH_POWER"; // our own entry for lasting Strength loss
        if (Cached(PowerIcons, powerId, out Texture2D? cached)) return cached;
        Texture2D? icon = null;
        try { icon = ModelDb.AllPowers.FirstOrDefault(p => p.Id.Entry == powerId)?.Icon; }
        catch (Exception) { /* no icon: the UI draws a plain marker */ }
        PowerIcons[powerId] = icon;
        return icon;
    }

    /// <summary>Icon keys for a character's select-screen portrait and its card energy gem: prefix + character id.</summary>
    public const string PortraitPrefix = "portrait:", EnergyPrefix = "energy:";

    /// <summary>
    /// Extends a character-icon lookup with the recap's other pictures: "power:ID" power icons, "badge…" badge art,
    /// "portrait:"/"energy:" + character id, and "card:"/"relic:"/"potion:"/"orb:" + id for damage sources.
    /// </summary>
    public static Func<string?, Texture2D?> WithPowerIcons(Func<string?, Texture2D?> characterIcons) =>
        key => key switch
        {
            null => characterIcons(key),
            _ when key.StartsWith(DebuffBuilder.IconPrefix, StringComparison.Ordinal) => PowerIcon(key[DebuffBuilder.IconPrefix.Length..]),
            _ when key.StartsWith(BadgeInfo.IconPrefix, StringComparison.Ordinal)
                   || key.StartsWith(BadgeInfo.BasePrefix, StringComparison.Ordinal) => BadgeImage(key),
            _ when key.StartsWith(PortraitPrefix, StringComparison.Ordinal) || key.StartsWith(EnergyPrefix, StringComparison.Ordinal)
                   || key.StartsWith(SourceArt.Card, StringComparison.Ordinal) || key.StartsWith(SourceArt.Relic, StringComparison.Ordinal)
                   || key.StartsWith(SourceArt.Potion, StringComparison.Ordinal) || key.StartsWith(SourceArt.Orb, StringComparison.Ordinal)
                   || key.StartsWith(SourceArt.Model, StringComparison.Ordinal) => ModelArt(key),
            _ => characterIcons(key),
        };

    private static readonly Dictionary<string, Texture2D?> ModelArts = new();

    /// <summary>Pictures that come from the game's models (modded ones included). Cached per key; null when missing.</summary>
    private static Texture2D? ModelArt(string key)
    {
        if (Cached(ModelArts, key, out Texture2D? cached)) return cached;
        Texture2D? texture = null;
        try
        {
            int colon = key.IndexOf(':');
            string prefix = key[..(colon + 1)], id = key[(colon + 1)..];
            texture = prefix switch
            {
                PortraitPrefix => CharacterById(id)?.CharacterSelectIcon,
                EnergyPrefix => CharacterById(id) is CharacterModel c && ResourceLoader.Exists(c.CardPool.EnergyIconPath)
                    ? ResourceLoader.Load<Texture2D>(c.CardPool.EnergyIconPath) : null,
                SourceArt.Card => Model<CardModel>("CARD", id)?.Portrait,
                SourceArt.Relic => Model<RelicModel>("RELIC", id)?.Icon,
                SourceArt.Potion => Model<PotionModel>("POTION", id)?.Image,
                SourceArt.Orb => Model<OrbModel>("ORB", id)?.Icon,
                SourceArt.Model => Models.Icon(id),
                _ => null,
            };
        }
        catch (Exception)
        {
            // no picture: the UI shows a plain placeholder
        }
        ModelArts[key] = texture;
        return texture;
    }

    private static T? Model<T>(string category, string entry) where T : AbstractModel
    {
        try { return ModelDb.GetByIdOrNull<T>(ModelId.Deserialize($"{category}.{entry}")); }
        catch (Exception) { return null; }
    }

    /// <summary>A character by id entry ("NECROBINDER", "THETAILOR-THE_TAILOR"), modded characters included.</summary>
    public static CharacterModel? CharacterById(string? entry)
    {
        if (string.IsNullOrEmpty(entry)) return null;
        try
        {
            return ModelDb.AllCharacters.FirstOrDefault(c => c.Id.Entry == entry)
                   ?? ModelDb.GetByIdOrNull<CharacterModel>(ModelId.Deserialize($"CHARACTER.{entry}"));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string CharacterName(CharacterModel character) => GameText.Title(character.Title, character.Id.Entry);

    public static string CharacterHex(CharacterModel character, int index)
    {
        try { return character.NameColor.ToHtml(false); }
        catch (Exception) { return Palette[index % Palette.Length]; }
    }

    public static IReadOnlyList<DeckCard> DeckFacts(IEnumerable<CardModel> cards) =>
        cards.Select(c => new DeckCard(c.Id.Entry, GameText.Title(c.TitleLocString, c.Id.Entry), c.Type.ToString(),
            c.Rarity.ToString(), c.CurrentUpgradeLevel)).ToList();

    public static IReadOnlyDictionary<ulong, IReadOnlyList<DeckCard>> Decks(IRunState run) =>
        run.Players.ToDictionary(p => p.NetId, p => DeckFacts(p.Deck.Cards));

    /// <summary>The most-upgraded copy of a card in a player's deck; that's the copy we draw.</summary>
    public static CardModel? DeckCardModel(IRunState run, ulong playerId, string cardId) =>
        run.Players.FirstOrDefault(p => p.NetId == playerId)?.Deck.Cards
            .Where(c => c.Id.Entry == cardId)
            .OrderByDescending(c => c.CurrentUpgradeLevel)
            .FirstOrDefault();

    /// <summary>
    /// Damage taken, HP healed, the lowest end-of-floor HP and gold earned per player, from the game's own per-floor
    /// history.
    /// </summary>
    public static IReadOnlyDictionary<ulong, DefenseTotals> Defense(IRunState run)
    {
        var taken = new Dictionary<ulong, int>();
        var healed = new Dictionary<ulong, int>();
        var gold = new Dictionary<ulong, int>();
        var lows = new FloorLows();
        bool firstPoint = true;
        foreach (var act in run.MapPointHistory)
        foreach (var point in act)
        {
            foreach (var stats in point.PlayerStats)
            {
                taken[stats.PlayerId] = taken.GetValueOrDefault(stats.PlayerId) + stats.DamageTaken;
                gold[stats.PlayerId] = gold.GetValueOrDefault(stats.PlayerId) + stats.GoldGained;
                // The run's first point records the starting HP as "healed" (0 -> start HP); that isn't healing.
                healed[stats.PlayerId] = healed.GetValueOrDefault(stats.PlayerId) + (firstPoint ? 0 : stats.HpHealed);
                lows.Add(stats.PlayerId, stats.CurrentHp, stats.MaxHp, stats.DamageTaken);
            }
            firstPoint = false;
        }
        return taken.Keys.Union(healed.Keys).Union(gold.Keys)
            .ToDictionary(id => id, id => new DefenseTotals(taken.GetValueOrDefault(id), healed.GetValueOrDefault(id),
                lows.Get(id).Hp, lows.Get(id).Max, gold.GetValueOrDefault(id)));
    }

    /// <summary>
    /// A badge's name and description in the game's language, as its end screen shows them (some badges have a
    /// wording per rarity). Formatting tags are dropped; unknown badges fall back to a name made from the id.
    /// </summary>
    public static (string Title, string Description) BadgeText(string id, string rarity)
    {
        string? Text(string key)
        {
            try
            {
                if (!LocString.Exists("badges", key)) return null;
                string text = Tags.Replace(new LocString("badges", key).GetFormattedText(), "").Trim();
                return text.Length > 0 ? text : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
        return (Text($"{id}.{rarity}Title") ?? Text($"{id}.title") ?? LogReplay.Pretty(id),
                Text($"{id}.{rarity}Description") ?? Text($"{id}.description") ?? "");
    }

    private static readonly Regex Tags = new(@"\[/?[a-zA-Z_]+[^\]]*\]", RegexOptions.Compiled);

    private static readonly Dictionary<string, Texture2D?> BadgeImages = new();

    /// <summary>A badge's picture ("badge:ID") or its rarity holder ("badge-base:gold"), from the game's end screen art.</summary>
    public static Texture2D? BadgeImage(string key)
    {
        if (Cached(BadgeImages, key, out Texture2D? cached)) return cached;
        string name = key.StartsWith(BadgeInfo.BasePrefix, StringComparison.Ordinal)
            ? key[BadgeInfo.BasePrefix.Length..]
            : key[BadgeInfo.IconPrefix.Length..];
        Texture2D? texture = null;
        try
        {
            string path = ImageHelper.GetImagePath($"ui/game_over_screen/badge_{name.ToLowerInvariant()}.png");
            if (ResourceLoader.Exists(path)) texture = ResourceLoader.Load<Texture2D>(path);
        }
        catch (Exception)
        {
            // no picture: the UI draws a plain medal
        }
        BadgeImages[key] = texture;
        return texture;
    }

    /// <summary>
    /// How many times the run starting now has been reloaded; a new run starts at 0. Not proof of a load on its own:
    /// see <see cref="RunOrigin"/>. 0 if the count can't be read.
    /// </summary>
    public static int ReloadCount()
    {
        try
        {
            return (int)(AccessTools.Field(typeof(RunManager), "_numReloads")?.GetValue(RunManager.Instance) ?? 0);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// Seed + the run's start time (set before RunStarted fires). Stable across Save &amp; Quit, except that a co-op
    /// guest's first reload swaps its own start time for the host's.
    /// </summary>
    public static string RunKey(IRunState run)
    {
        long start = 0;
        try
        {
            start = (long)(AccessTools.Field(typeof(RunManager), "_startTime")?.GetValue(RunManager.Instance) ?? 0L);
        }
        catch (Exception)
        {
            // fall back to seed only
        }
        return $"{run.Rng.StringSeed}:{start}";
    }

    /// <summary>The result chip next to the title: "Defeat on floor 37", "Victory on floor 49", "Act 2, floor 17".</summary>
    public static string Header(IRunState run, bool? victory) => victory switch
    {
        true => Loc.Text("WHO_CARRIED.result.victory_floor", run.TotalFloor),
        false => Loc.Text("WHO_CARRIED.result.defeat_floor", run.TotalFloor),
        null => Loc.Text("WHO_CARRIED.result.progress", run.CurrentActIndex + 1, run.TotalFloor),
    };

    /// <summary>The map room the party is in, for the fight icons: "monster", "elite", "boss", "unknown" (event), or "".</summary>
    public static string RoomType(IRunState run)
    {
        try
        {
            return run.CurrentMapPoint?.PointType switch
            {
                MapPointType.Monster => "monster",
                MapPointType.Elite => "elite",
                MapPointType.Boss => "boss",
                MapPointType.Unknown => "unknown",
                _ => "",
            };
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>Floor, ascension, play time and seed, for the recap's top bar.</summary>
    public static RunFacts Facts(IRunState run)
    {
        long seconds = 0;
        try { seconds = RunManager.Instance.RunTime; }
        catch (Exception) { /* no timer */ }
        string seed = "";
        try { seed = run.Rng.StringSeed; }
        catch (Exception) { /* no seed */ }
        return new RunFacts(run.TotalFloor, run.AscensionLevel, seconds, seed, run.CurrentActIndex + 1);
    }

    public static string EncounterLabel(ICombatState? combat)
    {
        EncounterModel? encounter = combat?.Encounter;
        return encounter == null ? Loc.Text("WHO_CARRIED.fallback.fight") : GameText.Title(encounter.Title, encounter.Id.Entry);
    }

    /// <summary>
    /// Solo runs use the "None" platform, whose player id is just 1, so ask Steam for the local persona instead.
    /// Raw (unescaped) names: our labels are plain text, not BBCode.
    /// </summary>
    private static string PlayerName(Player p, int index, int playerCount)
    {
        try
        {
            PlatformType platform = RunManager.Instance.NetService.Platform;
            string name = platform == PlatformType.None && playerCount == 1
                ? PlatformUtil.GetPlayerNameRaw(PlatformType.Steam, PlatformUtil.GetLocalPlayerId(PlatformType.Steam))
                : PlatformUtil.GetPlayerNameRaw(platform, p.NetId);
            if (!string.IsNullOrWhiteSpace(name) && name != p.NetId.ToString(CultureInfo.InvariantCulture)) return name;
        }
        catch (Exception)
        {
            // fall through
        }
        return playerCount == 1 ? Loc.Text("WHO_CARRIED.fallback.you") : Loc.Text("WHO_CARRIED.fallback.player", index + 1);
    }
}
