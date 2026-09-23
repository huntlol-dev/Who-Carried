using Godot;
using WhoCarried.Core;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// "The climb": every fight as a stack of the players' damage standing on its map room icon, along a dotted path
/// like the map's, with act labels and the biggest fight named in the heading. Live: stacks grow and new fights slide
/// in. When interactive, hovering a fight shows who dealt what.
/// </summary>
internal static class Climb
{
    /// <summary>Set while the dev preview or a replay takes screenshots, so an idle cursor can't open a readout.</summary>
    public static bool IgnoreHover { get; set; }

    /// <summary>The gap between two players' segments in a stack, in design pixels.</summary>
    private const float SegmentGap = 2;

    /// <param name="width">Design width of the whole strip.</param>
    /// <param name="height">Design height, heading included.</param>
    /// <param name="barMax">Design height of the tallest stack.</param>
    public static Control Create(Kit k, RecapView view, float width, float height, float barMax, bool interactive, Live? live,
                                 PadTab? pad = null)
    {
        Control box = k.Box(width, height);
        HBoxContainer heading = k.Heading(Loc.Text("WHO_CARRIED.timeline.climb"), GameArt.Get(GameArt.Monster));
        Label summary = k.Text("", 15, RecapTheme.Muted);
        heading.AddChild(Kit.Center(summary));
        HBoxContainer tag = k.Row(5);
        tag.AddChild(Kit.Center(k.Pic(GameArt.Get(GameArt.Trophy), 18, 18)));
        Label tagText = k.Text("", 15, RecapTheme.Gold, true, Ink.Soft);
        tag.AddChild(Kit.Center(tagText));
        heading.AddChild(Kit.Center(tag));
        heading.AddChild(Kit.Fill());
        HBoxContainer swatches = k.Row(16);
        heading.AddChild(Kit.Center(swatches));
        box.AddChild(k.At(heading, 0, 0, width, 30));
        Label empty = k.Text(Loc.Text("WHO_CARRIED.empty.climb"), 16, RecapTheme.Muted);
        box.AddChild(k.At(empty, 0, 60));

        // Geometry, in design pixels relative to the strip, as in the mockup.
        float baseY = height - 30, node = 32, bossNode = 42, colW = 26;
        Control chart = k.Box(width, height);
        chart.MouseFilter = interactive ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
        box.AddChild(chart);

        RecapView current = view;
        int fights = 0, hovered = -1;
        float[][] from = Array.Empty<float[]>(), to = Array.Empty<float[]>();
        float fromMax = 1, toMax = 1, t = 1;
        Tween? tween = null;
        Color[] colors = Array.Empty<Color>();
        int[] order = Array.Empty<int>(); // series in scoreboard order, the leader at the bottom of each stack
        // Fonts and pictures are looked up at each draw: the game can dispose ones kept from earlier.
        Font? Caps() => RecapTheme.Tracked(true, k.F(1));

        float X(int i) => fights <= 1 ? width / 2 : 20 + i * (width - 40) / (fights - 1);
        float Value(int i, int s) => from[i][s] + (to[i][s] - from[i][s]) * t;
        float Scale() => fromMax + (toMax - fromMax) * t;

        chart.Draw += () =>
        {
            if (fights == 0) return;
            // Pictures are looked up at each draw: the game can dispose a handle kept from earlier.
            if (GameArt.Get(GameArt.Dot) is Texture2D dot)
            {
                Vector2 size = dot.GetSize();
                var middle = new Rect2(0, size.Y / 4, size.X, size.Y / 2);
                for (float x = 0; x < width; x += 12)
                    chart.DrawTextureRectRegion(dot, new Rect2(k.V(x, baseY), k.V(12, 10)), middle, new Color(1, 1, 1, 0.28f));
            }
            for (int i = 0; i < fights; i++)
            {
                float total = 0;
                for (int s = 0; s < colors.Length; s++) total += Value(i, s);
                float stack = total * barMax / Scale();
                float y = baseY - 16;
                if (stack > 0.5f)
                {
                    bool bottom = true;
                    foreach (int s in order)
                    {
                        float segment = Value(i, s) / total * stack;
                        if (segment <= 0) continue;
                        // Every segment above the bottom one leaves a gap under it, so two players of one colour don't merge.
                        float gap = bottom ? 0 : ChartMath.SegmentGap(segment, SegmentGap);
                        chart.DrawRect(new Rect2(k.V(X(i) - colW / 2, y - segment), k.V(colW, segment - gap)), colors[s]);
                        y -= segment;
                        bottom = false;
                    }
                    chart.DrawRect(new Rect2(k.V(X(i) - colW / 2, baseY - 16 - stack), k.V(colW, stack)), new Color(0, 0, 0, 0.4f), false, k.U(1));
                    if (i == hovered)
                        chart.DrawRect(new Rect2(k.V(X(i) - colW / 2 - 3, baseY - 19 - stack), k.V(colW + 6, stack + 6)), RecapTheme.Text, false, k.U(2));
                }
                FightPoint fight = current.FightPoints[i];
                bool boss = fight.Room == "boss";
                float size = boss ? bossNode : node;
                if (GameArt.Room(fight.Room) is Texture2D icon)
                    chart.DrawTextureRect(icon, new Rect2(k.V(X(i) - size / 2, baseY - 12 - (boss ? 5 : 0)), k.V(size, size)), false);
            }
            if (Caps() is not Font caps) return;
            float actY = baseY - 36 - barMax;
            var gold = new Color(RecapTheme.Gold, 0.8f);
            chart.DrawString(caps, k.V(0, actY + 12), Loc.Text("WHO_CARRIED.timeline.act", current.FightPoints[0].Act), HorizontalAlignment.Left, -1, k.F(13), gold);
            foreach (int start in current.ActStarts)
                chart.DrawString(caps, k.V((X(start - 1) + X(start)) / 2 - 20, actY + 12), Loc.Text("WHO_CARRIED.timeline.act", current.FightPoints[start].Act),
                    HorizontalAlignment.Left, -1, k.F(13), gold);
        };

        PanelContainer tip = k.Tip(14, 10, alpha: 0.97f);
        tip.Visible = false;
        tip.ZIndex = 5;
        VBoxContainer tipRows = k.Column(2);
        tip.AddChild(tipRows);
        if (interactive) chart.AddChild(tip);

        void ShowTip()
        {
            if (hovered < 0 || hovered >= fights)
            {
                tip.Visible = false;
                return;
            }
            FightTip.Fill(k, tipRows, current, hovered);
            Vector2 size = tip.GetCombinedMinimumSize();
            tip.Size = size;
            float x = k.U(X(hovered) + colW / 2 + 12);
            if (x + size.X > k.U(width)) x = k.U(X(hovered) - colW / 2 - 12) - size.X;
            tip.Position = new Vector2(x, Math.Max(0, k.U(baseY - 20) - size.Y));
            tip.Visible = true;
        }

        // Points at fight i (-1: none), from the mouse or the controller.
        void Point(int i)
        {
            if (i == hovered) return;
            hovered = i;
            chart.QueueRedraw();
            ShowTip();
        }

        void Set(RecapView v, bool animate)
        {
            // Series follow the scoreboard: timeline series are in join order, the stacks put the leader at the bottom.
            List<string> ranked = v.Overview.Where(r => r.Share != null).Select(r => r.Label).ToList();
            int series = v.Timeline.Count;
            float[][] shown = Enumerable.Range(0, fights).Select(i => Enumerable.Range(0, Math.Min(series, colors.Length)).Select(s => Value(i, s))
                .Concat(Enumerable.Repeat(0f, Math.Max(0, series - colors.Length))).ToArray()).ToArray();
            float shownMax = Scale();
            current = v;
            colors = v.Timeline.Select(s => RecapTheme.Accent(s.ColorHex)).ToArray();
            order = Enumerable.Range(0, series).OrderBy(s => ranked.IndexOf(v.Timeline[s].Label) is int r && r >= 0 ? r : 99).ToArray();
            int n = v.FightPoints.Count;
            to = Enumerable.Range(0, n).Select(i => v.Timeline.Select(s => (float)s.Values[i]).ToArray()).ToArray();
            from = Enumerable.Range(0, n).Select(i => i < shown.Length ? shown[i] : new float[series]).ToArray();
            fights = n;
            toMax = Math.Max(1, to.Select(f => f.Sum()).DefaultIfEmpty(0).Max());
            fromMax = animate && shown.Length > 0 ? shownMax : toMax;

            int team = v.Overview.Where(r => r.Share != null).Sum(r => r.Value);
            summary.Text = Loc.Text("WHO_CARRIED.timeline.total", Kit.Num(team), Loc.Text(n == 1 ? "WHO_CARRIED.summary.fight_one" : "WHO_CARRIED.summary.fights", n));
            int best = n == 0 ? -1 : Enumerable.Range(0, n).OrderByDescending(i => to[i].Sum()).First();
            tag.Visible = best >= 0 && to[best].Sum() > 0;
            if (tag.Visible) tagText.Text = $"{v.FightPoints[best].Label} {Kit.Num((int)to[best].Sum())}";
            summary.Text += tag.Visible ? " ·" : "";
            empty.Visible = n == 0;

            foreach (Node child in swatches.GetChildren())
            {
                swatches.RemoveChild(child);
                child.QueueFree();
            }
            if (v.Timeline.Count > 1)
            {
                foreach (int s in order)
                {
                    HBoxContainer item = k.Row(6);
                    item.AddChild(Kit.Center(k.Swatch(colors[s], 12, 12, 3)));
                    item.AddChild(Kit.Center(k.Text(v.Timeline[s].Label, 14, RecapTheme.Muted)));
                    swatches.AddChild(item);
                }
            }

            tween?.Kill();
            if (!animate || !chart.IsInsideTree())
            {
                t = 1;
                chart.QueueRedraw();
            }
            else
            {
                t = 0;
                tween = Anim.Progress(chart, p =>
                {
                    t = p;
                    chart.QueueRedraw();
                });
            }
            if (interactive) ShowTip();
        }

        Set(view, animate: false);
        live?.On(v => Set(v, animate: true));

        if (interactive)
        {
            chart.GuiInput += input =>
            {
                if (input is not InputEventMouseMotion motion || fights == 0 || IgnoreHover) return;
                float x = motion.Position.X / k.S, y = motion.Position.Y / k.S;
                int nearest = Enumerable.Range(0, fights).OrderBy(i => Math.Abs(X(i) - x)).First();
                Point(Math.Abs(X(nearest) - x) <= Math.Max(colW, (width - 40) / Math.Max(1, fights - 1) / 2) && y > baseY - 30 - barMax && y < baseY + 30
                    ? nearest : -1);
            };
            chart.MouseExited += () => Point(-1);
            pad?.Rows.Add(new PadRow(() => fights, () => fights - 1, Point, () => Point(-1)));
        }
        return box;
    }
}

/// <summary>The hover readout for one fight: floor and name, then each player's damage, biggest first, and the team total.</summary>
internal static class FightTip
{
    public static void Fill(Kit k, VBoxContainer rows, RecapView view, int fight)
    {
        foreach (Node child in rows.GetChildren())
        {
            rows.RemoveChild(child); // out of the tree now, so the tip's size excludes it
            child.QueueFree();
        }
        FightPoint point = view.FightPoints[fight];
        HBoxContainer title = k.Row(9);
        if (GameArt.Room(point.Room) is Texture2D icon) title.AddChild(Kit.Center(k.Pic(icon, 30, 30)));
        title.AddChild(Kit.Center(k.Text(Loc.Text("WHO_CARRIED.timeline.floor", point.Floor, point.Label), 19, RecapTheme.Gold, true, Ink.Soft)));
        rows.AddChild(title);
        int team = 0;
        foreach (TimelineSeries s in view.Timeline.OrderByDescending(s => s.Values[fight]))
        {
            HBoxContainer line = k.Row(8);
            line.CustomMinimumSize = k.V(230, 24);
            line.AddChild(Kit.Center(k.Swatch(RecapTheme.Accent(s.ColorHex), 14, 6, 3)));
            line.AddChild(Kit.Center(k.Text(s.Label, 15, RecapTheme.Text)));
            line.AddChild(Kit.Fill());
            line.AddChild(Kit.Center(k.Text(Kit.Num(s.Values[fight]), 17, RecapTheme.Text, true, Ink.Soft)));
            rows.AddChild(line);
            team += s.Values[fight];
        }
        if (view.Timeline.Count > 1)
        {
            rows.AddChild(k.Swatch(new Color(1, 1, 1, 0.1f), 230, 1, 0));
            HBoxContainer total = k.Row(8);
            total.AddChild(Kit.Center(k.Text(Loc.Text("WHO_CARRIED.stat.team"), 15, RecapTheme.Muted)));
            total.AddChild(Kit.Fill());
            total.AddChild(Kit.Center(k.Text(Kit.Num(team), 17, RecapTheme.Text, true, Ink.Soft)));
            rows.AddChild(total);
        }
    }
}
