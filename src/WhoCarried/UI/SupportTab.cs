using Godot;
using WhoCarried.Core;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// What each player gave their teammates, as nameplates: their portrait, then the energy, cards, block, buffs and draws
/// they gave. With nothing given (or nobody to give to) it says so instead.
/// </summary>
internal static class SupportTab
{
    public static Control Create(Kit k, RecapView view, Live live)
    {
        Control tab = k.Box(RecapPanel.DesignW, RecapPanel.DesignH);
        // Two plates a row; five or more players (modded lobbies) get shorter plates, without the "gave teammates" line,
        // so everything still fits. The hint follows the plates, however tall they came out.
        int rows = (Math.Max(1, view.Support.Count) + 1) / 2;
        float plateH = rows <= 2 ? 230 : Math.Max(150, (560 - 24 * (rows - 1)) / rows);
        VBoxContainer shown = k.Column(26);
        shown.AddChild(Plates(k, view, 1522, 2, plateH, live, subtitle: rows <= 2));
        Label note = k.Text(Loc.Text("WHO_CARRIED.support.hint"), 15, RecapTheme.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        shown.AddChild(note);
        tab.AddChild(k.At(shown, 40, 146, 1522, -1));

        Label empty = k.Text("", 18, RecapTheme.Muted);
        empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        tab.AddChild(k.At(empty, 40, 146, 1522, -1));

        void Apply(RecapView v)
        {
            shown.Visible = v.HasSupport;
            empty.Visible = !v.HasSupport;
            empty.Text = v.Support.Count > 1 ? Loc.Text("WHO_CARRIED.empty.support") : Loc.Text("WHO_CARRIED.support.solo");
        }
        Apply(view);
        live.On(Apply);
        return tab;
    }

    /// <summary>The nameplates in a grid, in scoreboard order.</summary>
    /// <param name="subtitle">The "gave teammates" line under each name (never on compact plates).</param>
    public static Control Plates(Kit k, RecapView view, float width, int columns, float height, Live? live, bool compact = false,
                                 bool subtitle = true)
    {
        float gap = compact ? 18 : 30;
        var grid = new GridContainer { Columns = columns, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", k.F(gap));
        grid.AddThemeConstantOverride("v_separation", k.F(compact ? 14 : 24));
        float plateW = (width - gap * (columns - 1)) / columns;
        var plates = new KeyedRows<SupportRow>(grid, r => r.Label, r => Plate(k, r, plateW, height, compact, subtitle && !compact));
        void Sync(RecapView v) => plates.Sync(v.Support);
        Sync(view);
        live?.On(Sync);
        return grid;
    }

    /// <summary>Each stat's icon, colour, "{0} energy"-style words, and value. Energy wears the player's own gem.</summary>
    private static List<(Texture2D? Icon, Color Tone, string Words, Func<SupportRow, int> Value)> Stats(Kit k, SupportRow row) => new()
    {
        (k.Icon(RecapTexts.EnergyKey(row.IconKey)) ?? GameArt.Get(GameArt.Energy), RecapTheme.Gold, "WHO_CARRIED.support.energy", r => r.Energy),
        (GameArt.Get(GameArt.Cards), RecapTheme.Text, "WHO_CARRIED.support.cards", r => r.Cards),
        (GameArt.Get(GameArt.Block), RecapTheme.Blocked, "WHO_CARRIED.support.block", r => r.Block),
        (k.Icon(DebuffBuilder.IconPrefix + "STRENGTH_POWER"), RecapTheme.Taken, "WHO_CARRIED.support.buffs", r => r.Buffs),
        (GameArt.Get(GameArt.DrawPile) ?? GameArt.Get(GameArt.Deck), RecapTheme.Teal, "WHO_CARRIED.support.draws", r => r.Draws),
    };

    private static (Control, Action<SupportRow>) Plate(Kit k, SupportRow row, float width, float height, bool compact, bool subtitle)
    {
        Color color = RecapTheme.FromHex(row.ColorHex), accent = RecapTheme.Accent(row.ColorHex);
        PanelContainer tip = compact ? k.Tip(10, 8, new Color(accent, 0.47f)) : k.Tip(24, 20, new Color(accent, 0.47f));
        tip.CustomMinimumSize = k.V(width, height);
        HBoxContainer line = k.Row(compact ? 12 : 24);
        tip.AddChild(line);
        float portraitH = compact ? 84 : height - 42, portraitW = compact ? 66 : Math.Min(170, portraitH * 0.78f);
        line.AddChild(Kit.Center(DefenseTab.Portrait(k, row.IconKey, color, portraitW, portraitH)));

        VBoxContainer right = k.Column(compact ? 6 : 10);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(Kit.Center(right));
        Label name = k.Text(row.Label, compact ? 20 : 34, accent, true, compact ? Ink.Soft : Ink.Strong);
        k.Fit(name, width - portraitW - (compact ? 40 : 96), compact ? 14 : 20);
        right.AddChild(name);
        if (subtitle) right.AddChild(k.Text(Loc.Text("WHO_CARRIED.support.gave"), 16, RecapTheme.Muted));

        var facts = new GridContainer { Columns = 3, MouseFilter = Control.MouseFilterEnum.Ignore };
        facts.AddThemeConstantOverride("h_separation", k.F(compact ? 14 : 28));
        facts.AddThemeConstantOverride("v_separation", k.F(compact ? 4 : 12));
        right.AddChild(facts);
        float icon = compact ? 18 : 30, text = compact ? 13 : 18;
        var numbers = new List<(LiveNumber Number, Func<SupportRow, int> Value)>();
        foreach ((Texture2D? art, Color tone, string words, Func<SupportRow, int> value) in Stats(k, row))
        {
            HBoxContainer fact = k.Row(compact ? 5 : 8);
            if (art != null) fact.AddChild(Kit.Center(k.Pic(art, icon, icon)));
            var number = new LiveNumber(k.Text("", text, tone, true, Ink.Soft), value(row), format: n => Loc.Text(words, Kit.Num(n)));
            fact.AddChild(Kit.Center(number.Control));
            facts.AddChild(fact);
            numbers.Add((number, value));
        }

        void Apply(SupportRow r)
        {
            foreach ((LiveNumber number, Func<SupportRow, int> value) in numbers) number.Set(value(r));
        }
        return (tip, Apply);
    }
}
