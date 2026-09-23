using Godot;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Localization;

namespace WhoCarried.UI;

/// <summary>
/// The "Dealt" look: a dark card table, the game's card font (Kreon) with dark outlines, gold for what matters, and the
/// players' character colours on their cards. Colours and fonts only; <see cref="Kit"/> builds controls with them.
/// </summary>
internal static class RecapTheme
{
    // The table.
    public static readonly Color Table = new("111824");
    public static readonly Color TableLight = new("1c2738");
    public static readonly Color TableDark = new("070a0f");
    public static readonly Color Ink = new("0d1117");

    // Text.
    public static readonly Color Text = new("fbf6ec");
    public static readonly Color Muted = new("a9b4c2");
    public static readonly Color Faint = new("6f7c8c");
    public static readonly Color Caption = new("aeb7c3");

    // Meaning.
    public static readonly Color Gold = new("f3cf6a");
    public static readonly Color Green = new("7fe0a0");
    public static readonly Color Teal = new("6fd6c0");
    public static readonly Color Taken = new("ff7a6a");
    public static readonly Color Blocked = new("7cc0ff");
    public static readonly Color Healed = new("8fe07a");
    public static readonly Color Grey = new("8a8a8a");
    public static readonly Color Track = new(1, 1, 1, 0.08f);
    public static readonly Color Line = new(1, 1, 1, 0.07f);
    public static readonly Color Clear = new(0, 0, 0, 0);

    // Card types, for frames round source pictures and deck chips.
    public static readonly Color Attack = new("c2493d");
    public static readonly Color Skill = new("3f9a5a");
    public static readonly Color Power = new("4a78c0");
    public static readonly Color Curse = new("8a52c8");
    public static readonly Color Relic = new("c9a44a");
    public static readonly Color Plain = new("4b5563");

    // The game's hover tips.
    public static readonly Color Tip = new(14 / 255f, 20 / 255f, 31 / 255f, 0.92f);
    public static readonly Color TipEdge = new("3f5064");
    public static readonly Color Plaque = new("1c160f");

    /// <summary>Kept for the deck's text tiles (cards the game can't draw).</summary>
    public static readonly Color Inset = new("1a2230");

    private static Font? _regular, _bold;
    private static readonly Dictionary<(bool, int), Font?> Spaced = new();
    private static bool _loaded;
    private static string? _language;

    public static Font? Regular { get { Load(); return _regular; } }
    public static Font? Bold { get { Load(); return _bold; } }

    /// <summary>Kreon with extra space between letters (small capitals: "DAMAGE DEALT", the award plaque).</summary>
    public static Font? Tracked(bool bold, int spacing)
    {
        Load();
        if (spacing <= 0) return bold ? _bold : _regular;
        if (Spaced.TryGetValue((bold, spacing), out Font? cached) && (cached == null || GodotObject.IsInstanceValid(cached))) return cached;
        Font? font = (bold ? _bold : _regular) is Font baseFont ? new FontVariation { BaseFont = baseFont, SpacingGlyph = spacing } : null;
        Spaced[(bold, spacing)] = font;
        return font;
    }

    public static Color FromHex(string hex)
    {
        try { return Color.FromHtml(hex); }
        catch (Exception) { return Grey; }
    }

    /// <summary>
    /// A player colour made readable on the dark table, for text and thin bars: dark colours (The Tailor's brown) are
    /// lightened just enough. Card frames keep the true colour.
    /// </summary>
    public static Color Accent(string hex) => FromHex(Core.ColourMath.Readable(hex));

    /// <summary>A badge's name colour, and a plain medal when its picture can't be loaded.</summary>
    public static Color MedalColor(string rarity) => rarity switch
    {
        "gold" => Gold,
        "silver" => new Color("d4dde8"),
        "bronze" => new Color("e0a46c"),
        _ => Muted,
    };

    /// <summary>Card-name colour by rarity, echoing the game's card banners.</summary>
    public static Color RarityColor(string rarity) => rarity switch
    {
        "Basic" => Muted,
        "Common" => Text,
        "Uncommon" => Blocked,
        "Rare" => Gold,
        "Curse" => new Color("c49bf0"),
        "Status" => Faint,
        _ => Teal,
    };

    /// <summary>The frame colour of a source's picture: its card type, relic gold, power blue, or plain.</summary>
    public static Color SourceColor(string kind) => kind switch
    {
        "Card" or "Pet" => Attack,
        "Relic" => Relic,
        "Power" or "Orb" => Power,
        "Potion" => Teal,
        _ => Plain,
    };

    public static Color TypeColor(string type) => type switch
    {
        "Attack" => Attack,
        "Skill" => Skill,
        "Power" => Power,
        "Curse" => Curse,
        _ => Plain,
    };

    public static StyleBoxFlat Box(Color background, float radius, Color? border = null, float borderWidth = 0,
                                   float padX = 0, float padY = 0)
    {
        var box = new StyleBoxFlat { BgColor = background, AntiAliasing = true };
        box.SetCornerRadiusAll((int)Math.Round(radius));
        if (border is Color b && borderWidth > 0)
        {
            box.BorderColor = b;
            box.SetBorderWidthAll(Math.Max(1, (int)Math.Round(borderWidth)));
        }
        box.ContentMarginLeft = padX;
        box.ContentMarginRight = padX;
        box.ContentMarginTop = padY;
        box.ContentMarginBottom = padY;
        return box;
    }

    /// <summary>
    /// Loads Kreon once, and again if the game has disposed it: the fonts are shared with the game, which disposes
    /// resources it unloads (after a fight), and a dead font would stop every label from being made.
    /// </summary>
    private static void Load()
    {
        bool alive = (_regular == null || GodotObject.IsInstanceValid(_regular)) && (_bold == null || GodotObject.IsInstanceValid(_bold));
        string language = LocManager.Instance?.Language ?? "eng";
        if (_loaded && alive && _language == language) return;
        _language = language;
        _loaded = true;
        Spaced.Clear(); // the letter-spaced variants were built on the old fonts
        _regular = LoadSubstituteFont(language, FontType.Regular) ?? LoadFont("res://themes/kreon_regular_shared.tres", "res://fonts/kreon_regular.ttf");
        _bold = LoadSubstituteFont(language, FontType.Bold) ?? LoadFont("res://themes/kreon_bold_shared.tres", "res://fonts/kreon_bold.ttf") ?? _regular;
    }

    private static Font? LoadSubstituteFont(string language, FontType type)
    {
        try
        {
            Font? font = FontManager.GetSubstituteFont(language, type);
            if (font == null || GodotObject.IsInstanceValid(font)) return font;
            // The game's cache may still hold a font released between scenes.
            FontManager.ClearCache();
            font = FontManager.GetSubstituteFont(language, type);
            return font != null && GodotObject.IsInstanceValid(font) ? font : null;
        }
        catch (Exception)
        {
            return null; // Keep the same safe resource fallback as the original theme.
        }
    }

    private static Font? LoadFont(params string[] paths)
    {
        foreach (string path in paths)
        {
            try
            {
                if (ResourceLoader.Exists(path) && ResourceLoader.Load<Font>(path) is Font font) return font;
            }
            catch (Exception)
            {
                // try the next candidate
            }
        }
        return null;
    }
}
