using System.Globalization;
using Godot;
using WhoCarried.Core;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// The shareable image in the "Dealt" style: one tall page, 1200 wide, that reads top to bottom like the recap. The
/// game's top bar with the date, the hand, awards as condensed tiles, relics of the run, top sources, the climb,
/// debuffs, defense, the decks as lists, and the seed at the foot. Drawn at scale 1.
/// </summary>
internal static class SummaryCard
{
    public const int Width = 1200;
    private const float Pad = 48, Inner = Width - 2 * Pad;

    /// <param name="date">The run's date (defaults to today, for a run just played).</param>
    public static Control Create(RecapView view, Func<string?, Texture2D?> icons, DateTime? date = null)
    {
        var k = new Kit(1, icons);
        var page = new PanelContainer { CustomMinimumSize = new Vector2(Width, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        page.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("0a0e15") });
        page.AddChild(Backdrop());

        VBoxContainer column = k.Column(0);
        page.AddChild(column);
        column.AddChild(TopBar(k, view, date ?? DateTime.Now));

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", (int)Pad);
        margin.AddThemeConstantOverride("margin_right", (int)Pad);
        margin.AddThemeConstantOverride("margin_bottom", 30);
        column.AddChild(margin);
        VBoxContainer body = k.Column(34);
        margin.AddChild(body);

        body.AddChild(Hand(k, view));
        string note = Note(view);
        if (note.Length > 0)
        {
            Label line = k.Text(note, 14, RecapTheme.Muted);
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.CustomMinimumSize = new Vector2(Inner, 0);
            body.AddChild(Pull(line, -22));
        }

        if (view.Awards.Count > 0)
            body.AddChild(Section(k, Loc.Text("WHO_CARRIED.tab.awards"), GameArt.Get(GameArt.Trophy), Awards(k, view),
                AwardsNote(view.Awards.Count, ScoreboardTab.Players(view).Count > 1)));
        if (view.Badges?.Any(p => p.Badges.Count > 0) == true)
            body.AddChild(Section(k, Loc.Text("WHO_CARRIED.awards.badges"), GameArt.Get(GameArt.Achievements), Relics(k, view), Loc.Text("WHO_CARRIED.awards.badges_hint")));
        if (view.Sources.Any(s => s.Rows.Count > 0))
            body.AddChild(Section(k, Loc.Text("WHO_CARRIED.sources.top"), GameArt.Get(GameArt.Swords), Sources(k, view)));
        if (view.FightPoints.Count > 0)
            body.AddChild(Climb.Create(k, view, Inner, 212, 110, interactive: false, live: null));
        if (view.Debuffs.Applied.Count > 0)
            body.AddChild(Section(k, Loc.Text("WHO_CARRIED.tab.debuffs"), k.Icon(DebuffBuilder.IconPrefix + "VULNERABLE_POWER"),
                DebuffsTab.Applied(k, view, Inner, 3, null), Loc.Text("WHO_CARRIED.debuffs.hint")));
        // Only when someone gave a teammate something: a solo run, or a co-op run with no gifts, leaves it out.
        // Headed "Given to teammates", like the tab: one row of small cards, as many across as there are kinds given.
        if (view.HasSupport)
            body.AddChild(Section(k, Loc.Text("WHO_CARRIED.support.heading"), SupportTab.HeadingArt(k, view),
                SupportTab.Cards(k, view, Inner, SupportTab.KindsGiven(view), null, compact: true), Loc.Text("WHO_CARRIED.support.hint")));
        if (view.Defense.Count > 0)
            body.AddChild(Section(k, Loc.Text("WHO_CARRIED.tab.defense"), GameArt.Get(GameArt.Block), DefenseTab.Plates(k, view, Inner, 2, 104, null, compact: true)));
        if (view.Decks.Any(d => d.Entries.Count > 0))
            body.AddChild(Section(k, Loc.Text("WHO_CARRIED.tab.decks"), GameArt.Get(GameArt.Deck), Decks(k, view), Loc.Text("WHO_CARRIED.decks.hint")));

        body.AddChild(Footer(k, view));
        return page;
    }

    /// <summary>The table for the page: a dark blue-grey fall with a soft spotlight behind the hand.</summary>
    private static Control Backdrop()
    {
        var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        var fall = new Gradient
        {
            Offsets = new[] { 0f, 0.4f, 0.8f, 1f },
            Colors = new[] { new Color("141c29"), new Color("0b1019"), new Color("0e1520"), new Color("0a0e15") },
        };
        holder.AddChild(Fill(new GradientTexture2D { Gradient = fall, FillFrom = Vector2.Zero, FillTo = new Vector2(0, 1), Width = 4, Height = 256 }));
        var glow = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color(80 / 255f, 110 / 255f, 150 / 255f, 0.28f), new Color(80 / 255f, 110 / 255f, 150 / 255f, 0) } };
        TextureRect spot = Fill(new GradientTexture2D
        {
            Gradient = glow, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 256, Height = 128,
        });
        spot.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        spot.OffsetBottom = 640;
        holder.AddChild(spot);
        return holder;
    }

    private static TextureRect Fill(Texture2D texture)
    {
        var rect = new TextureRect
        {
            Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    /// <summary>The game's top bar: "Who Carried? · Victory", floor, time, ascension, team damage, and the date.</summary>
    private static Control TopBar(Kit k, RecapView view, DateTime date)
    {
        Control bar = k.Box(Width, 78);
        if (GameArt.Get(GameArt.TopBar) is Texture2D art) bar.AddChild(k.Stretch(art, Width, 78));
        else bar.AddChild(k.Swatch(new Color("1b2635"), Width, 78, 0));
        HBoxContainer row = k.Row(24);
        bar.AddChild(k.At(row, 30, 0, Width - 60, 72));
        HBoxContainer title = k.Row(0);
        title.AddChild(Kit.Center(k.Strong(RecapTexts.ModName + " · ", 32)));
        (string result, Color tone) = RecapTexts.Result(view);
        title.AddChild(Kit.Center(k.Strong(result, 32, tone)));
        row.AddChild(Kit.Center(title));
        void Stat(Texture2D? icon, string text)
        {
            if (text.Length == 0) return;
            HBoxContainer stat = k.Row(6);
            if (icon != null) stat.AddChild(Kit.Center(k.Pic(icon, 30, 30)));
            stat.AddChild(Kit.Center(k.Text(text, 22, RecapTheme.Text, true, Ink.Soft)));
            row.AddChild(Kit.Center(stat));
        }
        int floor = RecapTexts.Floor(view);
        Stat(GameArt.Get(GameArt.Floor), floor > 0 ? floor.ToString(CultureInfo.InvariantCulture) : "");
        Stat(GameArt.Get(GameArt.Timer), RecapTexts.Duration(view.Facts?.Seconds ?? 0));
        Stat(GameArt.Get(GameArt.Ascension), (view.Facts?.Ascension ?? 0) > 0 ? view.Facts!.Ascension.ToString(CultureInfo.InvariantCulture) : "");
        Stat(GameArt.Get(GameArt.Swords), Kit.Num(RecapTexts.TeamDamage(view)));
        row.AddChild(Kit.Fill());
        row.AddChild(Kit.Center(k.Text(Loc.Text("WHO_CARRIED.summary.date", date), 16, RecapTheme.Muted)));
        return bar;
    }

    /// <summary>The party as a fanned hand of cards, like the scoreboard; a lone player's card has their run beside it.</summary>
    private static Control Hand(Kit k, RecapView view)
    {
        List<BarRow> players = ScoreboardTab.Players(view);
        int n = players.Count;
        float w = n <= 2 ? 272 : 250;
        // Tall enough for the lowest card (the outer ones drop) and the badges along its bottom edge.
        Control hand = k.Box(Inner, n == 0 ? 0 : 22 + 26 + w * CardFace.Aspect + 34);
        if (n == 0) return hand;
        float step = n == 1 ? 0 : n == 2 ? 360 : n <= 4 ? 262 : (Inner - 40 - w) / (n - 1);
        float spread = (n - 1) * step + w;
        float left = n == 1 ? 60 : (Inner - spread) / 2;
        float[] tilt = n switch
        {
            1 => new[] { 0f },
            2 => new[] { -3.5f, 3.5f },
            3 => new[] { -4f, 0f, 4f },
            4 => new[] { -5f, -1.7f, 1.7f, 5f },
            _ => Enumerable.Range(0, n).Select(i => -6f + 12f * i / (n - 1)).ToArray(),
        };
        for (int i = 0; i < n; i++)
        {
            var card = new ScoreboardTab.PlayerCard(k, view, players[i], w, n, foilAt: 0.42f);
            card.Update(view, players[i], i, n);
            float drop = n >= 3 ? (Math.Abs(tilt[i]) > 3 ? 26 : 4) : 10;
            card.Place(new HandLayout.Slot(left + i * step, 22 + drop - (i == 0 && n > 2 ? 12 : 0), tilt[i], i == 0 && n > 1 ? 1.05f : 1f), i + 1, animate: false);
            hand.AddChild(card.Face.Root);
        }
        if (n == 1) hand.AddChild(k.At(ScoreboardTab.Story(k, view, null, 640), 400, 40, 640, -1));
        return hand;
    }

    private static string Note(RecapView view)
    {
        var parts = new List<string>();
        if (view.BonusNote.Length > 0 && ScoreboardTab.Players(view).Count > 1) parts.Add(view.BonusNote);
        if (view.Overview.FirstOrDefault(r => r.Share == null) is BarRow unattributed)
            parts.Add(Loc.Text("WHO_CARRIED.summary.unattributed", Kit.Num(unattributed.Value)));
        return string.Join("  ·  ", parts);
    }

    private static string AwardsNote(int count, bool party) =>
        Loc.Text(count == 1 ? "WHO_CARRIED.awards.one" : party ? "WHO_CARRIED.awards.team_count" : "WHO_CARRIED.awards.count", count);


    /// <summary>A section: its heading, then its content.</summary>
    private static Control Section(Kit k, string title, Texture2D? icon, Control content, string? note = null)
    {
        VBoxContainer section = k.Column(14);
        section.AddChild(k.Heading(title, icon, note, 26));
        section.AddChild(content);
        return section;
    }

    /// <summary>Moves a control up (negative) or down inside a column.</summary>
    private static Control Pull(Control child, int by)
    {
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_top", by);
        margin.AddChild(child);
        return margin;
    }

    /// <summary>
    /// Awards condensed to one tile each, two columns: the award's art on the winner's colour with their face in
    /// their gem, the title and winner, the number, and what it means.
    /// </summary>
    private static Control Awards(Kit k, RecapView view)
    {
        var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 10);
        float tileW = (Inner - 14) / 2;
        foreach (Award award in view.Awards)
        {
            Color color = RecapTheme.FromHex(award.ColorHex), accent = RecapTheme.Accent(award.ColorHex);
            PanelContainer tip = k.Tip(14, 8, new Color(accent, 0.4f));
            ((StyleBoxFlat)tip.GetThemeStylebox("panel")).ContentMarginLeft = 10;
            tip.CustomMinimumSize = new Vector2(tileW, 66);
            HBoxContainer row = k.Row(14);
            tip.AddChild(row);

            Control art = k.Box(64, 50);
            var tile = new Panel { Size = new Vector2(64, 50), MouseFilter = Control.MouseFilterEnum.Ignore, ClipChildren = CanvasItem.ClipChildrenMode.AndDraw };
            StyleBoxFlat mask = RecapTheme.Box(RecapTheme.Inset, 6);
            tile.AddThemeStyleboxOverride("panel", mask);
            tile.AddChild(k.Glow(color.Lerp(Colors.White, 0.1f), color.Lerp(new Color("0b0f16"), 0.65f), 64, 50));
            tile.AddChild(k.At(k.Pic(RecapTexts.AwardArt(k, award), 30, 30), 17, 10));
            art.AddChild(tile);
            var ring = new Panel { Size = new Vector2(64, 50), MouseFilter = Control.MouseFilterEnum.Ignore };
            StyleBoxFlat ringBox = RecapTheme.Box(RecapTheme.Clear, 6, accent, 2);
            ringBox.DrawCenter = false;
            ringBox.SetExpandMarginAll(2);
            ring.AddThemeStyleboxOverride("panel", ringBox);
            art.AddChild(ring);
            Control gem = k.At(k.Box(30, 30), 46, 30);
            Texture2D? energy = k.Icon(RecapTexts.EnergyKey(award.IconKey));
            gem.AddChild(k.Pic(energy ?? GameArt.Get(GameArt.Energy), 30, 30, energy == null ? accent : null));
            if (k.Icon(award.IconKey) is Texture2D face) gem.AddChild(k.At(k.Pic(face, 19, 19), 5.5f, 5.5f));
            art.AddChild(gem);
            var artPad = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            artPad.AddThemeConstantOverride("margin_right", 10);
            artPad.AddChild(art);
            row.AddChild(Kit.Center(artPad));

            VBoxContainer who = k.Column(0);
            who.CustomMinimumSize = new Vector2(180, 0);
            Label title = k.Text(Loc.Text(award.Title), 18, RecapTheme.Gold, true, Ink.Soft);
            k.Fit(title, 180, 13);
            who.AddChild(title);
            Label name = k.Text(award.PlayerName, 14, accent, true, Ink.Soft);
            k.Fit(name, 180, 11);
            who.AddChild(name);
            row.AddChild(Kit.Center(who));

            Label value = k.Strong(award.Value, 26);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.CustomMinimumSize = new Vector2(92, 0);
            row.AddChild(Kit.Center(value));
            Label detail = k.Text(award.Detail, 13, RecapTheme.Muted);
            detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            detail.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            detail.AddThemeConstantOverride("line_spacing", -2);
            row.AddChild(Kit.Center(detail));
            grid.AddChild(tip);
        }
        return grid;
    }

    /// <summary>Each player's game badges on one line: name, medals, then the badges' names.</summary>
    private static Control Relics(Kit k, RecapView view)
    {
        var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        float tileW = (Inner - 12) / 2;
        foreach (PlayerBadges p in view.Badges ?? Array.Empty<PlayerBadges>())
        {
            PanelContainer tip = k.Tip(14, 10);
            tip.CustomMinimumSize = new Vector2(tileW, 0);
            HBoxContainer row = k.Row(12);
            tip.AddChild(row);
            HBoxContainer who = k.Who(p.Name, p.IconKey, RecapTheme.Accent(p.ColorHex));
            who.CustomMinimumSize = new Vector2(150, 0);
            row.AddChild(Kit.Center(who));
            if (p.Badges.Count == 0)
            {
                row.AddChild(Kit.Center(k.Text(Loc.Text("WHO_CARRIED.empty.badges"), 13, RecapTheme.Faint)));
            }
            else
            {
                HBoxContainer medals = k.Row(4);
                foreach (BadgeInfo badge in p.Badges.Take(6)) medals.AddChild(k.Medal(badge, 36));
                row.AddChild(Kit.Center(medals));
                Label names = k.Text(string.Join(" · ", p.Badges.Select(b => b.Title)), 13, RecapTheme.Muted);
                names.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                names.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(Kit.Center(names));
            }
            grid.AddChild(tip);
        }
        return grid;
    }

    /// <summary>Each player's top five sources with their art, on one shared scale.</summary>
    private static Control Sources(Kit k, RecapView view)
    {
        List<SourcesView> sources = view.Sources.Where(s => s.PlayerLabel != RecapBuilder.UnattributedLabel).ToList();
        int n = Math.Max(1, sources.Count);
        // Never wider than half the page: a lone player's list would otherwise stretch its bars across it.
        float colW = Math.Min((Inner - 18) / 2, (Inner - 18 * (n - 1)) / n);
        HBoxContainer row = k.Row(18);
        int max = sources.SelectMany(s => s.Rows).Where(r => !RecapTexts.IsOther(r)).Select(r => r.Value).DefaultIfEmpty(1).Max();
        foreach (SourcesView source in sources)
        {
            Color accent = RecapTheme.Accent(source.ColorHex);
            VBoxContainer column = k.Column(8);
            column.CustomMinimumSize = new Vector2(colW, 0);
            HBoxContainer header = k.Row(7);
            header.AddChild(k.Who(RecapTexts.Name(source.PlayerLabel), source.IconKey, accent, 18));
            header.AddChild(Kit.Fill());
            header.AddChild(Kit.Center(k.Text(Kit.Num(source.Rows.Sum(r => r.Value)), 18, RecapTheme.Text, true, Ink.Soft)));
            VBoxContainer head = k.Column(6);
            head.AddChild(header);
            head.AddChild(k.Swatch(accent, colW, 3, 0));
            column.AddChild(head);
            foreach (BarRow r in source.Rows.Where(r => !RecapTexts.IsOther(r)).Take(5))
            {
                HBoxContainer line = k.Row(9);
                line.AddChild(Kit.Center(k.Thumb(r.ArtKey, r.SubLabel, 40, 30, 2)));
                VBoxContainer words = k.Column(3);
                words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                HBoxContainer top = k.Row(6);
                Label label = k.Text(RecapTexts.SourceLabel(r), 14, RecapTheme.Text);
                label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                label.CustomMinimumSize = new Vector2(10, 0);
                top.AddChild(Kit.Center(label));
                top.AddChild(Kit.Center(k.Text(Kit.Num(r.Value), 15, RecapTheme.Text, true, Ink.Soft)));
                words.AddChild(top);
                words.AddChild(new LiveBar((double)r.Value / Math.Max(1, max), accent, colW - 49, 4).Control);
                line.AddChild(words);
                column.AddChild(line);
            }
            row.AddChild(column);
        }
        return row;
    }

    /// <summary>Every deck as a list, side by side.</summary>
    private static Control Decks(Kit k, RecapView view)
    {
        int n = Math.Max(1, view.Decks.Count);
        float colW = Math.Min((Inner - 18 * 3) / 4 * 1.5f, (Inner - 18 * (n - 1)) / n);
        HBoxContainer row = k.Row(18);
        foreach (DeckView deck in view.Decks) row.AddChild(DecksTab.List(k, deck, colW));
        return row;
    }

    /// <summary>The seed and who played on the left, the mod's name on the right.</summary>
    private static Control Footer(Kit k, RecapView view)
    {
        HBoxContainer row = k.Row(12);
        var parts = new List<string>();
        if (view.Facts?.Seed is string seed && seed.Length > 0) parts.Add(Loc.Text("WHO_CARRIED.summary.seed", seed));
        int n = ScoreboardTab.Players(view).Count;
        if (n > 0) parts.Add(n == 1 ? Loc.Text("WHO_CARRIED.summary.solo") : Loc.Text("WHO_CARRIED.summary.players", n));
        if (view.Victory != null && view.FightPoints.Count > 0) parts.Add(view.FightPoints[^1].Label);
        row.AddChild(k.Text(string.Join(" · ", parts), 13, RecapTheme.Faint));
        row.AddChild(Kit.Fill());
        row.AddChild(k.Text(Loc.Text("WHO_CARRIED.summary.credit", RecapTexts.ModName), 13, RecapTheme.Faint));
        return Pull(row, -4);
    }
}
