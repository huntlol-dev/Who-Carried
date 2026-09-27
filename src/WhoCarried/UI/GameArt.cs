using Godot;

namespace WhoCarried.UI;

/// <summary>
/// The game's own UI art the recap is dressed in: the ancient card frame and banner, energy gems, the top bar, map
/// room icons, stats-screen icons, and power, intent and reward icons that give each award and heading a picture of
/// its own. Loaded on first use and cached; a missing texture comes back as null and the recap draws without it.
/// </summary>
internal static class GameArt
{
    private const string Ui = "res://images/atlases/ui_atlas.sprites/";
    private const string Map = "res://images/atlases/compressed.sprites/map/";
    private const string Stats = "res://images/packed/statistics_screen/";
    private const string Powers = "res://images/powers/";

    public const string Frame = "frame", Banner = "banner", Energy = "energy", TopBar = "top_bar", Floor = "floor",
        Timer = "timer", Ascension = "ascension", Heart = "heart", Deck = "deck", Swords = "swords", Trophy = "trophy",
        Cards = "cards", Achievements = "achievements",
        Block = "block", Skull = "skull", Brush = "brush",
        Dot = "dot", Monster = "monster", Elite = "elite", Boss = "boss", Unknown = "unknown",
        DrawPile = "draw_pile", Gold = "gold", CardReward = "card_reward", Chest = "chest", DebuffIntent = "debuff_intent",
        MapMarker = "map_marker", HighFive = "high_five", Explosion = "explosion", Rampart = "rampart",
        BrokenBlock = "broken_block", Guarded = "guarded", Leader = "leader", Wrench = "wrench", Factory = "factory",
        Dummy = "dummy", Blur = "blur", Lightning = "lightning", Clipboard = "clipboard", Pawn = "pawn", Shell = "shell",
        Peak = "peak", Tainted = "tainted";

    private static readonly Dictionary<string, string> Paths = new()
    {
        [Frame] = Ui + "card/card_frame_ancient_s.tres",
        [Banner] = Ui + "card/ancient_banner.tres",
        [Energy] = Ui + "card/energy_colorless.tres",
        [TopBar] = Ui + "top_bar/top_bar.tres",
        [Floor] = Ui + "top_bar/top_bar_floor.tres",
        [Timer] = Ui + "top_bar/timer_icon.tres",
        [Ascension] = Ui + "top_bar/top_bar_ascension.tres",
        [Heart] = Ui + "top_bar/top_bar_heart.tres",
        [Deck] = Ui + "top_bar/top_bar_deck.tres",
        [Swords] = Stats + "stats_swords.png",
        [Trophy] = Stats + "stats_trophy.png",
        [Cards] = Stats + "stats_cards.png",
        [Achievements] = Stats + "stats_achievements.png",
        [Block] = "res://images/ui/combat/block.png",
        [Skull] = "res://images/ui/emote/skull.png",
        [Brush] = Map + "map_circle_0.tres",
        [Dot] = Map + "map_dot.tres",
        [Monster] = Ui + "map/icons/map_monster.tres",
        [Elite] = Ui + "map/icons/map_elite.tres",
        [Boss] = Ui + "map/icons/map_burly_monster.tres",
        [Unknown] = Ui + "map/icons/map_unknown.tres",
        [DrawPile] = "res://images/packed/combat_ui/draw_pile.png",
        [Gold] = "res://images/packed/sprite_fonts/gold_icon.png",
        [CardReward] = "res://images/ui/reward_screen/reward_icon_card.png",
        [Chest] = "res://images/ui/reward_screen/reward_icon_shared_relic.png",
        [DebuffIntent] = "res://images/packed/intents/intent_debuff.png",
        [MapMarker] = "res://images/packed/map/icons/map_spoils_map_marker.png",
        [HighFive] = Powers + "tag_team_power.png",
        [Explosion] = Powers + "vigor_power.png",
        [Rampart] = Powers + "rampart_power.png",
        [BrokenBlock] = Powers + "no_block_power.png",
        [Guarded] = Powers + "guarded_power.png",
        [Leader] = Powers + "leadership_power.png",
        [Wrench] = Powers + "tools_of_the_trade_power.png",
        [Factory] = Powers + "smokestack_power.png",
        [Dummy] = Powers + "battleworn_dummy_time_limit_power.png",
        [Blur] = Powers + "blur_power.png",
        [Lightning] = Powers + "energy_next_turn_power.png",
        [Clipboard] = Powers + "coordinate_power.png",
        [Pawn] = Powers + "stratagem_power.png",
        [Shell] = Powers + "hardened_shell_power.png",
        [Peak] = Powers + "conqueror_power.png",
        [Tainted] = Powers + "tainted_power.png",
    };

    private static readonly Dictionary<string, Texture2D?> Cache = new();

    public static Texture2D? Get(string name)
    {
        // The game disposes pictures it unloads, which kills our handle too: look a dead one up again.
        if (Cache.TryGetValue(name, out Texture2D? cached) && (cached == null || GodotObject.IsInstanceValid(cached))) return cached;
        Texture2D? texture = null;
        try
        {
            if (Paths.TryGetValue(name, out string? path) && ResourceLoader.Exists(path))
                texture = Trim(ResourceLoader.Load<Texture2D>(path));
        }
        catch (Exception)
        {
            // drawn without it
        }
        Cache[name] = texture;
        return texture;
    }

    private static readonly Dictionary<ulong, (Color Left, Color Right)?> Edges = new();

    /// <summary>
    /// The average colour of a picture's outermost two pixel columns on each side, to fill the strips beside a
    /// zoomed-out portrait; read once per texture. Null when its pixels can't be read or its edges are transparent.
    /// </summary>
    public static (Color Left, Color Right)? EdgeColours(Texture2D texture)
    {
        ulong id = texture.GetInstanceId();
        if (Edges.TryGetValue(id, out (Color, Color)? cached)) return cached;
        (Color, Color)? edges = null;
        try
        {
            Image? image = texture.GetImage();
            if (image != null && !image.IsEmpty())
            {
                if (image.IsCompressed()) image.Decompress();
                int w = image.GetWidth(), h = image.GetHeight();
                if (w >= 4 && h > 0 && ColumnAverage(image, 0, h) is Color left && ColumnAverage(image, w - 2, h) is Color right)
                    edges = (left, right);
            }
        }
        catch (Exception)
        {
            // the strips fall back to the card's colour
        }
        Edges[id] = edges;
        return edges;
    }

    /// <summary>Two pixel columns from <paramref name="x0"/>, averaged by opacity; null if they're transparent.</summary>
    private static Color? ColumnAverage(Image image, int x0, int height)
    {
        float r = 0, g = 0, b = 0, weight = 0;
        for (int x = x0; x < x0 + 2; x++)
        for (int y = 0; y < height; y++)
        {
            Color p = image.GetPixel(x, y);
            r += p.R * p.A;
            g += p.G * p.A;
            b += p.B * p.A;
            weight += p.A;
        }
        return weight > height * 0.5f ? new Color(r / weight, g / weight, b / weight) : null;
    }

    /// <summary>
    /// An atlas sprite without the transparent margin the atlas packer gives back (the game positions its sprites
    /// with it); we size the picture itself, so a map icon fills its box instead of sitting small in the middle.
    /// </summary>
    public static Texture2D? Trim(Texture2D? texture) =>
        texture is AtlasTexture atlas && atlas.Margin.Size != Vector2.Zero
            ? new AtlasTexture { Atlas = atlas.Atlas, Region = atlas.Region, FilterClip = atlas.FilterClip }
            : texture;

    /// <summary>The map icon for a fight's room; an unknown room gets the plain monster icon.</summary>
    public static Texture2D? Room(string room) => Get(room switch
    {
        "elite" => Elite,
        "boss" => Boss,
        "unknown" => Unknown,
        _ => Monster,
    });
}
