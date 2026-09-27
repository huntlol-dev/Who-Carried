using Godot;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// The recap's controls. The one action worth pressing gets a stone this mod draws (<see cref="HewnStoneArt"/>),
/// nine-sliced so it holds its shape at any width; the keys the bar names get caps; everything else is a painted word.
/// Nothing here loads a texture from the game, so nothing here can break when the game moves its art.
/// </summary>
internal static class HewnStone
{
    /// <summary>The slab's height in design pixels: centred on the tab band, resting on HandLayout.TabLine.</summary>
    public const float SlabHeight = 42;

    private static readonly Dictionary<HewnStoneArt.Palette, ImageTexture?> Stones = new();

    /// <summary>The stone in a palette, drawn once. Null if it couldn't be built — callers fall back to a plain box.</summary>
    private static ImageTexture? Texture(HewnStoneArt.Palette palette)
    {
        // The game disposes textures it unloads; a dead handle has to be redrawn like GameArt re-looks-up its own.
        if (Stones.TryGetValue(palette, out ImageTexture? stone) && (stone == null || GodotObject.IsInstanceValid(stone))) return stone;
        try
        {
            stone = HewnStoneArt.Draw(palette) is HewnStoneArt.Result tile
                ? ImageTexture.CreateFromImage(Image.CreateFromData(tile.Width, tile.Height, false, Image.Format.Rgba8, tile.Pixels))
                : null;
        }
        catch (Exception e)
        {
            stone = null;
            Tracker.LogError("drawing the button stone (a plain box instead)", e);
        }
        Stones[palette] = stone;
        return stone;
    }

    /// <summary>
    /// The stone as a style box. TextureMargin is in texture pixels and must not be scaled — that is the whole point
    /// of a nine-slice, and dropping it is what made the old buttons stretch. ContentMargin is on screen, so it is.
    /// </summary>
    private static StyleBox Box(Kit k, Color tint, HewnStoneArt.Palette palette)
    {
        if (Texture(palette) is Texture2D stone)
            return new StyleBoxTexture
            {
                Texture = stone,
                ModulateColor = tint,
                TextureMarginLeft = HewnStoneArt.Margin,
                TextureMarginRight = HewnStoneArt.Margin,
                TextureMarginTop = HewnStoneArt.Margin,
                TextureMarginBottom = HewnStoneArt.Margin,
                ContentMarginLeft = k.U(18),
                ContentMarginRight = k.U(18),
                ContentMarginTop = k.U(6),
                ContentMarginBottom = k.U(8),
            };
        Color edge = palette == HewnStoneArt.Palette.Default ? RecapTheme.Gold : RecapTheme.Muted;
        return RecapTheme.Box(RecapTheme.Table, k.U(4), edge, k.U(2), k.U(18), k.U(7));
    }

    /// <summary>
    /// The panel's real buttons: a stone that lights on hover and sinks when pressed. Bronze for the main action (Copy
    /// to clipboard), <see cref="HewnStoneArt.Palette.Plain"/> for the one beside it.
    /// </summary>
    public static Button Slab(Kit k, string text, float height, HewnStoneArt.Palette? palette = null)
    {
        HewnStoneArt.Palette stone = palette ?? HewnStoneArt.Palette.Default;
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            // Nothing is in the icon slot in mouse mode; PadHints puts the controller glyph there. Pinning its width is
            // what stops it inflating the button's height, the defect the old Save image button had. The IconMaxWidth
            // property isn't on this Godot binding, but its theme constant is — and ExpandIcon, the other way round,
            // reserves the space and then draws the glyph into nothing.
            ExpandIcon = false,
            CustomMinimumSize = new Vector2(0, k.U(height)),
        };
        if (RecapTheme.Bold is Font font) button.AddThemeFontOverride("font", font);
        button.AddThemeFontSizeOverride("font_size", k.F(19));
        button.AddThemeConstantOverride("icon_max_width", (int)k.F(26));
        button.AddThemeConstantOverride("h_separation", k.F(11));
        button.AddThemeConstantOverride("outline_size", k.F(3));
        button.AddThemeColorOverride("font_outline_color", RecapTheme.Ink);
        button.AddThemeColorOverride("font_color", RecapTheme.Text);
        button.AddThemeColorOverride("font_hover_color", new Color("fff7e6"));
        button.AddThemeColorOverride("font_pressed_color", RecapTheme.Muted);
        button.AddThemeColorOverride("font_hover_pressed_color", RecapTheme.Text);
        button.AddThemeStyleboxOverride("normal", Box(k, new Color(1, 1, 1), stone));
        button.AddThemeStyleboxOverride("hover", Box(k, new Color(1.12f, 1.12f, 1.12f), stone));
        button.AddThemeStyleboxOverride("pressed", Box(k, new Color(0.78f, 0.78f, 0.78f), stone));
        button.AddThemeStyleboxOverride("hover_pressed", Box(k, new Color(0.9f, 0.9f, 0.9f), stone));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.Size = new Vector2(button.GetCombinedMinimumSize().X, k.U(height));
        return button;
    }

    /// <summary>How far the shadow sits below the stone, so callers can leave room for it.</summary>
    public const float ShadowDrop = 3;

    /// <summary>The stone's shadow: the same silhouette behind it, so it rests on the panel instead of floating.</summary>
    public static Panel Shadow(Kit k, Control control, float drop = ShadowDrop)
    {
        var shadow = new Panel
        {
            Position = control.Position + k.V(0, drop),
            Size = control.Size,
            CustomMinimumSize = control.Size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        shadow.AddThemeStyleboxOverride("panel", Box(k, new Color(0.08f, 0.1f, 0.13f, 0.75f), HewnStoneArt.Palette.Default));
        // The stone can be re-fitted after it is built — a controller glyph widens it — and a shadow left at the old
        // width would show its edge. Follow.
        control.Resized += () =>
        {
            if (!GodotObject.IsInstanceValid(shadow)) return;
            shadow.Size = control.Size;
            shadow.CustomMinimumSize = control.Size;
        };
        return shadow;
    }

    /// <summary>
    /// Hover lifts the stone off its shadow, press drops it into it. The shadow stays where it is — that is what makes
    /// the movement read as the stone moving rather than the whole control sliding. <paramref name="rest"/> is where the
    /// stone lies, for a stone that's moved after this (one that follows its neighbour); by default, where it is now.
    /// </summary>
    public static void Lift(Button button, Kit k, Func<Vector2>? rest = null)
    {
        Vector2 now = button.Position;
        rest ??= () => now;
        bool down = false;
        void Place(float y) => button.Position = rest() + k.V(0, y);
        button.MouseEntered += () => Place(down ? 2 : -2);
        button.MouseExited += () => { down = false; Place(0); };
        button.ButtonDown += () => { down = true; Place(2); };
        button.ButtonUp += () => { down = false; Place(-2); };
    }

    public enum CapLook { Bound, Listening, Unbound }

    /// <summary>A key cap: how the bar names a key. Dark face, ink edge, a heavy bottom lip.</summary>
    public static Button Cap(Kit k, string text)
    {
        var cap = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = k.V(38, 30) };
        if (RecapTheme.Bold is Font font) cap.AddThemeFontOverride("font", font);
        cap.AddThemeFontSizeOverride("font_size", k.F(16));
        cap.AddThemeConstantOverride("outline_size", k.F(2));
        cap.AddThemeColorOverride("font_outline_color", RecapTheme.Ink);
        Dress(cap, k, CapLook.Bound);
        return cap;
    }

    /// <summary>Re-dresses a cap: a key on it, an empty socket waiting for one, or an empty socket with none.</summary>
    public static void Dress(Button cap, Kit k, CapLook look)
    {
        Color face = look == CapLook.Bound ? new Color("1f2d3a") : new Color("121b25");
        Color edge = look == CapLook.Listening ? RecapTheme.Gold : RecapTheme.Ink;
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
        {
            StyleBoxFlat box = RecapTheme.Box(face, k.U(4), edge, k.U(2), k.U(8), k.U(3));
            box.BorderWidthBottom = Math.Max(2, k.F(4));
            cap.AddThemeStyleboxOverride(state, box);
        }
        cap.AddThemeColorOverride("font_color", look == CapLook.Bound ? RecapTheme.Text : RecapTheme.Faint);
        cap.AddThemeColorOverride("font_hover_color", RecapTheme.Gold);
        cap.AddThemeColorOverride("font_pressed_color", RecapTheme.Gold);
    }

    /// <summary>A painted word that does something: the panel's default, used for everything that isn't the export.</summary>
    public static Button Word(Kit k, string text)
    {
        var word = new Button { Text = text, Flat = true, FocusMode = Control.FocusModeEnum.None };
        if (RecapTheme.Bold is Font font) word.AddThemeFontOverride("font", font);
        word.AddThemeFontSizeOverride("font_size", k.F(19));
        word.AddThemeConstantOverride("outline_size", k.F(3));
        word.AddThemeColorOverride("font_outline_color", RecapTheme.Ink);
        word.AddThemeColorOverride("font_color", RecapTheme.Text);
        word.AddThemeColorOverride("font_hover_color", RecapTheme.Gold);
        word.AddThemeColorOverride("font_pressed_color", RecapTheme.Muted);
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
            word.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        return word;
    }
}
