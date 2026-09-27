namespace WhoCarried.Core;

/// <summary>
/// The copied picture's layout in the recap's design pixels: its size and scale, where its parts start, and how many
/// rows and columns they get. UI/ShareCard draws to it.
/// </summary>
public static class ShareLayout
{
    /// <summary>The design width, the recap's own.</summary>
    public const float Width = 1600;

    /// <summary>The shortest the picture gets (16:9); a taller bottom row makes it longer.</summary>
    public const float MinHeight = 900;

    /// <summary>Design pixels to picture pixels: 1600 becomes 1920, like a 1080p screenshot.</summary>
    public const float Scale = 1.2f;

    /// <summary>The picture's width in pixels.</summary>
    public static int PixelWidth => (int)MathF.Round(Width * Scale);

    /// <summary>The top bar's height (the exported image's bar), and the room the hand keeps below it.</summary>
    public const float BarHeight = 78, HandGap = 16;

    /// <summary>Where Top sources starts, and where the note under the hand sits.</summary>
    public const float SourcesTop = 96, NoteTop = 608;

    /// <summary>Where the bottom row (support, or the climb) starts, unless Top sources reaches lower.</summary>
    public const float BottomTop = 640;

    /// <summary>The margin either side of the full-width parts, and the gap round the footer and below Top sources.</summary>
    public const float Side = 40, Gap = 12;

    /// <summary>
    /// Top sources per player: as on the scoreboard (6 solo, 4 each for two players, 2 each for three or four), and 1
    /// each from five, so the column still ends above the bottom row.
    /// </summary>
    public static int SourceRows(int players) => players switch
    {
        <= 1 => 6,
        2 => 4,
        <= 4 => 2,
        _ => 1,
    };

    /// <summary>Columns for the support cards: one per kind given, but at least three, so one or two kinds keep a card's width.</summary>
    public static int SupportColumns(int kinds) => Math.Max(3, kinds);
}
