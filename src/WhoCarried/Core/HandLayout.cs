namespace WhoCarried.Core;

/// <summary>
/// The scoreboard's hand of player cards as plain maths in design pixels: where each card lies, and how high a card
/// reaches once it's tilted, scaled and lifted. UI/CardFace draws cards to the shape described here.
/// </summary>
public static class HandLayout
{
    /// <summary>Where a card lies: its top-left corner before tilting, its tilt in degrees, and its scale.</summary>
    public readonly record struct Slot(float X, float Y, float Tilt, float Scale);

    /// <summary>Card height over width, like the game's cards.</summary>
    public const float Aspect = 1.41f;

    /// <summary>Tilts and scales turn round a point half across the card and this many heights down (one wrist).</summary>
    public const float PivotDown = 1.2f;

    /// <summary>Sizes inside a card are in "em": its width over this.</summary>
    public const float EmsAcross = 18.25f;

    /// <summary>The rank gem overhangs the top-left corner: its offset from the corner and its size, in em.</summary>
    public const float GemLeft = -1.1f, GemTop = -1.25f, GemSize = 4.6f;

    /// <summary>A hovered card rises this far and grows by this factor round its middle.</summary>
    public const float HoverRise = 8f, HoverGrow = 1.03f;

    /// <summary>
    /// The panel's tabs (RecapPanel.Nav): buttons from y 88 to <see cref="TabsBottom"/>, a thin line at
    /// <see cref="TabLine"/>. Cards keep <see cref="TabGap"/> clear of the buttons; the line they may touch.
    /// </summary>
    public const float TabsTop = 88f, TabsBottom = 118f, TabLine = 124f, TabGap = 4f;

    /// <summary>Where the hand would sit if nothing had to clear the tabs.</summary>
    public const float PreferredTop = 148f;

    public static (float Width, Slot[] Slots) Layout(int n, float handWidth, float top)
    {
        n = Math.Max(1, n);
        float w = n == 1 ? 330 : n == 2 ? 312 : 292;
        float step = n == 1 ? 0 : n == 2 ? 400 : n <= 4 ? 256 : (handWidth - 120 - w) / (n - 1);
        float spread = (n - 1) * step + w;
        float left = n == 1 ? 190 : (handWidth - spread) / 2;
        float[] tilt = n switch
        {
            1 => new[] { 0f },
            2 => new[] { -3.5f, 3.5f },
            3 => new[] { -4f, 0f, 4f },
            4 => new[] { -5f, -1.7f, 1.7f, 5f },
            _ => Enumerable.Range(0, n).Select(i => -6f + 12f * i / (n - 1)).ToArray(),
        };
        var slots = new Slot[n];
        for (int i = 0; i < n; i++)
        {
            float drop = n >= 3 ? (Math.Abs(tilt[i]) > 3 ? 30 : 4) : 10;
            float y = top + (n == 2 ? 24 : 0) + drop - (i == 0 && n > 2 ? 16 : 0);
            slots[i] = new Slot(left + i * step, y, tilt[i], i == 0 && n > 1 ? 1.06f : 1f);
        }
        return (w, slots);
    }

    /// <summary>
    /// Where the point (<paramref name="x"/>, <paramref name="y"/>) of a card (its own design pixels, before tilting)
    /// ends up on the table: the tilt and scale turn round the wrist point.
    /// </summary>
    public static (float X, float Y) Point(float width, Slot slot, float x, float y)
    {
        float pivotX = width / 2, pivotY = width * Aspect * PivotDown;
        (float sin, float cos) = SinCos(slot.Tilt);
        float dx = x - pivotX, dy = y - pivotY;
        return (slot.X + pivotX + slot.Scale * (dx * cos - dy * sin), slot.Y + pivotY + slot.Scale * (dx * sin + dy * cos));
    }

    /// <summary>
    /// The same card hovered: risen by <see cref="HoverRise"/> and grown by <see cref="HoverGrow"/> round its middle.
    /// It still turns round the wrist point, so it's also shifted by however far growing would move its middle.
    /// </summary>
    public static Slot Hovered(float width, Slot slot)
    {
        (float dx, float dy) = HoverShift(width, slot.Tilt, slot.Scale);
        return slot with { X = slot.X + dx, Y = slot.Y + dy, Scale = slot.Scale * HoverGrow };
    }

    /// <summary>How far hovering moves a card's slot: see <see cref="Hovered"/>. UI/CardFace applies the same shift.</summary>
    public static (float X, float Y) HoverShift(float width, float tilt, float scale)
    {
        float middleBelowPivot = width * Aspect * (0.5f - PivotDown); // the middle, measured from the wrist point
        (float sin, float cos) = SinCos(tilt);
        float grow = (1 - HoverGrow) * scale;
        return (grow * -middleBelowPivot * sin, grow * middleBelowPivot * cos - HoverRise);
    }

    /// <summary>
    /// The highest point (smallest y) a card of <paramref name="width"/> reaches at this slot, at rest or hovered: its
    /// top corners or its gem's.
    /// </summary>
    public static float Reach(float width, Slot slot, bool hovered)
    {
        if (hovered) slot = Hovered(width, slot);
        float em = width / EmsAcross;
        (float X, float Y)[] corners =
        {
            (0, 0), (width, 0), (GemLeft * em, GemTop * em), ((GemLeft + GemSize) * em, GemTop * em),
        };
        return corners.Min(c => Point(width, slot, c.X, c.Y).Y);
    }

    /// <summary>
    /// The top for a hand of <paramref name="n"/> cards: <see cref="PreferredTop"/>, or as much lower as it takes for
    /// every card, even hovered, to stay <see cref="TabGap"/> clear of the tab buttons.
    /// </summary>
    public static float TopClearOfTabs(int n, float handWidth)
    {
        (float w, Slot[] slots) = Layout(n, handWidth, PreferredTop);
        float highest = slots.Min(s => Reach(w, s, hovered: true));
        return PreferredTop + Math.Max(0f, TabsBottom + TabGap - highest);
    }

    /// <summary>
    /// The top for a hand of <paramref name="n"/> cards at rest that sits as high as it can with every card and gem at or
    /// below <paramref name="line"/>: the copied picture's hand, which has no tabs to clear and never hovers.
    /// </summary>
    public static float TopBelow(float line, int n, float handWidth)
    {
        (float w, Slot[] slots) = Layout(n, handWidth, PreferredTop);
        float highest = slots.Min(s => Reach(w, s, hovered: false));
        return PreferredTop + (line - highest);
    }

    private static (float Sin, float Cos) SinCos(float degrees)
    {
        double radians = degrees * Math.PI / 180;
        return ((float)Math.Sin(radians), (float)Math.Cos(radians));
    }
}
