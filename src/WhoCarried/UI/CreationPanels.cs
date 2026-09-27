using Godot;
using WhoCarried.Core;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// Every card each player's cards and powers created, for themselves or a teammate, with a bar against the card they
/// made most of. Shared by the recap and the exported image.
/// </summary>
internal static class CreationPanels
{
    public static Control Create(Kit k, RecapView view, float width, Live? live, bool compact = false)
    {
        VBoxContainer root = k.Column(compact ? 8 : 12);
        root.CustomMinimumSize = k.V(width, 0);
        Label hint = k.Text(Loc.Text("WHO_CARRIED.support.created_hint"), compact ? 12 : 15, RecapTheme.Muted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = k.V(width, 0);
        root.AddChild(hint);
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
        float nameWidth = compact ? 220 : 280, amountWidth = compact ? 40 : 56;
        float barWidth = width - padding * 2 - gap * 2 - nameWidth - amountWidth;
        Color color = RecapTheme.Accent(player.ColorHex);
        var panel = k.Tip(padding, compact ? 10 : 14);
        panel.CustomMinimumSize = k.V(width, 0);
        VBoxContainer body = k.Column(compact ? 6 : 10);
        panel.AddChild(body);
        HBoxContainer who = k.Row(10);
        who.AddChild(Kit.Center(k.Pic(k.Icon(player.IconKey), compact ? 22 : 28, compact ? 22 : 28)));
        Label name = k.Text(player.Label, compact ? 17 : 21, color, true, Ink.Soft);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        who.AddChild(name);
        body.AddChild(who);

        // A card's name, a bar in the player's colour against the card they made most of, and how many they made.
        var rows = new KeyedRows<(CreatedCardRow Card, int Most)>(body, c => c.Card.Key, c =>
        {
            HBoxContainer line = k.Row(gap);
            Label title = k.Text(c.Card.Label, compact ? 13 : 16, RecapTheme.Text);
            title.CustomMinimumSize = k.V(nameWidth, 0);
            title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.AddChild(Kit.Center(title));
            var bar = new LiveBar((double)c.Card.Created / Math.Max(1, c.Most), color, k.U(barWidth), k.U(compact ? 5 : 7));
            line.AddChild(Kit.Center(bar.Control));
            Label amount = k.Text("", compact ? 13 : 16, RecapTheme.Text, true);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            amount.CustomMinimumSize = k.V(amountWidth, 0);
            var created = new LiveNumber(amount, c.Card.Created);
            line.AddChild(Kit.Center(created.Control));
            return (line, next =>
            {
                title.Text = next.Card.Label;
                created.Set(next.Card.Created);
                bar.Set((double)next.Card.Created / Math.Max(1, next.Most));
            });
        }, offset: 1);
        void Apply(CreationRow next)
        {
            name.Text = next.Label;
            int most = next.Cards.Select(c => c.Created).DefaultIfEmpty(1).Max();
            rows.Sync(next.Cards.Select(c => (c, most)));
        }
        Apply(player);
        return (panel, Apply);
    }
}
