using Godot;
using WhoCarried.Core;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// The picture Copy to clipboard puts on the clipboard, for pasting into Discord: the scoreboard as players already
/// screenshot it, without the recap's controls. The bar with the date, the hand and its note, Top sources with each
/// player's gold earned, then what players gave their teammates (the climb when nobody gave anything), and the footer.
/// Laid out in the recap's design pixels (<see cref="ShareLayout"/>) and drawn at its scale; taller when the bottom row
/// needs it, never cut off.
/// </summary>
internal static class ShareCard
{
    public static int PixelWidth => ShareLayout.PixelWidth;

    /// <param name="date">The run's date (defaults to today, for a run just played).</param>
    public static Control Create(RecapView view, Func<string?, Texture2D?> icons, DateTime? date = null)
    {
        var k = new Kit(ShareLayout.Scale, icons);
        var page = new PanelContainer { CustomMinimumSize = k.V(ShareLayout.Width, ShareLayout.MinHeight), MouseFilter = Control.MouseFilterEnum.Ignore };
        // Solid all over: a see-through corner would show the chat's own background.
        page.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = RecapTheme.TableDark });
        page.AddChild(Table.Backdrop(k));

        VBoxContainer column = k.Column(0);
        page.AddChild(column);
        column.AddChild(Top(k, view, date ?? DateTime.Now));
        Control bottom = view.HasSupport
            ? Support(k, view)
            : Climb.Create(k, view, ShareLayout.Width - 2 * ShareLayout.Side, 204, 92, interactive: false, live: null);
        column.AddChild(Sides(k, bottom));
        // Takes up whatever the rest leaves of the picture's height, so the footer sits at its foot.
        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        MarginContainer footer = Sides(k, SummaryCard.Footer(k, view));
        footer.AddThemeConstantOverride("margin_top", k.F(ShareLayout.Gap));
        footer.AddThemeConstantOverride("margin_bottom", k.F(ShareLayout.Gap));
        column.AddChild(footer);
        return page;
    }

    /// <summary>The bar, the hand and its note, and Top sources: the scoreboard's parts, placed as the recap places them.</summary>
    private static Control Top(Kit k, RecapView view, DateTime date)
    {
        Control top = k.Box(ShareLayout.Width, ShareLayout.BottomTop);
        top.AddChild(SummaryCard.TopBar(k, view, date, ShareLayout.Width, party: true));

        List<BarRow> players = ScoreboardTab.Players(view);
        int n = players.Count;
        if (n > 0)
        {
            // The scoreboard's hand, held still, as high as it goes below the bar: there are no tabs to clear.
            float handTop = HandLayout.TopBelow(ShareLayout.BarHeight + ShareLayout.HandGap, n, 1200);
            (float w, HandLayout.Slot[] slots) = HandLayout.Layout(n, 1200, handTop);
            for (int i = 0; i < n; i++)
            {
                var card = new ScoreboardTab.PlayerCard(k, view, players[i], w, n, foilAt: 0.42f);
                card.Update(view, players[i], i, n);
                card.Place(slots[i], i + 1, animate: false);
                top.AddChild(card.Face.Root);
            }
            // A lone player's card has their run beside it, as on the scoreboard.
            if (n == 1) top.AddChild(k.At(ScoreboardTab.Story(k, view, null), 590, slots[0].Y + 17, 520, -1));
        }

        string note = SummaryCard.Note(view);
        if (note.Length > 0)
        {
            Label line = k.Text(note, 14, RecapTheme.Muted);
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            top.AddChild(k.At(line, ShareLayout.Side, ShareLayout.NoteTop, 1120, -1));
        }

        Control sources = ScoreboardTab.TopSources(k, view, ShareLayout.SourceRows(n), live: null, gold: true);
        top.AddChild(k.At(sources, 1222, ShareLayout.SourcesTop, 340, -1));
        // Top sources can reach lower than planned (Chinese lines are taller): the bottom row then starts below it.
        void Fit() => top.CustomMinimumSize = new Vector2(top.CustomMinimumSize.X,
            Math.Max(k.U(ShareLayout.BottomTop), sources.Position.Y + sources.GetCombinedMinimumSize().Y + k.U(ShareLayout.Gap)));
        sources.MinimumSizeChanged += Fit;
        Fit();
        return top;
    }

    /// <summary>
    /// What players gave their teammates: the Support tab's cards under its heading, without the tab's hint or award
    /// lines, and with block titled "Block given", since the cards above have their own Block chip.
    /// </summary>
    private static Control Support(Kit k, RecapView view)
    {
        VBoxContainer section = k.Column(14);
        section.AddChild(k.Heading(Loc.Text("WHO_CARRIED.support.heading"), SupportTab.HeadingArt(k, view), null, 26));
        section.AddChild(SupportTab.Cards(k, view, ShareLayout.Width - 2 * ShareLayout.Side,
            ShareLayout.SupportColumns(SupportTab.KindsGiven(view)), live: null, showAwards: false, givenTitles: true));
        return section;
    }

    /// <summary>The picture's side margins round a full-width part.</summary>
    private static MarginContainer Sides(Kit k, Control content)
    {
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", k.F(ShareLayout.Side));
        margin.AddThemeConstantOverride("margin_right", k.F(ShareLayout.Side));
        margin.AddChild(content);
        return margin;
    }
}
