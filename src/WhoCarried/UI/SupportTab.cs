using Godot;
using WhoCarried.Core;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// Generated cards (including self-creation), followed by the separate teammate-gift categories.
/// </summary>
internal static class SupportTab
{
    public static Control Create(Kit k, RecapView view, Live live, PadTab? pad = null)
    {
        Control tab = k.Box(RecapPanel.DesignW, RecapPanel.DesignH);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            Position = k.V(24, 132), Size = k.V(1556, 652), MouseFilter = Control.MouseFilterEnum.Pass,
        };
        tab.AddChild(scroll);
        if (pad != null) pad.Scroll = PadTab.Scrolls(scroll, k.U(PadTab.ScrollStep));
        var inset = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        inset.AddThemeConstantOverride("margin_left", k.F(16));
        inset.AddThemeConstantOverride("margin_top", k.F(8));
        inset.AddThemeConstantOverride("margin_bottom", k.F(12));
        scroll.AddChild(inset);
        VBoxContainer content = k.Column(24);
        content.CustomMinimumSize = k.V(1522, 0);
        inset.AddChild(content);
        VBoxContainer creation = k.Column(14);
        creation.AddChild(k.Heading(Loc.Text("WHO_CARRIED.support.created_heading"), GameArt.Get(GameArt.Cards)));
        creation.AddChild(CreationPanels.Create(k, view, 1522, live));
        content.AddChild(creation);
        VBoxContainer shown = k.Column(14);
        shown.AddChild(k.Heading(Loc.Text("WHO_CARRIED.support.heading"), HeadingArt(k, view), Loc.Text("WHO_CARRIED.support.hint")));
        shown.AddChild(Cards(k, view, 1522, 3, live));
        content.AddChild(shown);

        Label empty = k.Text(Loc.Text("WHO_CARRIED.empty.support"), 18, RecapTheme.Muted);
        empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(empty);

        void Apply(RecapView v)
        {
            shown.Visible = v.HasSupport;
            creation.Visible = v.HasCardCreation;
            empty.Visible = !v.HasSupportContent;
        }
        Apply(view);
        live.On(Apply);
        return tab;
    }

    /// <summary>
    /// One kind of help: its name, colour, value, and the award its leader can win; and the name it goes by on the
    /// copied picture, when that differs.
    /// </summary>
    private sealed record Kind(string Words, Color Tone, Func<SupportRow, int> Value, string Award, string? GivenWords = null);

    private static readonly Kind[] Kinds =
    {
        new("WHO_CARRIED.support.energy", RecapTheme.Gold, r => r.Energy, AwardBuilder.Battery),
        new("WHO_CARRIED.support.cards", RecapTheme.Text, r => r.Cards, AwardBuilder.CarePackage),
        // On the copied picture the scoreboard cards' own Block chip (enemy block knocked off) sits just above.
        new("WHO_CARRIED.support.block", RecapTheme.Blocked, r => r.Block, AwardBuilder.Bodyguard, "WHO_CARRIED.support.block_given"),
        new("WHO_CARRIED.support.buffs", RecapTheme.Taken, r => r.Buffs, AwardBuilder.Coach),
        new("WHO_CARRIED.support.draws", RecapTheme.Teal, r => r.Draws, AwardBuilder.Playmaker),
    };

    /// <summary>The top-ranked player's energy gem, heading the support; the colourless one is a grey orb.</summary>
    public static Texture2D? HeadingArt(Kit k, RecapView view) =>
        k.Icon(RecapTexts.EnergyKey(view.Support.FirstOrDefault()?.IconKey)) ?? GameArt.Get(GameArt.Energy);

    /// <summary>How many kinds of help anyone gave: the number of cards <see cref="Cards"/> shows.</summary>
    public static int KindsGiven(RecapView view) => Kinds.Count(kind => Givers(view, kind).Count > 0);

    /// <summary>The players who gave this kind of help, most first.</summary>
    private static List<SupportRow> Givers(RecapView view, Kind kind) =>
        view.Support.Where(r => kind.Value(r) > 0).OrderByDescending(kind.Value).ToList();

    /// <summary>
    /// The cards in a grid, in the order of <see cref="Kinds"/>. Compact (the saved image) drops the award line and uses
    /// one row of cards across. The copied picture keeps the tab's sizes, but drops the award line
    /// (<paramref name="showAwards"/>) and names a kind as given where that reads better (<paramref name="givenTitles"/>).
    /// </summary>
    public static Control Cards(Kit k, RecapView view, float width, int columns, Live? live, bool compact = false,
                                bool showAwards = true, bool givenTitles = false)
    {
        float gap = compact ? 12 : 18;
        var grid = new GridContainer { Columns = columns, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", k.F(gap));
        grid.AddThemeConstantOverride("v_separation", k.F(gap));
        float cardW = (width - gap * (columns - 1)) / columns;
        string built = "";

        // Rebuilt only when which kinds show, or who gave them in what order, changes; otherwise the numbers just move.
        static string Signature(RecapView v) =>
            string.Join("|", Kinds.Select(kind => string.Join(",", Givers(v, kind).Select(r => r.Label))));

        var updaters = new List<Action<RecapView>>();
        void Apply(RecapView v)
        {
            string signature = Signature(v);
            if (signature == built)
            {
                foreach (Action<RecapView> update in updaters) update(v);
                return;
            }
            built = signature;
            updaters.Clear();
            foreach (Node child in grid.GetChildren())
            {
                grid.RemoveChild(child);
                child.QueueFree();
            }
            foreach (Kind kind in Kinds.Where(kind => Givers(v, kind).Count > 0))
            {
                (Control card, Action<RecapView> update) = Card(k, v, kind, cardW, compact, showAwards, givenTitles);
                grid.AddChild(card);
                updaters.Add(update);
            }
        }
        Apply(view);
        live?.On(Apply);
        return grid;
    }

    private static (Control, Action<RecapView>) Card(Kit k, RecapView view, Kind kind, float width, bool compact, bool showAwards,
                                                     bool givenTitles)
    {
        List<SupportRow> givers = Givers(view, kind);
        PanelContainer tip = compact ? k.Tip(10, 8) : k.Tip(16, 12);
        tip.CustomMinimumSize = k.V(width, 0);
        VBoxContainer column = k.Column(compact ? 2 : 4);
        tip.AddChild(column);

        HBoxContainer title = k.Row(compact ? 6 : 10);
        float art = compact ? 20 : 34;
        // The same picture as the kind's award; energy wears the gem of whoever gave the most.
        title.AddChild(Kit.Center(k.Pic(RecapTexts.AwardArt(k, kind.Award, givers[0].IconKey), art, art)));
        Label name = k.Text(Loc.Text(givenTitles && kind.GivenWords != null ? kind.GivenWords : kind.Words), compact ? 14 : 22,
            kind.Tone, true, Ink.Soft);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.AddChild(Kit.Center(name));
        Label total = k.Text("", compact ? 12 : 16, RecapTheme.Muted, true);
        title.AddChild(Kit.Center(total));
        column.AddChild(title);

        // Whoever gave the most won this kind's award, if they gave enough for it. Built only when it's shown: a
        // never-parented row and its two labels would leak three nodes per card otherwise.
        HBoxContainer? awardLine = null;
        Label? awardName = null, awardWinner = null;
        if (!compact && showAwards)
        {
            awardLine = k.Row(6);
            awardName = k.Text("", 14, RecapTheme.Gold, true);
            awardWinner = k.Text("", 14, RecapTheme.Muted, true);
            awardLine.AddChild(awardName);
            awardLine.AddChild(awardWinner);
            column.AddChild(awardLine);
        }

        float icon = compact ? 16 : 22, who = compact ? 62 : 104, amount = compact ? 30 : 48;
        var bars = new List<(LiveNumber Amount, LiveBar Bar)>();
        foreach (SupportRow row in givers)
        {
            Color color = RecapTheme.Accent(row.ColorHex);
            HBoxContainer line = k.Row(compact ? 5 : 8);
            line.CustomMinimumSize = k.V(0, compact ? 18 : 26);
            line.AddChild(Kit.Center(k.Pic(k.Icon(row.IconKey), icon, icon)));
            Label label = k.Text(row.Label, compact ? 12 : 15, color, true, Ink.Soft);
            label.CustomMinimumSize = k.V(who, 0);
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            line.AddChild(Kit.Center(label));
            float barW = width - (compact ? 20 : 32) - icon - who - amount - (compact ? 15 : 24);
            var fill = new LiveBar(1, color, k.U(barW), k.U(compact ? 5 : 7));
            line.AddChild(Kit.Center(fill.Control));
            Label amountLabel = k.Text("", compact ? 12 : 16, RecapTheme.Text, true, Ink.Soft);
            amountLabel.HorizontalAlignment = HorizontalAlignment.Right;
            amountLabel.CustomMinimumSize = k.V(amount, 0);
            var number = new LiveNumber(amountLabel, kind.Value(row));
            line.AddChild(Kit.Center(number.Control));
            column.AddChild(line);
            bars.Add((number, fill));
        }

        void Update(RecapView v)
        {
            List<SupportRow> now = Givers(v, kind);
            int most = now.Count > 0 ? kind.Value(now[0]) : 1;
            total.Text = Kit.Num(now.Sum(kind.Value));
            for (int i = 0; i < now.Count && i < bars.Count; i++)
            {
                bars[i].Amount.Set(kind.Value(now[i]));
                bars[i].Bar.Set((double)kind.Value(now[i]) / most);
            }
            if (awardLine != null && awardName != null && awardWinner != null)
            {
                Award? award = v.Awards.FirstOrDefault(a => a.Title == kind.Award);
                awardLine.Visible = award != null;
                awardName.Text = award == null ? "" : Loc.Text(award.Title);
                awardWinner.Text = award == null ? "" : "· " + award.PlayerName;
            }
        }
        Update(view);
        return (tip, Update);
    }
}
