using Godot;
using WhoCarried.Core;
using WhoCarried.Game;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// Each player's deck: a banner tab per player, the deck's size and type counts, then the deck as the game's own
/// cards, with copies and upgrades in the corner and the damage each card dealt on a gold plaque.
/// Live: damage updates in place; the grid is rebuilt only when the deck itself changes.
/// </summary>
internal static class DecksTab
{
    /// <summary>Card width on the table, in design pixels (the game's card is 300 wide).</summary>
    private const float CardW = 126, Gap = 12;

    public static Control Create(Kit k, RecapView view, CardVisuals? cards, Live live, PadTab? pad = null)
    {
        Control tab = k.Box(RecapPanel.DesignW, RecapPanel.DesignH);
        HBoxContainer banners = k.Row(14);
        tab.AddChild(k.At(banners, 40, 140));
        HBoxContainer counts = k.Row(10);
        tab.AddChild(k.At(counts, 46, 204));

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Position = k.V(40, 248), Size = k.V(1530, 640),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        tab.AddChild(scroll);
        float scale = k.U(CardW) / CardVisuals.CardSize.X;
        int columns = Math.Max(4, (int)((1510 + Gap) / (CardW + Gap)));
        var grid = new GridContainer { Columns = columns, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", k.F(Gap));
        grid.AddThemeConstantOverride("v_separation", k.F(Gap + 4));
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", k.F(8));
        margin.AddThemeConstantOverride("margin_top", k.F(8));
        margin.AddChild(grid);
        scroll.AddChild(margin);

        RecapView current = view;
        ulong? shownPlayer = null;
        string shownDeck = "", shownBanners = "";
        var slots = new Dictionary<string, Action<DeckEntry>>();
        var tabs = new List<(ulong Player, Control Banner)>();

        static string Signature(DeckView d) => string.Join("|", d.Entries.Select(e => $"{e.Id}:{e.Count}:{e.UpgradedCount}"));

        void Counts(DeckView? deck)
        {
            foreach (Node child in counts.GetChildren())
            {
                counts.RemoveChild(child);
                child.QueueFree();
            }
            if (deck == null || deck.Entries.Count == 0) return;
            counts.AddChild(Kit.Center(k.Text(Loc.Text("WHO_CARRIED.decks.count", deck.CardCount), 18, RecapTheme.Text, true, Ink.Soft)));
            counts.AddChild(k.Gap(6, 0));
            foreach (IGrouping<string, DeckEntry> type in deck.Entries.GroupBy(e => TypeGroup(e.Type)).OrderBy(g => Order(g.Key)))
                counts.AddChild(Kit.Center(TypeChip(k, type.Key, type.Sum(e => e.Count))));
            if (deck.Gold > 0)
            {
                counts.AddChild(k.Gap(6, 0));
                HBoxContainer gold = k.Row(5);
                gold.AddChild(Kit.Center(k.Pic(GameArt.Get(GameArt.Gold), 22, 22)));
                gold.AddChild(Kit.Center(k.Text(Loc.Text("WHO_CARRIED.decks.gold", Kit.Num(deck.Gold)), 18, RecapTheme.Gold, true, Ink.Soft)));
                counts.AddChild(Kit.Center(gold));
            }
            counts.AddChild(k.Gap(6, 0));
            counts.AddChild(Kit.Center(k.Text(Loc.Text("WHO_CARRIED.decks.distinct", deck.Entries.Count), 15, RecapTheme.Muted)));
        }

        void ShowDeck(ulong playerId)
        {
            DeckView? deck = current.Decks.FirstOrDefault(d => d.PlayerId == playerId);
            cards?.ReleaseAll();
            foreach (Node child in grid.GetChildren())
            {
                grid.RemoveChild(child);
                child.QueueFree();
            }
            slots.Clear();
            shownPlayer = playerId;
            shownDeck = deck == null ? "" : Signature(deck);
            foreach ((ulong player, Control banner) in tabs) Mark(banner, player == playerId);
            Counts(deck);
            if (deck == null || deck.Entries.Count == 0)
            {
                grid.AddChild(k.Text(Loc.Text("WHO_CARRIED.empty.decks"), 18, RecapTheme.Muted));
                return;
            }
            foreach (DeckEntry entry in deck.Entries)
            {
                (Control slot, Action<DeckEntry> update) = Slot(k, deck, entry, cards, scale);
                slots[entry.Id] = update;
                grid.AddChild(slot);
            }
        }

        void Banners(RecapView v)
        {
            string signature = string.Join("|", v.Decks.Select(d => d.PlayerId + d.PlayerLabel + d.ColorHex));
            if (signature == shownBanners) return;
            shownBanners = signature;
            foreach (Node child in banners.GetChildren())
            {
                banners.RemoveChild(child);
                child.QueueFree();
            }
            tabs.Clear();
            foreach (DeckView deck in v.Decks)
            {
                ulong playerId = deck.PlayerId;
                Control banner = Banner(k, RecapTexts.Name(deck.PlayerLabel), RecapTheme.FromHex(deck.ColorHex), 210, 52, () => ShowDeck(playerId));
                banners.AddChild(banner);
                tabs.Add((playerId, banner));
                Mark(banner, playerId == shownPlayer);
            }
        }

        Banners(view);
        if (view.Decks.Count > 0) ShowDeck(view.Decks[0].PlayerId);
        else grid.AddChild(k.Text(Loc.Text("WHO_CARRIED.empty.decks"), 18, RecapTheme.Muted));

        live.On(v =>
        {
            current = v;
            Banners(v);
            if (shownPlayer is not ulong playerId)
            {
                if (v.Decks.Count > 0) ShowDeck(v.Decks[0].PlayerId);
                return;
            }
            DeckView? deck = v.Decks.FirstOrDefault(d => d.PlayerId == playerId);
            if (deck == null) return;
            if (Signature(deck) != shownDeck)
            {
                ShowDeck(playerId);
                return;
            }
            Counts(deck);
            foreach (DeckEntry entry in deck.Entries)
                if (slots.TryGetValue(entry.Id, out Action<DeckEntry>? update)) update(entry);
        });

        if (pad != null)
        {
            // The banners: stepping to one shows that deck straight away; the first press lands on the one shown.
            pad.Rows.Add(new PadRow(() => tabs.Count, () => Math.Max(0, tabs.FindIndex(t => t.Player == shownPlayer)),
                i => { if (i >= 0 && i < tabs.Count && tabs[i].Player != shownPlayer) ShowDeck(tabs[i].Player); },
                () => { }));
            pad.Scroll = PadTab.Scrolls(scroll, k.U(PadTab.ScrollStep));
        }
        return tab;
    }

    /// <summary>The chosen player's banner is bright; the others are dimmed.</summary>
    private static void Mark(Control banner, bool on) => banner.Modulate = new Color(1, 1, 1, on ? 1 : 0.5f);

    /// <summary>A player's name on the card banner in their colour; clickable.</summary>
    public static Control Banner(Kit k, string name, Color color, float width, float height, Action? onPress)
    {
        var button = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = k.V(width, height), MouseFilter = Control.MouseFilterEnum.Stop };
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        if (onPress != null) button.Pressed += onPress;
        else button.MouseFilter = Control.MouseFilterEnum.Ignore;
        button.AddChild(k.Dyed(GameArt.Get(GameArt.Banner), width, height, color));
        Label label = k.Strong(name, height * 0.42f);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        k.Fit(label, width * 0.72f, 12);
        button.AddChild(k.At(label, 0, height * 0.17f, width, -1));
        return button;
    }

    /// <summary>"● Attacks 6": a type's count in a pill ringed with the type's colour.</summary>
    private static Control TypeChip(Kit k, string type, int count)
    {
        Color color = RecapTheme.TypeColor(type);
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        StyleBoxFlat box = RecapTheme.Box(new Color(0, 0, 0, 0.35f), k.U(12), color, k.U(1.5f));
        box.ContentMarginLeft = k.U(7);
        box.ContentMarginRight = k.U(10);
        box.ContentMarginTop = k.U(3);
        box.ContentMarginBottom = k.U(4);
        chip.AddThemeStyleboxOverride("panel", box);
        HBoxContainer row = k.Row(6);
        row.AddChild(Kit.Center(k.Swatch(color, 10, 10, 5)));
        row.AddChild(Kit.Center(k.Text(Plural(type), 15, RecapTheme.Text)));
        row.AddChild(Kit.Center(k.Text(count.ToString(), 15, RecapTheme.Text, true)));
        chip.AddChild(row);
        return chip;
    }

    private static string TypeGroup(string type) => type is "Attack" or "Skill" or "Power" or "Curse" ? type : "Other";

    private static int Order(string group) => group switch { "Attack" => 0, "Skill" => 1, "Power" => 2, "Curse" => 3, _ => 4 };

    private static string Plural(string group) => group == "Other" ? Loc.Text("WHO_CARRIED.sources.other") : GameText.CardType(group);

    /// <summary>A deck card (real or text) with a copies badge and a damage plaque; both update in place.</summary>
    private static (Control Slot, Action<DeckEntry> Update) Slot(Kit k, DeckView deck, DeckEntry entry, CardVisuals? cards, float scale)
    {
        Control slot = cards?.Slot(deck.PlayerId, entry, scale) ?? CardVisuals.TextSlot(entry, scale);
        Vector2 size = CardVisuals.CardSize * scale;
        var overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Size = size };
        slot.AddChild(overlay);

        var copies = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        StyleBoxFlat copiesBox = RecapTheme.Box(new Color(RecapTheme.Ink, 0.88f), k.U(8), RecapTheme.TipEdge, k.U(1.5f), k.U(6), k.U(1));
        copies.AddThemeStyleboxOverride("panel", copiesBox);
        Label copiesText = k.Text("", 14, RecapTheme.Text, true);
        copies.AddChild(copiesText);
        overlay.AddChild(copies);

        var damage = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        StyleBoxFlat damageBox = RecapTheme.Box(RecapTheme.Plaque, k.U(9), RecapTheme.Gold, k.U(1.5f), k.U(8), k.U(1));
        damage.AddThemeStyleboxOverride("panel", damageBox);
        Label damageText = k.Text("", 15, RecapTheme.Gold, true, Ink.Soft);
        damage.AddChild(damageText);
        overlay.AddChild(damage);

        void Apply(DeckEntry e)
        {
            string count = e.Count > 1 ? $"×{e.Count}" : "";
            string upgraded = e.UpgradedCount == 0 ? "" : e.UpgradedCount == e.Count ? "+" : $"{e.UpgradedCount}+";
            copiesText.Text = $"{count} {upgraded}".Trim();
            copies.Visible = copiesText.Text.Length > 0;
            copies.Size = Vector2.Zero;
            float copiesW = Kit.Measure(copiesText) + k.U(12) + k.U(3);
            copies.Position = new Vector2(size.X - copiesW + k.U(6), -k.U(6));
            damageText.Text = Loc.Text("WHO_CARRIED.stat.damage_amount", Kit.Num(e.Damage));
            damage.Visible = e.Damage > 0;
            damage.Size = Vector2.Zero;
            float damageW = Kit.Measure(damageText) + k.U(16) + k.U(3);
            damage.Position = new Vector2((size.X - damageW) / 2, size.Y - k.U(22));
        }
        Apply(entry);
        return (slot, Apply);
    }

    /// <summary>The saved image's deck: rarity-coloured names grouped by type, with copies, upgrades and damage.</summary>
    public static Control List(Kit k, DeckView deck, float width)
    {
        Color accent = RecapTheme.Accent(deck.ColorHex);
        VBoxContainer column = k.Column(1);
        column.CustomMinimumSize = k.V(width, 0);
        HBoxContainer who = k.Who(RecapTexts.Name(deck.PlayerLabel), deck.IconKey, accent, 17);
        column.AddChild(who);
        column.AddChild(k.Swatch(accent, width, 2, 0));
        if (deck.Gold > 0)
        {
            HBoxContainer gold = k.Row(4);
            gold.AddChild(Kit.Center(k.Pic(GameArt.Get(GameArt.Gold), 15, 15)));
            gold.AddChild(Kit.Center(k.Text(Loc.Text("WHO_CARRIED.decks.gold", Kit.Num(deck.Gold)), 14, RecapTheme.Gold)));
            column.AddChild(gold);
        }
        int group = -1;
        foreach (DeckEntry entry in deck.Entries)
        {
            int order = Order(TypeGroup(entry.Type));
            if (order != group)
            {
                group = order;
                column.AddChild(k.Gap(0, 6));
                column.AddChild(k.Caps(Plural(TypeGroup(entry.Type)), 11, RecapTheme.Muted, 1.2f, bold: true));
            }
            HBoxContainer line = k.Row(5);
            string copies = entry.Count > 1 ? $" ×{entry.Count}" : "";
            Label label = k.Text(entry.Label + copies, 14, RecapTheme.RarityColor(entry.Rarity));
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(label);
            if (entry.UpgradedCount > 0)
                line.AddChild(k.Text(entry.UpgradedCount == entry.Count ? "+" : $"{entry.UpgradedCount}+", 13, RecapTheme.Healed, true));
            if (entry.Damage > 0)
            {
                Label damage = k.Text(Kit.Num(entry.Damage), 14, RecapTheme.Gold, true);
                damage.HorizontalAlignment = HorizontalAlignment.Right;
                damage.CustomMinimumSize = k.V(44, 0);
                line.AddChild(damage);
            }
            column.AddChild(line);
        }
        if (deck.Entries.Count == 0) column.AddChild(k.Text(Loc.Text("WHO_CARRIED.empty.decks"), 14, RecapTheme.Muted));
        return column;
    }
}
