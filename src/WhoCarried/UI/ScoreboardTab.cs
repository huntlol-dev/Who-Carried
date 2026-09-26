using Godot;
using WhoCarried.Core;
using WhoCarried.Game;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// The scoreboard: the party as a hand of cards (the leader first, bigger and foil), each player's top sources down
/// the right, and the climb along the bottom. A lone player gets one big card and a "Your run" panel beside it.
/// </summary>
internal static class ScoreboardTab
{
    public static Control Create(Kit k, RecapView view, Live live, bool deal, PadTab? pad = null)
    {
        Control tab = k.Box(RecapPanel.DesignW, RecapPanel.DesignH);
        int n = Players(view).Count;
        Hand hand = new(k, tab, deal);
        hand.Sync(view);
        live.On(hand.Sync);
        pad?.Rows.Add(hand.Row());

        Label note = k.Text("", 14, RecapTheme.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        tab.AddChild(k.At(note, 40, 648, 1120, -1)); // under the lowest badges of a 2-card hand
        void Note(RecapView v)
        {
            BarRow? unattributed = v.Overview.FirstOrDefault(r => r.Share == null);
            var parts = new List<string>();
            if (v.BonusNote.Length > 0 && Players(v).Count > 1) parts.Add(v.BonusNote);
            if (unattributed != null) parts.Add(Loc.Text("WHO_CARRIED.summary.unattributed", Kit.Num(unattributed.Value)));
            note.Text = string.Join("  ·  ", parts);
        }
        Note(view);
        live.On(Note);

        if (n == 1) tab.AddChild(k.At(Story(k, view, live), 590, 176, 520, -1));
        tab.AddChild(k.At(TopSources(k, view, n == 1 ? 6 : n == 2 ? 4 : 2, live), 1222, 146, 340, -1));
        tab.AddChild(k.At(Climb.Create(k, view, 1522, 204, 92, interactive: true, live, pad), 40, 682));
        return tab;
    }

    public static List<BarRow> Players(RecapView view) => view.Overview.Where(r => r.Share != null).ToList();

    /// <summary>
    /// The player cards. Live: numbers count up, and when the ranking changes the cards glide to their new places
    /// (the new leader turns foil).
    /// </summary>
    private sealed class Hand
    {
        private readonly Kit _k;
        private readonly Control _table;
        private readonly Dictionary<string, PlayerCard> _cards = new();

        /// <summary>The cards in scoreboard order, as the controller steps through them.</summary>
        private readonly List<PlayerCard> _order = new();

        private bool _deal;

        public Hand(Kit k, Control table, bool deal)
        {
            _k = k;
            _table = table;
            _deal = deal;
        }

        public void Sync(RecapView view)
        {
            List<BarRow> players = Players(view);
            int n = players.Count;
            if (n == 0) return;
            // Low enough that no card, even hovered, reaches up into the tabs.
            (float w, HandLayout.Slot[] slots) = HandLayout.Layout(n, 1200, HandLayout.TopClearOfTabs(n, 1200));
            // A different party size (a player joined mid-run) re-deals the hand at the new size.
            if (_cards.Count > 0 && (_cards.Count != n || _cards.Values.Any(c => Math.Abs(c.Width - w) > 0.1f) || players.Any(p => !_cards.ContainsKey(p.Label))))
            {
                foreach (PlayerCard old in _cards.Values) old.Face.Root.QueueFree();
                _cards.Clear();
            }
            for (int i = 0; i < n; i++)
            {
                BarRow row = players[i];
                if (!_cards.TryGetValue(row.Label, out PlayerCard? card))
                {
                    card = new PlayerCard(_k, view, row, w, n);
                    card.Face.LiftOnHover();
                    _cards[row.Label] = card;
                    _table.AddChild(card.Face.Root);
                }
                card.Update(view, row, i, n);
                card.Place(slots[i], i + 1, animate: !_deal && card.Placed);
                if (_deal) card.DealIn(i);
            }
            _order.Clear();
            _order.AddRange(players.Select(p => _cards[p.Label]));
            _deal = false;
        }

        /// <summary>The cards for the controller: selecting one lifts it as hover does and sets the others down.</summary>
        public PadRow Row() => new(() => _order.Count, () => 0,
            i => { for (int j = 0; j < _order.Count; j++) _order[j].Face.SetLifted(j == i); },
            () => { foreach (PlayerCard card in _order) card.Face.SetLifted(false); });
    }

    /// <summary>One player's card on the scoreboard (and in the saved image).</summary>
    internal sealed class PlayerCard
    {
        private readonly Kit _k;
        private readonly LiveNumber _number;
        private readonly Label _share, _block, _bonusValue, _bonusText, _deathsText;
        private readonly Control _shareChip, _bonusLine, _deathsLine;
        private readonly bool _solo;

        /// <param name="foilAt">Holds the leader's foil still (the saved image); -1 animates it.</param>
        public PlayerCard(Kit k, RecapView view, BarRow row, float width, int n, float foilAt = -1)
        {
            _k = k;
            _solo = n == 1;
            Width = width;
            Color color = RecapTheme.FromHex(row.ColorHex);
            // A modded character without select-screen art shows its icon on a glow of its colour instead.
            Texture2D? portrait = k.Icon(RecapTexts.PortraitKey(row.IconKey));
            Face = new CardFace(k, new CardSpec(width, color, row.Label,
                Portrait: portrait, Art: portrait == null ? k.Icon(row.IconKey) : null, Gem: k.Icon(RecapTexts.EnergyKey(row.IconKey)),
                Foil: true, Glow: true, Badges: row.BadgeList, FoilAt: foilAt));
            float em = Face.Em;
            VBoxContainer body = Face.Body;

            _number = new LiveNumber(k.Strong("", em * 3.6f), row.Value, tight: true, maxWidth: k.U(width * 0.76f), minSize: k.F(em * 2));
            body.AddChild(Centered(_number.Control));
            Label caption = k.Caps(Loc.Text("WHO_CARRIED.stat.damage"), em * 0.72f, RecapTheme.Caption, em * 0.16f);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            body.AddChild(Pad(caption, em * 0.35f));

            HBoxContainer chips = k.Row(em * 0.45f);
            chips.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            (_shareChip, _share) = Chip(GameArt.Get(GameArt.Swords), RecapTheme.Gold, "");
            chips.AddChild(_shareChip);
            (Control blockChip, _block) = Chip(GameArt.Get(GameArt.Block), RecapTheme.Green, "");
            chips.AddChild(blockChip);
            body.AddChild(Pad(chips, em * 0.4f));

            HBoxContainer bonus = k.Row(em * 0.3f);
            bonus.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            Texture2D? bonusIcon = _solo ? GameArt.Get(GameArt.Swords) : k.Icon(DebuffBuilder.IconPrefix + "VULNERABLE_POWER");
            bonus.AddChild(Kit.Center(k.Pic(bonusIcon, em * 1.2f, em * 1.2f)));
            _bonusValue = k.Text("", em * 0.92f, RecapTheme.Gold, true);
            _bonusText = k.Text("", em * 0.92f, new Color("e8e2d4"));
            if (_solo)
            {
                bonus.AddChild(Kit.Center(_bonusText));
                bonus.AddChild(Kit.Center(_bonusValue));
            }
            else
            {
                bonus.AddChild(Kit.Center(_bonusValue));
                bonus.AddChild(Kit.Center(_bonusText));
            }
            _bonusLine = Pad(bonus, em * 0.25f);
            body.AddChild(_bonusLine);

            // Times this player died, under the bonus damage; hidden for a player who never went down.
            HBoxContainer deaths = k.Row(em * 0.3f);
            deaths.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            deaths.AddChild(Kit.Center(k.Pic(GameArt.Get(GameArt.Skull), em * 1.2f, em * 1.2f)));
            _deathsText = k.Text("", em * 0.92f, new Color("e8e2d4"));
            deaths.AddChild(Kit.Center(_deathsText));
            _deathsLine = Pad(deaths, em * 0.25f);
            body.AddChild(_deathsLine);
        }

        public CardFace Face { get; }

        public float Width { get; }

        public bool Placed { get; private set; }

        public void Update(RecapView view, BarRow row, int rank, int n)
        {
            _number.Set(row.Value);
            Face.SetGemText((rank + 1).ToString());
            Face.SetPlaque(row.Award.Length == 0 ? "" : Loc.Text(row.Award));
            Face.SetBadges(row.BadgeList);
            _share.Text = row.Share is double s ? $"{Math.Round(s * 100):0}%" : "";
            _shareChip.Visible = n > 1;
            _block.Text = Loc.Text("WHO_CARRIED.stat.block_amount", Kit.Num(row.BlockRemoved), GameText.Block);
            if (_solo)
            {
                Award? hitter = view.Awards.FirstOrDefault(a => a.Title == AwardBuilder.HeavyHitter);
                _bonusText.Text = Loc.Text("WHO_CARRIED.stat.biggest_hit_amount", hitter?.Value ?? "");
                _bonusValue.Text = "";
                _bonusLine.Visible = hitter != null;
            }
            else
            {
                _bonusValue.Text = "";
                _bonusText.Text = Loc.Text("WHO_CARRIED.stat.bonus_amount", Kit.Num(row.Bonus));
                _bonusLine.Visible = row.Bonus > 0;
            }
            _deathsText.Text = row.Deaths == 1
                ? Loc.Text("WHO_CARRIED.stat.deaths_one")
                : Loc.Text("WHO_CARRIED.stat.deaths", Kit.Num(row.Deaths));
            _deathsLine.Visible = row.Deaths > 0;
            Face.SetLeader(rank == 0);
        }

        public void Place(HandLayout.Slot slot, int z, bool animate)
        {
            Face.Root.ZIndex = z;
            Face.MoveTo(_k.V(slot.X, slot.Y), Mathf.DegToRad(slot.Tilt), slot.Scale, animate);
            Placed = true;
        }

        /// <summary>Deals the card in from below the table, one after another (once the card is on screen).</summary>
        public void DealIn(int index)
        {
            Control root = Face.Root;
            if (!root.IsInsideTree())
            {
                void Once()
                {
                    root.TreeEntered -= Once;
                    DealIn(index);
                }
                root.TreeEntered += Once;
                return;
            }
            Vector2 target = root.Position;
            float rotation = root.Rotation;
            root.Position = target + _k.V(-120 + index * 40, 520);
            root.Rotation = rotation - 0.35f;
            root.Modulate = new Color(1, 1, 1, 0);
            Tween tween = root.CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
            double delay = 0.06 + index * 0.08;
            // No overshoot on the way up: a bounce would carry the card over the tabs.
            tween.TweenProperty(root, "position", target, 0.5).SetDelay(delay).SetTrans(Tween.TransitionType.Quint);
            tween.TweenProperty(root, "rotation", rotation, 0.5).SetDelay(delay);
            tween.TweenProperty(root, "modulate", Colors.White, 0.25).SetDelay(delay).SetTrans(Tween.TransitionType.Linear);
        }

        private (Control Chip, Label Value) Chip(Texture2D? icon, Color tone, string suffix)
        {
            float em = Face.Em;
            var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            StyleBoxFlat box = RecapTheme.Box(new Color(0, 0, 0, 0.35f), _k.U(em * 0.7f), new Color(1, 1, 1, 0.12f), _k.U(1));
            box.ContentMarginLeft = _k.U(em * 0.35f);
            box.ContentMarginRight = _k.U(em * 0.55f);
            box.ContentMarginTop = _k.U(em * 0.08f);
            box.ContentMarginBottom = _k.U(em * 0.1f);
            chip.AddThemeStyleboxOverride("panel", box);
            HBoxContainer row = _k.Row(em * 0.3f);
            row.AddChild(Kit.Center(_k.Pic(icon, em * 1.25f, em * 1.25f)));
            Label value = _k.Text("", em * 0.9f, tone, true);
            row.AddChild(Kit.Center(value));
            if (suffix.Length > 0) row.AddChild(Kit.Center(_k.Text(suffix, em * 0.9f, RecapTheme.Text, true)));
            chip.AddChild(row);
            return (chip, value);
        }

        private Control Pad(Control child, float top)
        {
            var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            margin.AddThemeConstantOverride("margin_top", (int)_k.U(top));
            child.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            margin.AddChild(child);
            return margin;
        }

        private static Control Centered(Control child)
        {
            child.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            return child;
        }
    }

    /// <summary>Each player's top few damage sources with their art, on one shared scale.</summary>
    public static Control TopSources(Kit k, RecapView view, int rows, Live? live)
    {
        VBoxContainer box = k.Column(11);
        box.AddChild(k.Heading(Loc.Text("WHO_CARRIED.sources.top"), GameArt.Get(GameArt.Swords)));

        (Control, Action<(BarRow Row, int Max)>) Line((BarRow Row, int Max) item, Color color)
        {
            HBoxContainer line = k.Row(9);
            line.AddChild(Kit.Center(k.Thumb(item.Row.ArtKey, item.Row.SubLabel, 38, 29)));
            VBoxContainer words = k.Column(3);
            words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            HBoxContainer top = k.Row(6);
            Label label = k.Text(RecapTexts.SourceLabel(item.Row), 15, RecapTheme.Text);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            top.AddChild(Kit.Center(label));
            var value = new LiveNumber(k.Text("", 17, RecapTheme.Text, true, Ink.Soft), item.Row.Value);
            top.AddChild(Kit.Center(value.Control));
            words.AddChild(top);
            var bar = new LiveBar((double)item.Row.Value / Math.Max(1, item.Max), color, k.U(266), k.U(4));
            words.AddChild(bar.Control);
            line.AddChild(words);
            return (line, it =>
            {
                value.Set(it.Row.Value);
                bar.Set((double)it.Row.Value / Math.Max(1, it.Max));
            });
        }

        (Control, Action<(SourcesView Source, int Max)>) Group((SourcesView Source, int Max) item)
        {
            Color color = RecapTheme.Accent(item.Source.ColorHex);
            var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = k.V(340, 0) };
            StyleBoxFlat style = RecapTheme.Box(new Color(0, 0, 0, 0.28f), 0);
            style.BorderColor = color;
            style.BorderWidthLeft = k.F(4);
            style.CornerRadiusTopRight = style.CornerRadiusBottomRight = k.F(6);
            style.ContentMarginLeft = k.U(12);
            style.ContentMarginRight = k.U(12);
            style.ContentMarginTop = k.U(8);
            style.ContentMarginBottom = k.U(10);
            panel.AddThemeStyleboxOverride("panel", style);
            VBoxContainer group = k.Column(7);
            group.AddChild(k.Who(RecapTexts.Name(item.Source.PlayerLabel), item.Source.IconKey, color));
            Label none = k.Text(Loc.Text("WHO_CARRIED.empty.damage"), 14, RecapTheme.Muted);
            group.AddChild(none);
            panel.AddChild(group);
            var lines = new KeyedRows<(BarRow Row, int Max)>(group, l => RecapTexts.SourceKey(l.Row), l => Line(l, color), offset: 2);
            void Apply((SourcesView Source, int Max) it)
            {
                List<BarRow> top = it.Source.Rows.Where(r => !RecapTexts.IsOther(r)).Take(rows).ToList();
                none.Visible = top.Count == 0;
                lines.Sync(top.Select(r => (r, it.Max)));
            }
            Apply(item);
            return (panel, Apply);
        }

        var groups = new KeyedRows<(SourcesView Source, int Max)>(box, g => g.Source.PlayerLabel, Group, offset: 1);
        void Sync(RecapView v)
        {
            int max = v.Sources.SelectMany(s => s.Rows).Where(r => !RecapTexts.IsOther(r)).Select(r => r.Value).DefaultIfEmpty(1).Max();
            groups.Sync(v.Sources.Where(s => s.PlayerLabel != RecapBuilder.UnattributedLabel).Select(s => (s, max)));
        }
        Sync(view);
        live?.On(Sync);
        return box;
    }

    /// <summary>For a lone player: the run told in four lines beside their card.</summary>
    public static Control Story(Kit k, RecapView view, Live? live, float width = 520)
    {
        PanelContainer tip = k.Tip(22, 18);
        VBoxContainer column = k.Column(12);
        tip.AddChild(column);
        HBoxContainer title = k.Row(10);
        title.AddChild(Kit.Center(k.Pic(GameArt.Get(GameArt.Trophy), 34, 34)));
        title.AddChild(Kit.Center(k.Text(Loc.Text("WHO_CARRIED.summary.your_run"), 22, RecapTheme.Gold, true, Ink.Soft)));
        column.AddChild(title);
        VBoxContainer lines = k.Column(12);
        column.AddChild(lines);

        void Apply(RecapView v)
        {
            foreach (Node child in lines.GetChildren())
            {
                lines.RemoveChild(child);
                child.QueueFree();
            }
            foreach ((Control art, string text) in RecapTexts.Story(k, v))
            {
                HBoxContainer line = k.Row(12);
                line.AddChild(Kit.Center(art));
                Label words = k.Text(text, 16, new Color("dfe4ea"));
                words.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                words.CustomMinimumSize = k.V(width - 110, 0);
                line.AddChild(Kit.Center(words));
                lines.AddChild(line);
            }
        }
        Apply(view);
        live?.On(Apply);
        return tip;
    }
}
