using Godot;
using WhoCarried.Core;

namespace WhoCarried.UI;

/// <summary>
/// The cards each player made for their teammates, with a bar against the card they gave most. Shared by the recap and
/// the exported image.
/// </summary>
internal static class CardGiftPanels
{
    public static Control Create(Kit k, RecapView view, float width, Live? live, bool compact = false)
    {
        int columns = !compact && view.CardGifts.Count > 1 ? 2 : 1;
        float gap = compact ? 12 : 18;
        float panelWidth = (width - gap * (columns - 1)) / columns;
        var grid = new GridContainer { Columns = columns, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.CustomMinimumSize = k.V(width, 0);
        grid.AddThemeConstantOverride("h_separation", k.F(gap));
        grid.AddThemeConstantOverride("v_separation", k.F(gap));
        var players = new KeyedRows<CardGiftRow>(grid, p => p.PlayerId.ToString(), p => Player(k, p, panelWidth, compact));
        void Apply(RecapView v) => players.Sync(v.CardGifts.Where(p => p.Cards.Count > 0));
        Apply(view);
        live?.On(Apply);
        return grid;
    }

    private static (Control, Action<CardGiftRow>) Player(Kit k, CardGiftRow player, float width, bool compact)
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

        // A card's name, a bar in the player's colour against the card they gave most, and how many they gave.
        var rows = new KeyedRows<(GivenCard Card, int Most)>(body, c => c.Card.Key, c =>
        {
            HBoxContainer line = k.Row(gap);
            Label title = k.Text(c.Card.Label, compact ? 13 : 16, RecapTheme.Text);
            title.CustomMinimumSize = k.V(nameWidth, 0);
            title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.AddChild(Kit.Center(title));
            var bar = new LiveBar((double)c.Card.Given / Math.Max(1, c.Most), color, k.U(barWidth), k.U(compact ? 5 : 7));
            line.AddChild(Kit.Center(bar.Control));
            Label amount = k.Text("", compact ? 13 : 16, RecapTheme.Text, true);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            amount.CustomMinimumSize = k.V(amountWidth, 0);
            var given = new LiveNumber(amount, c.Card.Given);
            line.AddChild(Kit.Center(given.Control));
            return (line, next =>
            {
                title.Text = next.Card.Label;
                given.Set(next.Card.Given);
                bar.Set((double)next.Card.Given / Math.Max(1, next.Most));
            });
        }, offset: 1);
        void Apply(CardGiftRow next)
        {
            name.Text = next.Label;
            int most = next.Cards.Select(c => c.Given).DefaultIfEmpty(1).Max();
            rows.Sync(next.Cards.Select(c => (c, most)));
        }
        Apply(player);
        return (panel, Apply);
    }
}
