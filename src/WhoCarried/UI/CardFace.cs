using Godot;
using WhoCarried.Core;

namespace WhoCarried.UI;

/// <summary>What goes on a card. Sizes are design pixels; everything inside scales with <see cref="Width"/>.</summary>
/// <param name="Portrait">Art cropped into the picture window (a character's select-screen portrait).</param>
/// <param name="Art">An icon shown on a glow of the card's colour instead of a portrait (award cards).</param>
/// <param name="Gem">The energy gem; null draws the colourless gem tinted with the card's colour.</param>
/// <param name="GemText">Shown in the gem (the rank); <paramref name="GemFace"/> shows a character icon instead.</param>
/// <param name="Plaque">The type line under the picture (the headline award, or the winner's name); "" for none.</param>
/// <param name="FoilAt">Holds the foil sheen still at this point of its sweep (0–1) instead of animating (the saved image).</param>
internal sealed record CardSpec(float Width, Color Color, string Banner, Texture2D? Portrait = null, Texture2D? Art = null,
                                Texture2D? Gem = null, string GemText = "", Texture2D? GemFace = null, string Plaque = "",
                                bool Foil = false, bool Glow = false, IReadOnlyList<BadgeInfo>? Badges = null, float FoilAt = -1);

/// <summary>
/// A player (or an award) as one of the game's cards: portrait, the ancient frame and banner tinted in their colour,
/// the energy gem with their rank, a type plaque, a text box the caller fills, and their game badges along the bottom
/// edge. The leader's card is foil. Parts that change live (rank, plaque, badges) have setters.
/// </summary>
internal sealed class CardFace
{
    /// <summary>Height over width, like the game's cards. The card's shape lives in <see cref="HandLayout"/>.</summary>
    public const float Aspect = HandLayout.Aspect;

    /// <summary>How far the portrait is zoomed: 1 fills the picture window (cropping), 0 shows all of it.</summary>
    public const float PortraitZoom = 0.8f;

    private readonly Kit _k;
    private readonly float _w, _h, _em;
    private readonly Label _gemText;
    private readonly PanelContainer _plaque;
    private readonly Label _plaqueText;
    private readonly Label _banner;
    private readonly HBoxContainer _badges;
    private readonly Control? _foil, _glow;
    private Control? _glowPanel;
    private string _shownBadges = "";

    public CardFace(Kit k, CardSpec spec)
    {
        _k = k;
        _w = spec.Width;
        _h = spec.Width * Aspect;
        _em = spec.Width / HandLayout.EmsAcross;
        Color c = spec.Color;
        Root = k.Box(_w, _h);
        // Tilts turn round a point below the card, so a fanned hand spreads from one wrist.
        Root.PivotOffset = k.V(_w / 2, _h * HandLayout.PivotDown);

        Root.AddChild(Shadow(spec.Glow));
        _glow = spec.Glow ? _glowPanel : null;

        // The picture window.
        float px = _w * 0.09f, py = _h * 0.06f, pw = _w * 0.82f, ph = _h * 0.43f;
        if (spec.Art != null)
        {
            Root.AddChild(k.At(k.Glow(c.Lerp(Colors.White, 0.1f), c.Lerp(new Color("0b0f16"), 0.65f), pw, ph), px, py));
            float icon = pw * 0.46f;
            Root.AddChild(k.At(k.Pic(spec.Art, icon, icon), px + (pw - icon) / 2, py + (ph - icon) / 2 + ph * 0.04f));
        }
        else
        {
            // Zoomed out partway, so more of the portrait shows; the strips beside it take the portrait's own edge colours.
            Color strip = c.Darkened(0.6f);
            Root.AddChild(k.At(k.Cover(spec.Portrait, pw, ph, 0.18f, PortraitZoom,
                picture => GameArt.EdgeColours(picture) ?? (strip, strip)), px, py));
        }

        Texture2D? frame = GameArt.Get(GameArt.Frame);
        if (frame != null)
        {
            Root.AddChild(k.Dyed(frame, _w, _h, c));
            if (spec.Foil)
            {
                _foil = Foil(frame, spec.FoilAt);
                Root.AddChild(_foil);
            }
        }
        else
        {
            var plain = new Panel { Size = k.V(_w, _h), MouseFilter = Control.MouseFilterEnum.Ignore };
            plain.AddThemeStyleboxOverride("panel", RecapTheme.Box(c.Darkened(0.55f), k.U(_em), c, k.U(_em * 0.6f)));
            Root.AddChild(plain);
        }

        // The banner overhangs both sides, like the game's ancient cards.
        float bx = -_w * 0.07f, by = _h * 0.02f, bw = _w * 1.14f, bh = _em * 3.25f;
        Root.AddChild(k.At(k.Dyed(GameArt.Get(GameArt.Banner), bw, bh, c), bx, by));
        _banner = k.Strong(spec.Banner, _em * 1.55f);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        Root.AddChild(k.At(_banner, bx, by + _em * 0.45f, bw, -1));
        FitBanner();

        // The type plaque sits on the picture's lower edge.
        _plaque = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        StyleBoxFlat plaqueBox = RecapTheme.Box(RecapTheme.Plaque, k.U(_em * 0.8f), RecapTheme.Gold, k.U(Math.Max(1.2f, _em * 0.12f)),
            k.U(_em * 0.9f), k.U(_em * 0.12f));
        _plaque.AddThemeStyleboxOverride("panel", plaqueBox);
        float plaqueSize = Math.Max(_em * 0.75f, 11);
        _plaqueText = k.Caps(spec.Plaque, plaqueSize, RecapTheme.Gold, plaqueSize * 0.1f, bold: true);
        _plaque.AddChild(_plaqueText);
        Root.AddChild(_plaque);
        SetPlaque(spec.Plaque);

        Body = k.Column(0);
        Body.Alignment = BoxContainer.AlignmentMode.Center;
        Root.AddChild(k.At(Body, _w * 0.11f, _h * 0.55f, _w * 0.78f, _h * 0.38f));

        // The gem overhangs the top-left corner.
        float gem = _em * HandLayout.GemSize;
        Control gemBox = k.At(k.Box(gem, gem), _em * HandLayout.GemLeft, _em * HandLayout.GemTop);
        Texture2D? gemTexture = spec.Gem ?? GameArt.Get(GameArt.Energy);
        TextureRect gemPic = k.Pic(gemTexture, gem, gem, spec.Gem == null ? RecapTheme.Accent(c.ToHtml(false)) : null);
        gemBox.AddChild(gemPic);
        if (spec.GemFace != null) gemBox.AddChild(k.At(k.Pic(spec.GemFace, gem * 0.64f, gem * 0.64f), gem * 0.18f, gem * 0.18f));
        _gemText = k.Strong(spec.GemText, _em * 1.9f);
        _gemText.HorizontalAlignment = HorizontalAlignment.Center;
        _gemText.VerticalAlignment = VerticalAlignment.Center;
        gemBox.AddChild(k.At(_gemText, 0, _em * 0.1f, gem, gem));
        Root.AddChild(gemBox);

        _badges = k.Row(_em * 0.12f);
        Root.AddChild(_badges);
        SetBadges(spec.Badges ?? Array.Empty<BadgeInfo>());
    }

    /// <summary>The card itself, sized (Width × Width·1.41) with its pivot set for tilting.</summary>
    public Control Root { get; }

    /// <summary>The text box: add the card's lines here; they stack centred.</summary>
    public VBoxContainer Body { get; }

    /// <summary>The card's em (design pixels): text sizes on the card are multiples of it, like the mockups.</summary>
    public float Em => _em;

    public void SetGemText(string text) => _gemText.Text = text;

    private Vector2 _home;
    private float _homeScale = 1, _homeRotation;
    private bool _lifted;

    /// <summary>Puts the card in its place on the table (screen units), gliding there when <paramref name="animate"/>.</summary>
    public void MoveTo(Vector2 position, float rotation, float scale, bool animate)
    {
        _home = position;
        _homeScale = scale;
        _homeRotation = rotation;
        (Vector2 at, Vector2 size) = Lifted();
        if (animate && Root.IsInsideTree())
        {
            Anim.To(Root, "position", at);
            Anim.To(Root, "rotation", rotation);
            Anim.To(Root, "scale", size);
        }
        else
        {
            Root.Position = at;
            Root.Rotation = rotation;
            Root.Scale = size;
        }
    }

    /// <summary>The card rises a little and grows when the mouse is over it, like a card in hand.</summary>
    public void LiftOnHover()
    {
        Root.MouseFilter = Control.MouseFilterEnum.Pass;
        _home = Root.Position;
        _homeRotation = Root.Rotation;
        // The dev preview ignores the real, idle cursor; the controller still lifts cards there.
        Root.MouseEntered += () => { if (!Climb.IgnoreHover) SetLifted(true); };
        Root.MouseExited += () => { if (!Climb.IgnoreHover) SetLifted(false); };
    }

    /// <summary>Lifts the card or sets it back down: mouse hover, or the controller selecting it.</summary>
    public void SetLifted(bool on)
    {
        if (_lifted == on) return;
        _lifted = on;
        (Vector2 at, Vector2 size) = Lifted();
        Anim.To(Root, "position", at);
        Anim.To(Root, "scale", size);
    }

    /// <summary>Where the card sits and how big, lifted or not (hovering: see HandLayout.Hovered).</summary>
    private (Vector2 Position, Vector2 Scale) Lifted()
    {
        if (!_lifted) return (_home, new Vector2(_homeScale, _homeScale));
        (float dx, float dy) = HandLayout.HoverShift(_w, Mathf.RadToDeg(_homeRotation), _homeScale);
        float s = _homeScale * HandLayout.HoverGrow;
        return (_home + _k.V(dx, dy), new Vector2(s, s));
    }

    /// <summary>The leader's card is foil and glows; the others are plain (for cards built with foil and glow).</summary>
    public void SetLeader(bool leader)
    {
        if (_foil != null) _foil.Visible = leader;
        if (_glow != null) _glow.Visible = leader;
    }

    public void SetBanner(string text)
    {
        _banner.Text = text;
        FitBanner();
    }

    public void SetPlaque(string text)
    {
        _plaqueText.Text = text.ToUpperInvariant();
        _plaque.Visible = text.Length > 0;
        // Centred by hand: a container only learns its size after layout, too late for the first frame.
        float width = Kit.Measure(_plaqueText) + 2 * _k.U(_em * 0.9f) + 2 * _k.U(Math.Max(1.2f, _em * 0.12f));
        _plaque.Size = Vector2.Zero;
        _plaque.Position = new Vector2((_k.U(_w) - width) / 2, _k.U(_h * 0.465f) - _k.U(_em * 0.2f));
    }

    public void SetBadges(IReadOnlyList<BadgeInfo> badges)
    {
        string signature = string.Join(",", badges.Select(b => b.Id + b.Rarity));
        if (signature == _shownBadges) return;
        _shownBadges = signature;
        foreach (Node child in _badges.GetChildren())
        {
            _badges.RemoveChild(child);
            child.QueueFree();
        }
        float medal = _em * 2.25f;
        // Only as many as fit along the bottom edge.
        int room = Math.Max(1, (int)((_w * 0.92f) / (medal + _em * 0.12f)));
        foreach (BadgeInfo badge in badges.Take(room)) _badges.AddChild(_k.Medal(badge, medal));
        int n = Math.Min(room, badges.Count);
        float width = n * medal + Math.Max(0, n - 1) * _em * 0.12f;
        _badges.Position = _k.V((_w - width) / 2, _h - _em * 1.25f);
        _badges.Size = _k.V(width, medal);
    }

    private void FitBanner()
    {
        _banner.AddThemeFontSizeOverride("font_size", _k.F(_em * 1.55f));
        Kit.FitScaled(_banner, _k.U(_w * 0.86f), _k.F(_em * 0.9f));
        _banner.CustomMinimumSize = new Vector2(_k.U(_w * 1.14f), 0);
    }

    /// <summary>The card's drop shadow, plus a gold glow for the leader.</summary>
    private Control Shadow(bool glow)
    {
        Control holder = _k.Box(_w, _h);
        Panel Add(Color color, float size, float offsetY)
        {
            var panel = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            StyleBoxFlat box = RecapTheme.Box(RecapTheme.Clear, _k.U(_em * 1.2f));
            box.ShadowColor = color;
            box.ShadowSize = _k.F(size);
            box.ShadowOffset = new Vector2(0, _k.U(offsetY));
            panel.AddThemeStyleboxOverride("panel", box);
            _k.At(panel, _w * 0.05f, _h * 0.03f, _w * 0.9f, _h * 0.94f);
            holder.AddChild(panel);
            return panel;
        }
        if (glow) _glowPanel = Add(new Color(RecapTheme.Gold, 0.45f), _em * 2.2f, 0);
        Add(new Color(0, 0, 0, 0.55f), _em * 1.4f, _em * 0.9f);
        return holder;
    }

    private static Shader? _foilShader;

    /// <summary>A gold sheen that sweeps across the frame every few seconds, like a rare foil card.</summary>
    private TextureRect Foil(Texture2D frame, float still)
    {
        _foilShader ??= new Shader
        {
            Code = """
                shader_type canvas_item;
                render_mode blend_add;
                uniform vec2 size = vec2(1.0);
                uniform float offset = 0.0;
                uniform float still = -1.0;
                varying vec2 local;
                void vertex() { local = VERTEX / size; }
                void fragment() {
                    float a = texture(TEXTURE, UV).a;
                    float d = local.x * 0.8 + local.y * 0.45;
                    float sweep = (still >= 0.0 ? still : fract(TIME * 0.18 + offset)) * 2.6 - 0.7;
                    float band = smoothstep(0.13, 0.0, abs(d - sweep)) * 0.85 + smoothstep(0.08, 0.0, abs(d - sweep - 0.3)) * 0.55;
                    COLOR = vec4(1.0, 0.9, 0.6, band * a * 0.8);
                }
                """,
        };
        var material = new ShaderMaterial { Shader = _foilShader };
        material.SetShaderParameter("size", _k.V(_w, _h));
        material.SetShaderParameter("still", still);
        TextureRect sheen = _k.Stretch(frame, _w, _h);
        sheen.Material = material;
        return sheen;
    }
}
