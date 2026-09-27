using Godot;
using WhoCarried.Core;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// Debuffs as the game's keyword tooltips: each debuff's icon and name in gold, its stacks, what it did for the team,
/// and who applied how much; then what enemy debuffs cost each player.
/// </summary>
internal static class DebuffsTab
{
    public static Control Create(Kit k, RecapView view, Live live, PadTab? pad = null)
    {
        Control tab = k.Box(RecapPanel.DesignW, RecapPanel.DesignH);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Position = k.V(40, 140), Size = k.V(1530, 740),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        tab.AddChild(scroll);
        if (pad != null) pad.Scroll = PadTab.Scrolls(scroll, k.U(PadTab.ScrollStep));
        VBoxContainer content = k.Column(14);
        content.CustomMinimumSize = k.V(1522, 0);
        scroll.AddChild(content);
        // The headings' pictures aren't debuffs', which the cards below wear.
        content.AddChild(k.Heading(Loc.Text("WHO_CARRIED.debuffs.applied"), GameArt.Get(GameArt.DebuffIntent),
            Loc.Text("WHO_CARRIED.debuffs.hint")));
        content.AddChild(Applied(k, view, 1522, 3, live));
        // What enemy debuffs cost you: left out while nobody has paid anything (and for runs logged before it was tracked).
        Control gap = k.Gap(0, 8), heading = k.Heading(Loc.Text("WHO_CARRIED.debuffs.cost"), GameArt.Get(GameArt.Tainted)),
            costs = Costs(k, view, 1522, live);
        foreach (Control part in new[] { gap, heading, costs }) content.AddChild(part);
        void Show(RecapView v)
        {
            bool any = v.Debuffs.Costs.Any(r => r.Lines.Count > 0);
            gap.Visible = heading.Visible = costs.Visible = any;
        }
        Show(view);
        live.On(Show);
        return tab;
    }

    /// <summary>The debuff tooltips, dealt into columns shortest-first so the columns end level.</summary>
    public static Control Applied(Kit k, RecapView view, float width, int columns, Live? live)
    {
        VBoxContainer holder = k.Column(0);
        HBoxContainer row = k.Row(18);
        holder.AddChild(row);
        Label empty = k.Text(Loc.Text("WHO_CARRIED.empty.debuffs"), 16, RecapTheme.Muted);
        holder.AddChild(empty);
        float colW = (width - 18 * (columns - 1)) / columns;
        string shown = "";
        var updaters = new List<Action<DebuffGroup>>();

        static string Signature(DebuffsView d) => string.Join("|", d.Applied.Select(g =>
            $"{g.IconKey}:{g.BonusTotal > 0}:{g.PreventedTotal > 0}:{string.Join(",", g.Bars.Select(b => b.PlayerLabel + (b.Bonus > 0) + (b.Prevented > 0)))}"));

        void Apply(RecapView v)
        {
            DebuffsView d = v.Debuffs;
            empty.Visible = d.Applied.Count == 0;
            string signature = Signature(d);
            if (signature == shown)
            {
                for (int i = 0; i < d.Applied.Count && i < updaters.Count; i++) updaters[i](d.Applied[i]);
                return;
            }
            shown = signature;
            updaters.Clear();
            foreach (Node child in row.GetChildren())
            {
                row.RemoveChild(child);
                child.QueueFree();
            }
            var stacks = new List<(VBoxContainer Column, float Height)>();
            for (int c = 0; c < columns; c++)
            {
                VBoxContainer column = k.Column(14);
                column.CustomMinimumSize = k.V(colW, 0);
                row.AddChild(column);
                stacks.Add((column, 0));
            }
            foreach (DebuffGroup group in d.Applied)
            {
                (Control card, Action<DebuffGroup> update) = Card(k, group, colW);
                int shortest = Enumerable.Range(0, columns).OrderBy(c => stacks[c].Height).First();
                stacks[shortest].Column.AddChild(card);
                float height = 70 + group.Bars.Count * 26 + (group.BonusTotal > 0 ? 24 : 0) + (group.PreventedTotal > 0 ? 24 : 0);
                stacks[shortest] = (stacks[shortest].Column, stacks[shortest].Height + height);
                updaters.Add(update);
            }
        }
        Apply(view);
        live?.On(Apply);
        return holder;
    }

    private static (Control, Action<DebuffGroup>) Card(Kit k, DebuffGroup group, float width)
    {
        PanelContainer tip = k.Tip(16, 12);
        tip.CustomMinimumSize = k.V(width, 0);
        VBoxContainer column = k.Column(4);
        tip.AddChild(column);

        HBoxContainer title = k.Row(10);
        title.AddChild(Kit.Center(k.Pic(k.Icon(group.IconKey), 34, 34)));
        Label name = k.Text(group.Label, 22, RecapTheme.Gold, true, Ink.Soft);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.AddChild(Kit.Center(name));
        Label stacks = k.Text("", 14, RecapTheme.Muted);
        title.AddChild(Kit.Center(stacks));
        column.AddChild(title);

        (Control bonusLine, Label bonus) = Sentence(k, RecapTheme.Gold);
        column.AddChild(bonusLine);
        (Control keptLine, Label kept) = Sentence(k, RecapTheme.Teal);
        column.AddChild(keptLine);

        var bars = new List<(LiveNumber Amount, LiveBar Bar, Label Extra)>();
        foreach (DebuffBar bar in group.Bars)
        {
            Color color = RecapTheme.Accent(bar.ColorHex);
            HBoxContainer line = k.Row(8);
            line.CustomMinimumSize = k.V(0, 26);
            line.AddChild(Kit.Center(k.Pic(k.Icon(bar.IconKey), 22, 22)));
            Label who = k.Text(bar.PlayerLabel, 15, RecapTheme.Text, true, Ink.Soft);
            who.CustomMinimumSize = k.V(104, 0);
            who.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            line.AddChild(Kit.Center(who));
            var fill = new LiveBar(bar.Fraction, color, k.U(width - 32 - 22 - 104 - 40 - 54 - 32), k.U(7));
            line.AddChild(fill.Control);
            Label amountLabel = k.Text("", 16, RecapTheme.Text, true, Ink.Soft);
            amountLabel.HorizontalAlignment = HorizontalAlignment.Right;
            amountLabel.CustomMinimumSize = k.V(40, 0);
            var amount = new LiveNumber(amountLabel, bar.Amount);
            line.AddChild(Kit.Center(amount.Control));
            Label extra = k.Text("", 15, RecapTheme.Gold, true);
            extra.HorizontalAlignment = HorizontalAlignment.Right;
            extra.CustomMinimumSize = k.V(54, 0);
            line.AddChild(Kit.Center(extra));
            column.AddChild(line);
            bars.Add((amount, fill, extra));
        }

        void Update(DebuffGroup g)
        {
            stacks.Text = Loc.Text("WHO_CARRIED.debuffs.stacks", Kit.Num(g.Total));
            bonus.Text = Loc.Text("WHO_CARRIED.debuffs.bonus_sentence", Kit.Num(g.BonusTotal));
            bonusLine.Visible = g.BonusTotal > 0;
            kept.Text = Loc.Text("WHO_CARRIED.debuffs.prevented_sentence", Kit.Num(g.PreventedTotal));
            keptLine.Visible = g.PreventedTotal > 0;
            for (int i = 0; i < g.Bars.Count && i < bars.Count; i++)
            {
                DebuffBar b = g.Bars[i];
                bars[i].Amount.Set(b.Amount);
                bars[i].Bar.Set(b.Fraction);
                // Vulnerable: extra damage teammates dealt (gold). Weak: damage kept off the team (teal).
                (string text, Color tone) = b.Bonus > 0 ? ($"+{Kit.Num(b.Bonus)}", RecapTheme.Gold)
                    : b.Prevented > 0 ? ($"−{Kit.Num(b.Prevented)}", RecapTheme.Teal) : ("", RecapTheme.Faint);
                bars[i].Extra.Text = text;
                bars[i].Extra.AddThemeColorOverride("font_color", tone);
            }
        }
        Update(group);
        return (tip, Update);
    }

    /// <summary>"Teammates dealt +2,670 extra damage because of it.", with the number picked out.</summary>
    private static (Control Line, Label Number) Sentence(Kit k, Color tone)
    {
        HBoxContainer line = k.Row(0);
        Label number = k.Text("", 15, tone, true);
        line.AddChild(number);
        return (line, number);
    }

    /// <summary>Per player: what enemy debuffs cost them (extra damage taken, damage not dealt, block not gained).</summary>
    public static Control Costs(Kit k, RecapView view, float width, Live? live)
    {
        int n = Math.Max(1, view.Debuffs.Costs.Count);
        float cardW = (width - 18 * (n - 1)) / n;
        HBoxContainer row = k.Row(18);

        (Control, Action<CostLine>) Line(CostLine l)
        {
            HBoxContainer line = k.Row(8);
            Label debuff = k.Text(l.Debuff, 14, RecapTheme.Text);
            debuff.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            debuff.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            line.AddChild(Kit.Center(debuff));
            (string key, Color tone) = l.Effect switch
            {
                RunStats.CostTaken => ("WHO_CARRIED.debuffs.extra_taken", RecapTheme.Taken),
                RunStats.CostDealt => ("WHO_CARRIED.debuffs.less_dealt", RecapTheme.Gold),
                RunStats.CostBlock => ("WHO_CARRIED.debuffs.less_block", RecapTheme.Blocked),
                _ => ("", RecapTheme.Text),
            };
            var amount = new LiveNumber(k.Text("", 17, tone, true), l.Amount,
                format: value => key.Length == 0 ? Kit.Num(value) : Loc.Text(key, Kit.Num(value)));
            line.AddChild(Kit.Center(amount.Control));
            return (line, it => amount.Set(it.Amount));
        }

        (Control, Action<DebuffCostRow>) Card(DebuffCostRow r)
        {
            Color color = RecapTheme.Accent(r.ColorHex);
            PanelContainer tip = k.Tip(14, 9, new Color(color, 0.53f));
            tip.CustomMinimumSize = k.V(cardW, 0);
            VBoxContainer column = k.Column(2);
            column.AddChild(k.Who(r.Label, r.IconKey, color));
            Label none = k.Text(Loc.Text("WHO_CARRIED.empty.cost"), 14, RecapTheme.Faint);
            column.AddChild(none);
            tip.AddChild(column);
            var lines = new KeyedRows<CostLine>(column, l => $"{l.Effect}:{l.Debuff}", Line, offset: 2);
            void Apply(DebuffCostRow it)
            {
                none.Visible = it.Lines.Count == 0;
                lines.Sync(it.Lines);
            }
            Apply(r);
            return (tip, Apply);
        }

        var cards = new KeyedRows<DebuffCostRow>(row, r => r.Label, Card);
        cards.Sync(view.Debuffs.Costs);
        live?.On(v => cards.Sync(v.Debuffs.Costs));
        return row;
    }
}
