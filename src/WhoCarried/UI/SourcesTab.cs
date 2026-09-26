using Godot;
using WhoCarried.Core;
using WhoCarried.Game;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// Each player's damage as a deck list: a banner with their rank and total, then every source with its own picture
/// (a card's art, a relic's or power's icon) framed in its type's colour, a bar, and the block it knocked off.
/// </summary>
internal static class SourcesTab
{
    public static Control Create(Kit k, RecapView view, Live live, PadTab? pad = null)
    {
        Control tab = k.Box(RecapPanel.DesignW, RecapPanel.DesignH);
        // The scroll area starts a little left and above the content, so the rank gem and picture frames that stick
        // out past a column's edge aren't clipped.
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Position = k.V(24, 132), Size = k.V(1556, 652),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        tab.AddChild(scroll);
        if (pad != null) pad.Scroll = PadTab.Scrolls(scroll, k.U(PadTab.ScrollStep));
        var inset = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        inset.AddThemeConstantOverride("margin_left", k.F(16));
        inset.AddThemeConstantOverride("margin_top", k.F(8));
        inset.AddThemeConstantOverride("margin_bottom", k.F(8));
        scroll.AddChild(inset);
        HBoxContainer grid = k.Row(24);
        grid.CustomMinimumSize = k.V(1522, 0);
        inset.AddChild(grid);

        // Columns share the width; one or two players get half each, and a lone player's other half shows where
        // their damage came from.
        int count = Math.Max(1, view.Sources.Count);
        float colW = count <= 2 ? (1522 - 24) / 2f : (1522 - 24 * (count - 1)) / count;
        var columns = new KeyedRows<(SourcesView Source, int Rank, int Max)>(grid, c => c.Source.PlayerLabel, c => Column(k, c, colW));
        void Sync(RecapView v)
        {
            int max = v.Sources.SelectMany(s => s.Rows).Select(r => r.Value).DefaultIfEmpty(1).Max();
            columns.Sync(v.Sources.Select((s, i) => (s, s.PlayerLabel == RecapBuilder.UnattributedLabel ? 0 : i + 1, max)));
        }
        Sync(view);
        live.On(Sync);
        if (count == 1) grid.AddChild(Breakdown(k, view, colW, live));

        HBoxContainer legend = k.Row(22);
        foreach ((Color color, string text) in new[] { (RecapTheme.Attack, GameText.CardType("Attack")), (RecapTheme.Power, Loc.Text("WHO_CARRIED.sources.power_or_orb")), (RecapTheme.Relic, GameText.Relic), (RecapTheme.Plain, Loc.Text("WHO_CARRIED.sources.other")) })
        {
            HBoxContainer item = k.Row(7);
            var ring = new Panel { CustomMinimumSize = k.V(14, 14), MouseFilter = Control.MouseFilterEnum.Ignore };
            ring.AddThemeStyleboxOverride("panel", RecapTheme.Box(RecapTheme.Clear, k.U(3), color, k.U(2.5f)));
            item.AddChild(Kit.Center(ring));
            item.AddChild(Kit.Center(k.Text(text, 14, RecapTheme.Muted)));
            legend.AddChild(item);
        }
        Label pets = k.Text(Loc.Text("WHO_CARRIED.sources.pet_hint"), 14, RecapTheme.Muted);
        legend.AddChild(Kit.Center(pets));
        void Pets(RecapView v) => pets.Visible = v.Sources.Any(s => s.Rows.Any(r => r.SubLabel == nameof(SourceKind.Pet)));
        Pets(view);
        live.On(Pets);
        tab.AddChild(k.At(legend, 40, 800));
        return tab;
    }

    private static (Control, Action<(SourcesView Source, int Rank, int Max)>) Column(Kit k, (SourcesView Source, int Rank, int Max) item, float width)
    {
        SourcesView source = item.Source;
        Color color = source.PlayerLabel == RecapBuilder.UnattributedLabel ? RecapTheme.Grey : RecapTheme.FromHex(source.ColorHex);
        Color accent = RecapTheme.Accent(source.ColorHex);
        VBoxContainer column = k.Column(0);
        column.CustomMinimumSize = k.V(width, 0);

        // The banner: the player's name on the ancient banner in their colour, the rank gem, the total.
        Control header = k.Box(width, 80);
        header.AddChild(k.At(k.Dyed(GameArt.Get(GameArt.Banner), width + 12, 74, color), -6, -2));
        Label name = k.Strong(RecapTexts.Name(source.PlayerLabel), 26);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        k.Fit(name, width - 170, 15);
        header.AddChild(k.At(name, 0, 12, width, -1));
        var total = new LiveNumber(k.Strong("", 24), source.Rows.Sum(r => r.Value));
        total.Label.HorizontalAlignment = HorizontalAlignment.Right;
        header.AddChild(k.At(total.Control, width - 124, 16, 110, -1));
        Control gem = k.At(k.Box(60, 60), -14, -6);
        gem.AddChild(k.Pic(k.Icon(RecapTexts.EnergyKey(source.IconKey)) ?? GameArt.Get(GameArt.Energy), 60, 60,
            k.Icon(RecapTexts.EnergyKey(source.IconKey)) == null ? accent : null));
        Label rank = k.Strong("", 26);
        rank.HorizontalAlignment = HorizontalAlignment.Center;
        rank.VerticalAlignment = VerticalAlignment.Center;
        gem.AddChild(k.At(rank, 0, 1, 60, 60));
        header.AddChild(gem);
        column.AddChild(header);

        Label none = k.Text(Loc.Text("WHO_CARRIED.empty.sources"), 16, RecapTheme.Muted);
        column.AddChild(none);

        var rows = new KeyedRows<(BarRow Row, int Max)>(column, r => RecapTexts.SourceKey(r.Row), r => Row(k, r, width, accent), offset: 2);
        void Apply((SourcesView Source, int Rank, int Max) it)
        {
            rank.Text = it.Rank > 0 ? it.Rank.ToString() : "";
            gem.Visible = it.Rank > 0;
            total.Set(it.Source.Rows.Sum(r => r.Value));
            none.Visible = it.Source.Rows.Count == 0;
            rows.Sync(it.Source.Rows.Select(r => (r, it.Max)));
        }
        Apply(item);
        return (column, Apply);
    }

    /// <summary>
    /// A lone player's damage by kind (cards, orbs, powers, relics…): every hit counted, each kind with its share of the
    /// total and the source that did most of it.
    /// </summary>
    private static Control Breakdown(Kit k, RecapView view, float width, Live live)
    {
        VBoxContainer box = k.Column(12);
        box.CustomMinimumSize = k.V(width, 0);
        box.AddChild(k.Gap(0, 14));
        box.AddChild(k.Heading(Loc.Text("WHO_CARRIED.sources.heading"), GameArt.Get(GameArt.Swords), Loc.Text("WHO_CARRIED.sources.heading_hint")));
        PanelContainer tip = k.Tip(18, 12);
        VBoxContainer list = k.Column(0);
        tip.AddChild(list);
        box.AddChild(tip);
        Label none = k.Text(Loc.Text("WHO_CARRIED.empty.sources"), 16, RecapTheme.Muted);
        list.AddChild(none);

        (Control, Action<(KindTotal Kind, int Total)>) Line((KindTotal Kind, int Total) item)
        {
            KindTotal kind = item.Kind;
            var line = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            var style = new StyleBoxFlat { BgColor = RecapTheme.Clear, BorderColor = new Color(1, 1, 1, 0.06f), BorderWidthBottom = k.F(1) };
            style.ContentMarginTop = style.ContentMarginBottom = k.U(8);
            line.AddThemeStyleboxOverride("panel", style);
            HBoxContainer row = k.Row(14);
            row.AddChild(Kit.Center(k.Thumb(kind.TopArtKey, kind.Kind, 60, 46)));
            VBoxContainer words = k.Column(3);
            words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            HBoxContainer top = k.Row(8);
            top.AddChild(Kit.Center(k.Text(KindName(kind.Kind), 19, RecapTheme.Text, true, Ink.Soft)));
            Label detail = k.Text("", 14, RecapTheme.Muted);
            detail.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            detail.CustomMinimumSize = k.V(10, 0);
            top.AddChild(Kit.Center(detail));
            var value = new LiveNumber(k.Text("", 21, RecapTheme.Text, true, Ink.Soft), kind.Amount);
            top.AddChild(Kit.Center(value.Control));
            Label share = k.Text("", 15, RecapTheme.Gold, true);
            share.HorizontalAlignment = HorizontalAlignment.Right;
            share.CustomMinimumSize = k.V(46, 0);
            top.AddChild(Kit.Center(share));
            words.AddChild(top);
            var bar = new LiveBar(0, RecapTheme.SourceColor(kind.Kind).Lightened(0.15f), k.U(width - 36 - 74), k.U(6));
            words.AddChild(bar.Control);
            row.AddChild(Kit.Center(words));
            line.AddChild(row);
            void Apply((KindTotal Kind, int Total) it)
            {
                KindTotal kt = it.Kind;
                detail.Text = kt.Sources == 1 ? kt.TopLabel : Loc.Text("WHO_CARRIED.sources.more", kt.TopLabel, kt.Sources - 1);
                value.Set(kt.Amount);
                double fraction = (double)kt.Amount / Math.Max(1, it.Total);
                share.Text = $"{Math.Round(fraction * 100):0}%";
                bar.Set(fraction);
            }
            Apply(item);
            return (line, Apply);
        }

        var lines = new KeyedRows<(KindTotal Kind, int Total)>(list, l => l.Kind.Kind, Line, offset: 1);
        void Sync(RecapView v)
        {
            IReadOnlyList<KindTotal> kinds = v.Sources.FirstOrDefault()?.KindTotals ?? Array.Empty<KindTotal>();
            int total = kinds.Sum(x => x.Amount);
            none.Visible = kinds.Count == 0;
            lines.Sync(kinds.Select(x => (x, total)));
        }
        Sync(view);
        live.On(Sync);
        return box;
    }

    private static string KindName(string kind) => kind switch
    {
        "Card" => Loc.Text("WHO_CARRIED.sources.cards"),
        "Orb" => Loc.Text("WHO_CARRIED.sources.orbs"),
        "Power" => Loc.Text("WHO_CARRIED.sources.powers"),
        "Relic" => Loc.Text("WHO_CARRIED.sources.relics"),
        "Potion" => Loc.Text("WHO_CARRIED.sources.potions"),
        "Pet" => Loc.Text("WHO_CARRIED.sources.pets"),
        _ => Loc.Text("WHO_CARRIED.sources.other"),
    };

    private static (Control, Action<(BarRow Row, int Max)>) Row(Kit k, (BarRow Row, int Max) item, float width, Color accent)
    {
        BarRow row = item.Row;
        bool other = RecapTexts.IsOther(row);
        var line = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = k.V(width, 62) };
        var style = new StyleBoxFlat { BgColor = RecapTheme.Clear, BorderColor = new Color(1, 1, 1, 0.06f), BorderWidthBottom = k.F(1) };
        style.ContentMarginTop = style.ContentMarginBottom = k.U(6);
        line.AddThemeStyleboxOverride("panel", style);
        HBoxContainer content = k.Row(12);
        content.AddChild(Kit.Center(k.Thumb(row.ArtKey, row.SubLabel, 60, 46)));
        VBoxContainer words = k.Column(4);
        words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        HBoxContainer top = k.Row(6);
        Label label = k.Text(RecapTexts.SourceLabel(row), 17, other ? RecapTheme.Muted : RecapTheme.Text);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        top.AddChild(Kit.Center(label));
        var value = new LiveNumber(k.Text("", 21, RecapTheme.Text, true, Ink.Soft), row.Value);
        top.AddChild(Kit.Center(value.Control));
        words.AddChild(top);
        HBoxContainer under = k.Row(8);
        float barWidth = width - 72 - 70;
        var bar = new LiveBar((double)row.Value / Math.Max(1, item.Max), other ? RecapTheme.Grey : accent, k.U(barWidth), k.U(5));
        under.AddChild(bar.Control);
        Label block = k.Text("", 12, RecapTheme.Faint);
        under.AddChild(Kit.Center(block));
        words.AddChild(under);
        content.AddChild(Kit.Center(words));
        line.AddChild(content);

        void Apply((BarRow Row, int Max) it)
        {
            label.Text = RecapTexts.SourceLabel(it.Row);
            value.Set(it.Row.Value);
            bar.Set((double)it.Row.Value / Math.Max(1, it.Max));
            block.Text = it.Row.BlockRemoved > 0 ? Loc.Text("WHO_CARRIED.stat.block_amount", Kit.Num(it.Row.BlockRemoved), GameText.Block) : "";
        }
        Apply(item);
        return (line, Apply);
    }
}
