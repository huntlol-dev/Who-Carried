using Godot;
using WhoCarried.Core;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>All generated outputs and their recorded gift subset, shared by the recap and exported image.</summary>
internal static class CreationPanels
{
    public static Control Create(Kit k, RecapView view, float width, Live? live, bool compact = false)
    {
        VBoxContainer root = k.Column(compact ? 8 : 12);
        root.CustomMinimumSize = k.V(width, 0);
        foreach (string key in new[] { "WHO_CARRIED.support.created_hint", "WHO_CARRIED.support.gifts_history" })
        {
            Label hint = k.Text(Loc.Text(key), compact ? 12 : 15, RecapTheme.Muted);
            hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            hint.CustomMinimumSize = k.V(width, 0);
            root.AddChild(hint);
        }
        int columns = !compact && view.Creation.Count > 1 ? 2 : 1;
        float gap = compact ? 12 : 18;
        float panelWidth = (width - gap * (columns - 1)) / columns;
        var grid = new GridContainer { Columns = columns, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", k.F(gap));
        grid.AddThemeConstantOverride("v_separation", k.F(gap));
        root.AddChild(grid);
        var players = new KeyedRows<CreationRow>(grid, p => p.PlayerId.ToString(), p => Player(k, p, panelWidth, compact));
        void Apply(RecapView v) => players.Sync(v.Creation.Where(p => p.Cards.Count > 0));
        Apply(view);
        live?.On(Apply);
        return root;
    }

    private static (Control, Action<CreationRow>) Player(Kit k, CreationRow player, float width, bool compact)
    {
        float padding = compact ? 12 : 16, gap = compact ? 10 : 14;
        float createdWidth = compact ? 100 : 120, giftsWidth = compact ? 138 : 174;
        float nameWidth = width - padding * 2 - gap * 2 - createdWidth - giftsWidth;
        var panel = k.Tip(padding, compact ? 10 : 14);
        panel.CustomMinimumSize = k.V(width, 0);
        VBoxContainer body = k.Column(compact ? 6 : 10);
        panel.AddChild(body);
        HBoxContainer who = k.Row(10);
        who.AddChild(Kit.Center(k.Pic(k.Icon(player.IconKey), compact ? 22 : 28, compact ? 22 : 28)));
        Label name = k.Text(player.Label, compact ? 17 : 21, RecapTheme.Accent(player.ColorHex), true, Ink.Soft);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        who.AddChild(name);
        body.AddChild(who);

        Label Cell(string text, float cellWidth, bool number, bool heading = false)
        {
            Label label = k.Text(text, compact ? 13 : 16, heading ? RecapTheme.Muted : RecapTheme.Text, !heading && number);
            label.CustomMinimumSize = k.V(cellWidth, 0);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.HorizontalAlignment = number ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            return label;
        }
        HBoxContainer header = k.Row(gap);
        header.AddChild(Cell(Loc.Text("WHO_CARRIED.support.cards"), nameWidth, false, true));
        header.AddChild(Cell(Loc.Text("WHO_CARRIED.support.created_count"), createdWidth, true, true));
        header.AddChild(Cell(Loc.Text("WHO_CARRIED.support.gifts_recorded"), giftsWidth, true, true));
        body.AddChild(header);
        var rows = new KeyedRows<CreatedCardRow>(body, c => c.Key, c =>
        {
            HBoxContainer line = k.Row(gap);
            Label title = Cell(c.Label, nameWidth, false);
            var created = new LiveNumber(Cell("", createdWidth, true), c.Created);
            var given = new LiveNumber(Cell("", giftsWidth, true), c.Given);
            line.AddChild(title);
            line.AddChild(created.Control);
            line.AddChild(given.Control);
            return (line, next => { title.Text = next.Label; created.Set(next.Created); given.Set(next.Given); });
        }, offset: 2);
        void Apply(CreationRow next) { name.Text = next.Label; rows.Sync(next.Cards); }
        Apply(player);
        return (panel, Apply);
    }
}
