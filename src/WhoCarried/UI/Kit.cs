using System.Globalization;
using Godot;
using WhoCarried.Core;

namespace WhoCarried.UI;

/// <summary>How a label is inked: plain, a thin dark outline, or the heavy outline and drop the game uses on numbers.</summary>
internal enum Ink { None, Soft, Strong }

/// <summary>
/// Builds the recap's controls at one scale. Layout is written in "design pixels" (the 1600×900 mockups); the kit
/// multiplies them by <see cref="S"/>, so a bigger screen gets bigger, still-sharp text rather than a stretched image.
/// Every size a kit method takes is in design pixels.
/// </summary>
internal sealed class Kit
{
    private readonly Func<string?, Texture2D?> _icons;

    public Kit(float scale, Func<string?, Texture2D?> icons)
    {
        S = scale;
        _icons = icons;
    }

    public float S { get; }

    public Func<string?, Texture2D?> Icons => _icons;

    public float U(float px) => px * S;

    public Vector2 V(float x, float y) => new(x * S, y * S);

    /// <summary>A font size or line width: scaled and rounded, at least 1.</summary>
    public int F(float px) => Math.Max(1, (int)MathF.Round(px * S));

    public Texture2D? Icon(string? key)
    {
        try { return Alive(_icons(key)); }
        catch (Exception) { return null; }
    }

    /// <summary>The picture, or null if the game has disposed it (it frees a fight's pictures when the fight ends).</summary>
    public static Texture2D? Alive(Texture2D? texture) => texture != null && GodotObject.IsInstanceValid(texture) ? texture : null;

    public static string Num(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ text

    /// <param name="tracking">Extra space between letters, for small capitals.</param>
    public Label Text(string text, float size, Color color, bool bold = false, Ink ink = Ink.None, float tracking = 0)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        if (RecapTheme.Tracked(bold, tracking > 0 ? F(tracking) : 0) is Font font) label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", F(size));
        label.AddThemeColorOverride("font_color", color);
        Outline(label, ink);
        return label;
    }

    /// <summary>Bold text with the heavy outline: names, numbers, headings.</summary>
    public Label Strong(string text, float size, Color? color = null) => Text(text, size, color ?? RecapTheme.Text, true, Ink.Strong);

    /// <summary>Small capitals with letter spacing ("DAMAGE DEALT", "ACT 2").</summary>
    public Label Caps(string text, float size, Color color, float tracking, bool bold = false) =>
        Text(text.ToUpperInvariant(), size, color, bold, Ink.None, tracking);

    public void Outline(Label label, Ink ink)
    {
        if (ink == Ink.None) return;
        bool strong = ink == Ink.Strong;
        int size = F(strong ? 5 : 3);
        label.AddThemeConstantOverride("outline_size", size);
        label.AddThemeColorOverride("font_outline_color", RecapTheme.Ink);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, strong ? 0.4f : 0.35f));
        label.AddThemeConstantOverride("shadow_offset_x", F(strong ? 2 : 1));
        label.AddThemeConstantOverride("shadow_offset_y", F(strong ? 4 : 2));
        label.AddThemeConstantOverride("shadow_outline_size", size);
    }

    /// <summary>Shrinks the label's font until its text fits (long player names), then trims with an ellipsis.</summary>
    public Label Fit(Label label, float maxWidth, float minSize) => FitScaled(label, U(maxWidth), F(minSize));

    public static Label FitScaled(Label label, float maxWidth, int minSize)
    {
        int size = label.GetThemeFontSize("font_size");
        while (size > minSize && Measure(label) > maxWidth)
        {
            size -= 1;
            label.AddThemeFontSizeOverride("font_size", size);
        }
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        // Trimming lets a label shrink to nothing; keep the room the text needs (up to the limit).
        label.CustomMinimumSize = new Vector2(Math.Min(maxWidth, Measure(label) + 2), label.CustomMinimumSize.Y);
        return label;
    }

    /// <summary>Width of a label's text in its own font (a label only knows its size once it's on screen).</summary>
    public static float Measure(Label label)
    {
        Font? font = label.GetThemeFont("font");
        int size = label.GetThemeFontSize("font_size");
        float outline = label.GetThemeConstant("outline_size");
        return (font == null ? label.Text.Length * size * 0.5f : font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, size).X) + outline;
    }

    /// <summary>
    /// A label cropped to its digits' height: the font's line box is much taller than the numerals, which would leave
    /// big gaps round huge numbers.
    /// </summary>
    public static Control Tight(Label label)
    {
        var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        label.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
        holder.AddChild(label);
        Retight(label, holder);
        return holder;
    }

    public static void Retight(Label label, Control holder)
    {
        int size = label.GetThemeFontSize("font_size");
        Font? font = label.GetThemeFont("font");
        float ascent = font?.GetAscent(size) ?? size * 0.93f;
        float capHeight = size * 0.72f;
        float width = Measure(label) + 2;
        label.CustomMinimumSize = new Vector2(width, 0);
        holder.CustomMinimumSize = new Vector2(width, capHeight + size * 0.14f);
        label.Position = new Vector2(0, capHeight - ascent);
    }

    // ------------------------------------------------------------------ layout

    public HBoxContainer Row(float gap)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", (int)MathF.Round(U(gap)));
        return row;
    }

    public VBoxContainer Column(float gap)
    {
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", (int)MathF.Round(U(gap)));
        return column;
    }

    public Control Gap(float width, float height) =>
        new() { CustomMinimumSize = V(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };

    public static Control Fill() =>
        new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };

    public static T Center<T>(T control) where T : Control
    {
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return control;
    }

    /// <summary>Places a control at design coordinates inside an absolutely laid-out parent.</summary>
    public T At<T>(T control, float x, float y, float width = -1, float height = -1) where T : Control
    {
        control.Position = V(x, y);
        if (width >= 0 || height >= 0)
        {
            Vector2 size = new(width >= 0 ? U(width) : control.Size.X, height >= 0 ? U(height) : control.Size.Y);
            control.Size = size;
            control.CustomMinimumSize = size;
        }
        return control;
    }

    /// <summary>An empty control of a fixed design size, for absolute layouts.</summary>
    public Control Box(float width, float height) =>
        new() { Size = V(width, height), CustomMinimumSize = V(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };

    // ------------------------------------------------------------------ pictures

    /// <summary>A texture fitted inside a box, keeping its shape; an empty box when there's no texture.</summary>
    public TextureRect Pic(Texture2D? texture, float width, float height, Color? tint = null)
    {
        // Ignore the texture's own size before sizing: otherwise the rect can't be made smaller than the texture.
        var rect = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = Alive(texture),
            CustomMinimumSize = V(width, height),
            Size = V(width, height),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (tint is Color c) rect.SelfModulate = c;
        return rect;
    }

    /// <summary>A texture stretched to exactly fill a box (card frames, banners, the top bar).</summary>
    public TextureRect Stretch(Texture2D? texture, float width, float height, Color? tint = null)
    {
        TextureRect rect = Pic(texture, width, height, tint);
        rect.StretchMode = TextureRect.StretchModeEnum.Scale;
        return rect;
    }

    private static Shader? _tintShader;

    /// <summary>
    /// A texture stretched to a box and dyed with a colour the way the mockups did it (a multiply layer masked by
    /// the texture): solid parts multiply, and half-see-through dark parts (a card's text box) pick up a dark wash
    /// of the colour instead of staying black.
    /// </summary>
    public TextureRect Dyed(Texture2D? texture, float width, float height, Color color)
    {
        _tintShader ??= new Shader
        {
            Code = """
                shader_type canvas_item;
                uniform vec4 tint : source_color = vec4(1.0);
                void fragment() {
                    vec4 f = texture(TEXTURE, UV);
                    float a = f.a;
                    vec3 co = a * (1.0 - a) * tint.rgb + a * a * f.rgb * tint.rgb + (1.0 - a) * a * f.rgb;
                    float ao = 2.0 * a - a * a;
                    COLOR = vec4(ao > 0.0 ? co / ao : vec3(0.0), ao * COLOR.a);
                }
                """,
        };
        TextureRect rect = Stretch(texture, width, height);
        var material = new ShaderMaterial { Shader = _tintShader };
        material.SetShaderParameter("tint", color);
        rect.Material = material;
        return rect;
    }

    /// <summary>
    /// A texture cropped to cover a box, like CSS object-fit: cover; <paramref name="focusY"/> picks the band kept.
    /// <paramref name="zoom"/> below 1 shows more of the picture (0: all of it, like contain); the strips that leaves
    /// either side are filled with the colours <paramref name="sides"/> gives for the picture.
    /// </summary>
    public Control Cover(Texture2D? texture, float width, float height, float focusY = 0.5f, float zoom = 1,
                         Func<Texture2D, (Color Left, Color Right)>? sides = null)
    {
        Control box = Box(width, height);
        texture = Alive(texture);
        if (texture == null) return box;
        // Held by a hidden picture node and read back at each draw: the game may dispose our handle to the texture
        // after a fight, but the node keeps the picture itself alive and hands out a fresh handle.
        var keep = new TextureRect { Texture = texture, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddChild(keep);
        box.Draw += () =>
        {
            if (Alive(keep.Texture) is not Texture2D picture) return;
            Vector2 size = box.Size, source = picture.GetSize();
            if (PictureFit.Place(size.X, size.Y, source.X, source.Y, zoom, focusY) is not PictureFit.Placement place) return;
            if (sides != null && place.Dest.W < size.X - 0.5f)
            {
                (Color left, Color right) = sides(picture);
                box.DrawRect(new Rect2(0, 0, size.X / 2, size.Y), left);
                box.DrawRect(new Rect2(size.X / 2, 0, size.X - size.X / 2, size.Y), right);
            }
            box.DrawTextureRectRegion(picture, ToRect(place.Dest), ToRect(place.Source));
        };
        box.Resized += box.QueueRedraw;
        return box;
    }

    private static Rect2 ToRect(PictureFit.Rect r) => new(r.X, r.Y, r.W, r.H);

    /// <summary>A soft radial glow of a colour: behind award icons, the table's spotlight.</summary>
    public TextureRect Glow(Color inner, Color outer, float width, float height, Vector2? center = null)
    {
        var gradient = new Gradient();
        gradient.SetColor(0, inner);
        gradient.SetColor(1, outer);
        Vector2 from = center ?? new Vector2(0.5f, 0.55f);
        return new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = gradient, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = from, FillTo = from + new Vector2(0.5f, 0.5f),
                Width = 128, Height = 128,
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size = V(width, height),
            CustomMinimumSize = V(width, height),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    /// <summary>A game badge as its end screen draws it: the picture in its gold, silver or bronze holder.</summary>
    public Control Medal(BadgeInfo badge, float size)
    {
        Control holder = Box(size, size);
        Texture2D? rim = Icon(badge.BaseKey), picture = Icon(badge.IconKey);
        if (rim == null && picture == null)
        {
            holder.AddChild(Swatch(RecapTheme.MedalColor(badge.Rarity), size, size, size / 2));
            return holder;
        }
        if (rim != null) holder.AddChild(Pic(rim, size, size));
        if (picture != null) holder.AddChild(At(Pic(picture, size * 0.72f, size * 0.72f), size * 0.14f, size * 0.14f));
        return holder;
    }

    /// <summary>
    /// A damage source's picture in a small rounded frame of its type's colour: a card's portrait fills it, an icon
    /// sits in the middle. Without a picture, the stats screen's cards icon, dimmed.
    /// </summary>
    public Control Thumb(string? artKey, string kind, float width, float height, float ring = 2.5f)
    {
        Texture2D? art = Icon(artKey);
        bool portrait = art != null && artKey != null && artKey.StartsWith(SourceArt.Card, StringComparison.Ordinal);
        return ThumbOf(art, portrait, RecapTheme.SourceColor(kind), width, height, ring);
    }

    /// <summary>A picture in a small rounded frame: <paramref name="cover"/> fills it (card art), otherwise it sits inside.</summary>
    public Control ThumbOf(Texture2D? art, bool cover, Color ring, float width, float height, float ringWidth = 2.5f)
    {
        var frame = new Panel { CustomMinimumSize = V(width, height), Size = V(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };
        StyleBoxFlat box = RecapTheme.Box(RecapTheme.Inset, U(5), ring, U(ringWidth));
        box.SetExpandMarginAll(U(ringWidth));
        box.ShadowColor = new Color(0, 0, 0, 0.45f);
        box.ShadowSize = F(4);
        box.ShadowOffset = new Vector2(0, U(3));
        frame.AddThemeStyleboxOverride("panel", box);
        if (art != null && cover)
            frame.AddChild(Cover(art, width, height, 0.35f));
        else if (art != null)
            frame.AddChild(At(Pic(art, width * 0.7f, height * 0.7f), width * 0.15f, height * 0.15f));
        else
            frame.AddChild(At(Pic(GameArt.Get(GameArt.Cards), width * 0.7f, height * 0.7f, new Color(1, 1, 1, 0.6f)), width * 0.15f, height * 0.15f));
        return frame;
    }

    public Panel Swatch(Color color, float width, float height, float radius)
    {
        var swatch = new Panel { CustomMinimumSize = V(width, height), Size = V(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };
        swatch.AddThemeStyleboxOverride("panel", RecapTheme.Box(color, U(radius)));
        return swatch;
    }

    // ------------------------------------------------------------------ panels

    /// <summary>The game's hover-tip look: a dark blue panel with a slate border.</summary>
    public StyleBoxFlat TipBox(float padX = 16, float padY = 12, Color? edge = null, float alpha = 0.92f)
    {
        StyleBoxFlat box = RecapTheme.Box(new Color(RecapTheme.Tip, alpha), U(10), edge ?? RecapTheme.TipEdge, U(2), U(padX), U(padY));
        box.ShadowColor = new Color(0, 0, 0, 0.4f);
        box.ShadowSize = F(14);
        box.ShadowOffset = new Vector2(0, U(8));
        return box;
    }

    public PanelContainer Tip(float padX = 16, float padY = 12, Color? edge = null, float alpha = 0.92f)
    {
        var tip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        tip.AddThemeStyleboxOverride("panel", TipBox(padX, padY, edge, alpha));
        return tip;
    }

    /// <summary>A section heading: an icon, the title, and an optional muted note after it.</summary>
    public HBoxContainer Heading(string title, Texture2D? icon, string? note = null, float size = 22)
    {
        HBoxContainer row = Row(10);
        if (icon != null) row.AddChild(Center(Pic(icon, size * 1.27f, size * 1.27f)));
        row.AddChild(Center(Text(title, size, RecapTheme.Text, true, Ink.Soft)));
        if (!string.IsNullOrEmpty(note)) row.AddChild(Center(Text(note, size * 0.68f, RecapTheme.Muted)));
        return row;
    }

    /// <summary>A player's name in their colour with their character icon in front.</summary>
    public HBoxContainer Who(string name, string? iconKey, Color color, float size = 17)
    {
        HBoxContainer row = Row(8);
        if (Icon(iconKey) is Texture2D icon) row.AddChild(Center(Pic(icon, size * 1.3f, size * 1.3f)));
        row.AddChild(Center(Text(name, size, color, true, Ink.Soft)));
        return row;
    }
}
