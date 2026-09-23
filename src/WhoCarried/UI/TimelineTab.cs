using System.Globalization;
using Godot;
using WhoCarried.Core;
using WhoCarried.Game;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// Damage per fight as lines over the route itself: a legend, then a framed chart whose x-axis is the map you climbed
/// (each fight's room icon), gold act labels, and a tooltip-style readout that snaps to the nearest fight.
/// Live: points glide to their new values and new fights slide in.
/// </summary>
internal static class TimelineTab
{
    /// <summary>The chart's name in the scene tree, so the dev preview can find it and hover it.</summary>
    public const string HoverLayerName = "ChartHover";

    /// <summary>Opens the readout on a fight directly (dev preview screenshots).</summary>
    public static Action<int>? PreviewShowFight { get; private set; }

    /// <summary>Logs hover events (dev preview only).</summary>
    public static bool PreviewDiagnostics { get; set; }

    private const float ChartW = 1488, ChartH = 548;

    /// <summary>
    /// With two or more players each line ends in its player's name: the room kept for them on the right, their text
    /// size, and the least space between two of them (design pixels).
    /// </summary>
    private const float NameRoom = 150, NameSize = 18, NameGap = 20;

    /// <summary>The chart's right margin: room for the names when there's more than one line.</summary>
    private static float RightMargin(int lines) => lines > 1 ? 16 + NameRoom : 16;

    /// <summary>The text cut with "…" until it fits <paramref name="maxWidth"/> (screen pixels), whole letters at a time.</summary>
    private static string Shorten(Font font, string text, int size, float maxWidth)
    {
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= maxWidth) return text;
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        for (int n = starts.Length - 1; n > 0; n--)
        {
            string cut = text[..starts[n]].TrimEnd() + "…";
            if (font.GetStringSize(cut, HorizontalAlignment.Left, -1, size).X <= maxWidth) return cut;
        }
        return "…";
    }

    public static Control Create(Kit k, RecapView view, Live live, PadTab? pad = null)
    {
        Control tab = k.Box(RecapPanel.DesignW, RecapPanel.DesignH);
        tab.AddChild(k.At(Legend(k, view, live), 40, 140));
        Label empty = k.Text(Loc.Text("WHO_CARRIED.empty.timeline"), 18, RecapTheme.Muted);
        tab.AddChild(k.At(empty, 40, 190));
        PanelContainer frame = k.Tip(12, 10, alpha: 0.55f);
        frame.AddChild(Chart(k, view, ChartW, ChartH, interactive: true, live, visible => { frame.Visible = visible; empty.Visible = !visible; }, pad));
        tab.AddChild(k.At(frame, 40, 184));
        return tab;
    }

    /// <summary>Each player's icon, name and line colour.</summary>
    private static Control Legend(Kit k, RecapView view, Live live)
    {
        HBoxContainer legend = k.Row(26);
        string shown = "";
        void Apply(RecapView v)
        {
            // In scoreboard order, like every other tab.
            List<string> ranked = ScoreboardTab.Players(v).Select(p => p.Label).ToList();
            List<TimelineSeries> series = v.Timeline.OrderBy(s => ranked.IndexOf(s.Label) is int i && i >= 0 ? i : 99).ToList();
            string signature = string.Join("|", series.Select(s => s.Label + s.ColorHex + s.IconKey));
            if (signature == shown) return;
            shown = signature;
            foreach (Node child in legend.GetChildren())
            {
                legend.RemoveChild(child);
                child.QueueFree();
            }
            foreach (TimelineSeries s in series)
            {
                HBoxContainer item = k.Row(8);
                if (k.Icon(s.IconKey) is Texture2D icon) item.AddChild(Kit.Center(k.Pic(icon, 28, 28)));
                item.AddChild(Kit.Center(k.Text(s.Label, 19, RecapTheme.Text, true, Ink.Soft)));
                Panel pill = k.Swatch(RecapTheme.Accent(s.ColorHex), 26, 6, 3);
                ((StyleBoxFlat)pill.GetThemeStylebox("panel")).SetBorderWidthAll(Math.Max(1, k.F(1.5f)));
                ((StyleBoxFlat)pill.GetThemeStylebox("panel")).BorderColor = RecapTheme.Ink;
                item.AddChild(Kit.Center(pill));
                legend.AddChild(item);
            }
        }
        Apply(view);
        live.On(Apply);
        return legend;
    }

    /// <summary>
    /// The chart, drawn in one pass: gridlines and values, dashed act lines with gold labels, cased lines with dots,
    /// and the room icons along the bottom. <paramref name="setVisible"/> hears whether there's anything to draw.
    /// </summary>
    public static Control Chart(Kit k, RecapView view, float width, float height, bool interactive, Live? live,
                                Action<bool>? setVisible = null, PadTab? pad = null)
    {
        const float left = 58, top = 30, bottom = 30, icon = 28;
        // plotW changes with the number of lines (see Set): names at the ends need room.
        float plotW = width - left - RightMargin(view.Timeline.Count), plotH = height - top - bottom, baseline = top + plotH;
        var chart = new Control
        {
            Name = HoverLayerName, CustomMinimumSize = k.V(width, height), Size = k.V(width, height),
            MouseFilter = interactive ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore,
        };

        RecapView current = view;
        int fights = 0, hovered = -1;
        float[][] from = Array.Empty<float[]>(), to = Array.Empty<float[]>();
        float fromTop = 1, toTop = 1, t = 1;
        Tween? tween = null;
        Color[] colors = Array.Empty<Color>();
        var actLine = new Color(RecapTheme.Gold, 0.25f);

        float X(int i) => fights <= 1 ? left + plotW / 2 : left + i * plotW / (fights - 1);
        float Value(int i, int s) => s < from[i].Length && s < to[i].Length ? from[i][s] + (to[i][s] - from[i][s]) * t : 0;
        float Top() => fromTop + (toTop - fromTop) * t;
        float Y(float v) => top + plotH - v * plotH / Top();

        chart.Draw += () =>
        {
            if (fights == 0) return;
            // Looked up at each draw: the game can dispose fonts kept from earlier.
            Font? regular = RecapTheme.Regular, bold = RecapTheme.Bold;
            float max = Top();
            for (int g = 0; g <= 4; g++)
            {
                float value = max * g / 4, y = Y(value);
                chart.DrawLine(k.V(left, y), k.V(left + plotW, y), RecapTheme.Line, Math.Max(1, k.U(1)));
                if (regular != null)
                    chart.DrawString(regular, k.V(0, y + 5), Kit.Num((long)Math.Round(value)), HorizontalAlignment.Right, k.U(left - 10), k.F(14), RecapTheme.Faint);
            }
            if (bold != null) chart.DrawString(bold, k.V(left + 8, 16), Loc.Text("WHO_CARRIED.timeline.act", current.FightPoints[0].Act), HorizontalAlignment.Left, -1, k.F(14), RecapTheme.Gold);
            foreach (int start in current.ActStarts)
            {
                if (start <= 0 || start >= fights) continue;
                float x = (X(start - 1) + X(start)) / 2;
                chart.DrawDashedLine(k.V(x, 4), k.V(x, baseline), actLine, k.U(2), k.U(6));
                if (bold != null) chart.DrawString(bold, k.V(x + 8, 16), Loc.Text("WHO_CARRIED.timeline.act", current.FightPoints[start].Act), HorizontalAlignment.Left, -1, k.F(14), RecapTheme.Gold);
            }
            if (hovered >= 0 && hovered < fights)
                chart.DrawLine(k.V(X(hovered), top), k.V(X(hovered), baseline), new Color(1, 1, 1, 0.4f), k.U(1.5f));
            for (int s = 0; s < colors.Length; s++)
            {
                Vector2[] points = Enumerable.Range(0, fights).Select(i => k.V(X(i), Y(Value(i, s)))).ToArray();
                if (points.Length >= 2)
                {
                    chart.DrawPolyline(points, RecapTheme.Ink, k.U(7), true);
                    chart.DrawPolyline(points, colors[s], k.U(3.5f), true);
                }
                for (int i = 0; i < points.Length; i++)
                {
                    float r = i == hovered ? 6.5f : 4.5f;
                    chart.DrawCircle(points[i], k.U(r + 2), RecapTheme.Ink);
                    chart.DrawCircle(points[i], k.U(r), colors[s]);
                }
            }
            if (colors.Length > 1 && bold != null)
            {
                // Each line ends in its player's name, so lines of close colours can still be told apart.
                int last = fights - 1;
                float[] ends = Enumerable.Range(0, colors.Length).Select(s => Y(Value(last, s))).ToArray();
                float[] placed = EndLabels.Spread(ends, NameGap, top, baseline);
                float x = X(last) + 14;
                for (int s = 0; s < colors.Length && s < current.Timeline.Count; s++)
                {
                    if (Math.Abs(placed[s] - ends[s]) > 2)
                        chart.DrawLine(k.V(X(last) + 8, ends[s]), k.V(x - 3, placed[s]), new Color(colors[s], 0.6f), k.U(1.5f));
                    string name = Shorten(bold, current.Timeline[s].Label, k.F(NameSize), k.U(NameRoom - 20));
                    Vector2 at = k.V(x, placed[s] + NameSize * 0.35f);
                    chart.DrawStringOutline(bold, at, name, HorizontalAlignment.Left, -1, k.F(NameSize), k.F(5), RecapTheme.Ink);
                    chart.DrawString(bold, at, name, HorizontalAlignment.Left, -1, k.F(NameSize), colors[s]);
                }
            }
            float pitch = fights > 1 ? plotW / (fights - 1) : plotW;
            float size = Math.Min(icon, pitch - 2);
            for (int i = 0; i < fights; i++)
            {
                FightPoint fight = current.FightPoints[i];
                if (GameArt.Room(fight.Room) is Texture2D room)
                    chart.DrawTextureRect(room, new Rect2(k.V(X(i) - size / 2, height - 24 - (size - icon) / 2), k.V(size, size)), false);
            }
        };

        PanelContainer tip = k.Tip(16, 12, alpha: 0.97f);
        tip.Visible = false;
        tip.ZIndex = 5;
        VBoxContainer rows = k.Column(2);
        tip.AddChild(rows);
        if (interactive) chart.AddChild(tip);

        ulong pinnedUntil = 0; // the dev preview pins the readout so the real, idle cursor can't close it before the screenshot

        void Hide()
        {
            if (Time.GetTicksMsec() < pinnedUntil) return;
            if (PreviewDiagnostics && hovered >= 0) Tracker.Note("preview hover: readout hidden");
            hovered = -1;
            tip.Visible = false;
            chart.QueueRedraw();
        }

        void Show(int i, bool force = false)
        {
            if (i == hovered && !force) return;
            hovered = i;
            FightTip.Fill(k, rows, current, i);
            Vector2 size = tip.GetCombinedMinimumSize();
            tip.Size = size;
            // Beside the point, on whichever side has room, like the game's hover tips.
            float px = k.U(X(i));
            float x = px - k.U(28) - size.X >= 0 ? px - k.U(28) - size.X : px + k.U(28);
            tip.Position = new Vector2(x, k.U(40));
            tip.Visible = true;
            chart.QueueRedraw();
            if (PreviewDiagnostics)
                Tracker.Note($"preview hover: readout for fight {i} at {tip.GlobalPosition} size {size}, visible {tip.IsVisibleInTree()}");
        }

        void Set(RecapView v, bool animate)
        {
            int oldSeries = colors.Length;
            float[][] shown = Enumerable.Range(0, fights).Select(i => Enumerable.Range(0, oldSeries).Select(s => Value(i, s)).ToArray()).ToArray();
            float shownTop = Top();
            current = v;
            plotW = width - left - RightMargin(v.Timeline.Count);
            colors =v.Timeline.Select(s => RecapTheme.Accent(s.ColorHex)).ToArray();
            int series = colors.Length, n = v.FightPoints.Count;
            to = Enumerable.Range(0, n).Select(i => v.Timeline.Select(s => (float)s.Values[i]).ToArray()).ToArray();
            from = Enumerable.Range(0, n).Select(i => Enumerable.Range(0, series)
                .Select(s => i < shown.Length && s < shown[i].Length ? shown[i][s] : 0f).ToArray()).ToArray();
            fights = n;
            int max = v.Timeline.SelectMany(s => s.Values).DefaultIfEmpty(0).Max();
            toTop = max > 0 ? ChartMath.GridCeiling(max) : 1;
            fromTop = animate && shown.Length > 0 ? shownTop : toTop;
            setVisible?.Invoke(n > 0 && max > 0);

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
            if (hovered >= fights) Hide();
            else if (hovered >= 0) Show(hovered, force: true);
        }

        Set(view, animate: false);
        live?.On(v => Set(v, animate: true));

        if (interactive)
        {
            chart.GuiInput += input =>
            {
                if (input is not InputEventMouseMotion motion || fights == 0 || Climb.IgnoreHover) return;
                float mx = motion.Position.X / k.S, my = motion.Position.Y / k.S;
                int nearest = Enumerable.Range(0, fights).OrderBy(i => Math.Abs(X(i) - mx)).First();
                // Snap to the nearest fight anywhere between points (they can sit 100+ px apart on short runs).
                float reach = Math.Max(40, fights > 1 ? plotW / (fights - 1) / 2 + 2 : width);
                if (Math.Abs(X(nearest) - mx) > reach || my < top - 10 || my > height) Hide();
                else Show(nearest);
            };
            // The dev preview and replays ignore the real, idle cursor (the controller and PreviewShowFight still work).
            chart.MouseExited += () => { if (!Climb.IgnoreHover) Hide(); };
            pad?.Rows.Add(new PadRow(() => fights, () => fights - 1, i => Show(i), Hide));
            PreviewShowFight = i =>
            {
                if (!GodotObject.IsInstanceValid(chart) || i < 0 || i >= fights) return;
                pinnedUntil = Time.GetTicksMsec() + 1500;
                Show(i);
            };
        }
        return chart;
    }
}
