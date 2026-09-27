using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using WhoCarried.Core;
using WhoCarried.Game;
using MegaCrit.Sts2.Core.Localization;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// Developer-only visual check. If preview.flag exists in the mod's data folder (<see cref="Tracker.DataDir"/>), opens
/// the panel with sample data shortly after start-up, saves a screenshot of each tab plus the exported card beside it,
/// then closes. Inert otherwise.
/// </summary>
internal static class DevPreview
{
    private static readonly string[] TabNames = { "scoreboard", "awards", "sources", "debuffs", "support", "timeline", "defense", "decks" };

    public static void StartIfFlagged(string dataDir)
    {
        if (!File.Exists(Path.Combine(dataDir, "preview.flag"))) return;
        Later.Run(10, () => Run(dataDir));
    }

    /// <param name="Advance">Adds a new fight to the sample run (an Act 4 heart fight) and returns the updated view.</param>
    private sealed record Sample(RecapView View, Func<string?, Texture2D?> Icons, Func<ulong, string, CardModel?> CardFor,
                                 Func<RecapView> Advance);

    private static void Run(string dataDir)
    {
        // The flag can hold a party size (1–4) to check the hand's other layouts, "m1", "m2"… for the installed
        // modded characters four at a time (to check their art), or a comma-separated list of up to five character
        // ids ("IRONCLAD,IRONCLAD,REGENT") to check players who share a character; four base characters otherwise.
        string wanted = "";
        try { wanted = File.ReadAllText(Path.Combine(dataDir, "preview.flag")).Trim(); }
        catch (Exception) { }
        string[] options = wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        wanted = options.FirstOrDefault() ?? "";
        if (options.Length > 1 && LocManager.Languages.Contains(options[1]))
            LocManager.Instance.SetLanguage(options[1]);
        CharacterModel[] all = ModelDb.AllCharacters.ToArray();
        CharacterModel[] characters;
        if (wanted.StartsWith('m') && int.TryParse(wanted[1..], out int page))
        {
            string[] vanilla = { "IRONCLAD", "SILENT", "REGENT", "NECROBINDER", "DEFECT" };
            CharacterModel[] modded = all.Where(c => !vanilla.Contains(c.Id.Entry)).ToArray();
            characters = modded.Skip((Math.Max(1, page) - 1) * 4).Take(4).ToArray();
            Tracker.Note($"preview: modded characters {string.Join(", ", characters.Select(c => c.Id.Entry))} (of {modded.Length})");
        }
        else if (wanted.Contains(','))
        {
            characters = wanted.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => GameReader.CharacterById(id.ToUpperInvariant()))
                .OfType<CharacterModel>()
                .Take(5)
                .ToArray();
            Tracker.Note($"preview: characters {string.Join(", ", characters.Select(c => c.Id.Entry))}");
        }
        else
        {
            characters = all.Take(int.TryParse(wanted, out int count) ? Math.Clamp(count, 1, 4) : 4).ToArray();
        }
        if (characters.Length == 0) characters = all.Take(4).ToArray();
        Sample sample = BuildSample(characters);
        string? creationCase = options.FirstOrDefault(o => o.StartsWith("creation-", StringComparison.Ordinal));
        if (creationCase != null)
        {
            Sample original = sample;
            sample = original with
            {
                View = CreationPreview.Apply(original.View, creationCase),
                Advance = () => CreationPreview.Apply(original.Advance(), creationCase, advanced: true),
            };
        }
        if (wanted == "timer")
        {
            CheckTimer(dataDir, sample);
            return;
        }
        if (wanted == "close")
        {
            CheckCloseKey(dataDir, sample);
            return;
        }
        if (wanted == "steam")
        {
            CheckSteam(dataDir, sample);
            return;
        }
        // The game's canvas for this display setup (the aspect ratio setting picks the content size and how it fits).
        Window window = ((SceneTree)Engine.GetMainLoop()).Root;
        Tracker.Note($"preview screen: canvas {window.GetVisibleRect().Size}, window {window.Size}, " +
                     $"content {window.ContentScaleSize} {window.ContentScaleAspect}");
        TimelineTab.PreviewDiagnostics = true;
        Climb.IgnoreHover = true;
        RecapUi.ShowView(sample.View, sample.Icons, new CardVisuals(sample.CardFor));
        CaptureTab(0);

        // Live check: push a newer view into the open recap and capture the scoreboard after the transitions settle.
        void CaptureLive()
        {
            if (RecapUi.Open is not PanelHandle handle) return;
            RecapView later = sample.Advance();
            handle.Tabs.CurrentTab = creationCase != null ? 4 : 0;
            RecapUi.Apply(later);
            Later.Run(1.2, () =>
            {
                Image screen = ((SceneTree)Engine.GetMainLoop()).Root.GetTexture().GetImage();
                screen.SavePng(Path.Combine(dataDir, $"preview-{TabNames.Length + 1}-live.png"));
                PngExporter.Save(SummaryCard.Create(later, sample.Icons), SummaryCard.Width,
                    Path.Combine(dataDir, $"preview-{TabNames.Length + 2}-export.png"), error =>
                    {
                        if (error != null) Tracker.Note($"preview export failed: {error}");
                        SaveShare(dataDir, "preview-share.png", later, sample.Icons, () =>
                        {
                            RecapUi.Hide();
                            CaptureTopBar(dataDir, () => CapturePad(dataDir, sample, () => Tracker.Note("preview done")));
                        });
                    });
            });
        }

        void CaptureTab(int index)
        {
            // A resize replaces the recap, so look it up each time.
            if (RecapUi.Open is not PanelHandle handle) return; // someone closed the preview: stop quietly
            if (index >= TabNames.Length)
            {
                CaptureLive();
                return;
            }
            handle.Tabs.CurrentTab = index;
            if (TabNames[index] == "timeline")
            {
                Later.Run(0.5, () =>
                {
                    if (RecapUi.Open is PanelHandle open) SimulateHover(open.Tabs);
                });
                // The idle real cursor can take hover back a frame later; open the readout directly for the screenshot.
                Later.Run(0.75, () => TimelineTab.PreviewShowFight?.Invoke(sample.View.FightPoints.Count * 2 / 3));
            }
            Later.Run(0.9, () =>
            {
                Image screen = ((SceneTree)Engine.GetMainLoop()).Root.GetTexture().GetImage();
                screen.SavePng(Path.Combine(dataDir, $"preview-{index + 1}-{TabNames[index]}.png"));
                CaptureTab(index + 1);
            });
        }
    }

    /// <summary>Checks timer text and neighboring geometry in the real recap, only for the timer preview flag.</summary>
    private static async void CheckTimer(string dataDir, Sample sample)
    {
        PanelHandle? handle = null;
        try
        {
            RunFacts facts = sample.View.Facts
                ?? throw new InvalidOperationException("timer preview needs run facts");
            RecapView At(long seconds) => sample.View with
            {
                Facts = facts with { Seconds = seconds }
            };
            handle = RecapUi.ShowView(At(60), sample.Icons, new CardVisuals(sample.CardFor));
            Label label = (Label)handle.Root.FindChild("RecapTimerText", true, false);
            Control team = (Control)handle.Root.FindChild("RecapTeamStat", true, false);
            Control timer = (Control)handle.Root.FindChild("RecapTimerStat", true, false);
            if (label == null || team == null || timer == null)
                throw new InvalidOperationException("timer preview controls missing");

            async Task<Vector2> Read(long seconds)
            {
                if (RecapUi.Open != handle)
                    throw new InvalidOperationException("timer preview closed or resized; rerun at the new size");
                RecapUi.Apply(At(seconds));
                Vector2 geometry = await SettleTimer(handle.Root, label, team);
                string expected = RecapTexts.Duration(seconds);
                if (label.Text != expected || timer.Visible != (expected.Length > 0))
                    throw new InvalidOperationException("timer preview text/visibility mismatch");
                if (expected.Length > 0 && label.Size.X + 0.5f < Kit.Measure(label))
                    throw new InvalidOperationException("timer text exceeds its label");
                Tracker.Note($"timer preview: {seconds}s text={label.Text} width={geometry.X} teamX={geometry.Y}");
                return geometry;
            }

            Vector2 baseline = await Read(60);
            long[] boundaries = { 61, 60, 599, 600, 3599, 3600, 35999, 36000, 359999, 1 };
            long[] sweep = Enumerable.Range(0, 60).Select(i => 60L + i)
                .Concat(Enumerable.Range(0, 60).Select(i => 35940L + i)).ToArray();
            foreach (long seconds in boundaries.Concat(sweep).Concat(sweep.Reverse()))
                RequireTimerStable(baseline, await Read(seconds), seconds);

            await Read(36000);
            ((SceneTree)Engine.GetMainLoop()).Root.GetTexture().GetImage()
                .SavePng(Path.Combine(dataDir, "preview-timer-normal.png"));

            Vector2 expanded = await Read(360000); // 100:00:00: one growth is allowed
            if (expanded.X + 0.5f < baseline.X)
                throw new InvalidOperationException("timer width shrank on hour expansion");
            foreach (long seconds in new long[] { 360001, 360008, 360059, 360060, 60, 61 })
                RequireTimerStable(expanded, await Read(seconds), seconds);

            await Read(0); // visibility transitions are deliberately outside the stability assertion
            RecapUi.Apply(sample.View with { Facts = null });
            await SettleTimer(handle.Root, label, team);
            if (timer.Visible) throw new InvalidOperationException("missing facts must hide timer");
            RequireTimerStable(expanded, await Read(61), 61);
            ((SceneTree)Engine.GetMainLoop()).Root.GetTexture().GetImage()
                .SavePng(Path.Combine(dataDir, "preview-timer.png"));
            Tracker.Note("timer preview PASS");
        }
        catch (Exception e)
        {
            Tracker.LogError("timer preview FAIL", e);
        }
        finally
        {
            if (handle != null && RecapUi.Open == handle) RecapUi.Hide();
        }
    }

    private static void RequireTimerStable(Vector2 expected, Vector2 actual, long seconds)
    {
        if (Math.Abs(expected.X - actual.X) > 0.5f || Math.Abs(expected.Y - actual.Y) > 0.5f)
            throw new InvalidOperationException($"timer moved at {seconds}s: {expected} -> {actual}");
    }

    private static async Task<Vector2> SettleTimer(Control root, Label label, Control team)
    {
        // ShowView attaches its first CanvasLayer deferred, so the panel may not have a tree until the next frame.
        var tree = (SceneTree)Engine.GetMainLoop();
        Vector2 previous = new(float.NaN, float.NaN);
        int stable = 0;
        for (int frame = 0; frame < 30; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(root) || !root.IsInsideTree())
                throw new InvalidOperationException("timer preview panel closed");
            Vector2 current = new(label.Size.X, team.GlobalPosition.X);
            stable = current == previous ? stable + 1 : 0;
            if (stable >= 3) return current;
            previous = current;
        }
        throw new InvalidOperationException("timer layout did not settle within 30 frames");
    }

    /// <summary>
    /// The game's real top bar with the recap button added the way a run adds it, at rest and hovered. The bar isn't set
    /// up for a run here, so its labels show placeholders, and its own start-up logs an error when it can't find the
    /// run's screens (the buttons are ready before that).
    /// </summary>
    private static void CaptureTopBar(string dataDir, Action done)
    {
        Window root = ((SceneTree)Engine.GetMainLoop()).Root;
        var layer = new CanvasLayer { Layer = 101, Name = "WhoCarriedPreviewTopBar" };
        root.AddChild(layer);
        NTopBar bar = ResourceLoader.Load<PackedScene>("res://scenes/ui/top_bar.tscn").Instantiate<NTopBar>();
        layer.AddChild(bar);
        Control? button = TopBarButton.AddTo(bar);
        if (button == null)
        {
            Tracker.Note("top bar preview: no button (icon failed, see the log)");
            layer.QueueFree();
            done();
            return;
        }
        Later.Run(0.8, () =>
        {
            Control podium = button.GetChild<Control>(0);
            Control mapIcon = bar.Map.GetNode<Control>("Control/Icon");
            Tracker.Note($"top bar preview: podium box {podium.GetGlobalRect()}, map icon box {mapIcon.GetGlobalRect()}");
            (((TextureRect)podium).Texture as ImageTexture)?.GetImage().SavePng(Path.Combine(dataDir, "preview-topbar-icon.png"));
            root.GetTexture().GetImage().SavePng(Path.Combine(dataDir, "preview-10-topbar.png"));
            button.EmitSignal(Control.SignalName.MouseEntered);
            Later.Run(0.8, () =>
            {
                root.GetTexture().GetImage().SavePng(Path.Combine(dataDir, "preview-11-topbar-hover.png"));
                button.EmitSignal(Control.SignalName.MouseExited);
                layer.QueueFree();
                done();
            });
        });
    }

    /// <summary>
    /// Presses controller buttons the way a pad would (raw joypad events, through the game's bindings): RB to the next
    /// tab, the d-pad on the scoreboard's cards, a held left on the Timeline, B to close, then the podium selected on
    /// the top bar. Logs what each press did and whether the game is in controller mode (it only switches while the
    /// game window has focus).
    /// </summary>
    private static void CapturePad(string dataDir, Sample sample, Action done)
    {
        Window root = ((SceneTree)Engine.GetMainLoop()).Root;
        PadInput.Diagnostics = true;
        PanelHandle handle = RecapUi.ShowView(sample.View, sample.Icons, new CardVisuals(sample.CardFor));
        void Press(JoyButton button, bool down = true) => Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = down });
        void Tap(JoyButton button)
        {
            Press(button);
            Press(button, down: false);
        }
        void Shot(string name) => root.GetTexture().GetImage().SavePng(Path.Combine(dataDir, name));

        // Injected presses are handled on the next frame, so each step reports the previous one's result.
        Later.Run(1.0, () =>
        {
            Tap(JoyButton.DpadDown); // the first press after the mouse switches the game to controller mode
            Tap(JoyButton.RightShoulder); // in the same frame: must still reach the recap
        });
        Later.Run(2.0, () =>
        {
            Tracker.Note($"preview pad: controller mode {PadInput.ControllerMode}, focus on {root.GuiGetFocusOwner()?.Name}, " +
                         $"after RB tab {handle.Tabs.CurrentTab} (1 expected)");
            Shot("preview-12-pad-tab.png");
            Tap(JoyButton.LeftShoulder);
            Tap(JoyButton.DpadRight);
            Tap(JoyButton.DpadRight);
        });
        Later.Run(3.0, () =>
        {
            Tracker.Note($"preview pad: after LB tab {handle.Tabs.CurrentTab} (0 expected), last {PadInput.LastCommand}");
            Shot("preview-13-pad-card.png");
            handle.Tabs.CurrentTab = 5; // Timeline (Support now sits at 4)
            // Timed from here, not from the start: saving a screenshot takes a noticeable part of a second.
            Later.Run(0.3, () =>
            {
                Press(JoyButton.DpadLeft); // held: the press selects the latest fight, then repeats from 400 ms
                Later.Run(0.7, () => Press(JoyButton.DpadLeft, down: false));
                Later.Run(1.3, () =>
                {
                    Shot("preview-14-pad-fight.png");
                    Tap(JoyButton.B);
                    Later.Run(0.4, () =>
                    {
                        Tracker.Note($"preview pad: after B, last {PadInput.LastCommand}, recap open {GodotObject.IsInstanceValid(handle.Root) && handle.Root.IsInsideTree()}");
                        CapturePodiumFocus(dataDir, done);
                    });
                });
            });
        });
    }

    /// <summary>The podium selected with the controller: grown, brightened, with its tooltip.</summary>
    private static void CapturePodiumFocus(string dataDir, Action done)
    {
        Window root = ((SceneTree)Engine.GetMainLoop()).Root;
        var layer = new CanvasLayer { Layer = 101, Name = "WhoCarriedPreviewTopBarFocus" };
        root.AddChild(layer);
        NTopBar bar = ResourceLoader.Load<PackedScene>("res://scenes/ui/top_bar.tscn").Instantiate<NTopBar>();
        layer.AddChild(bar);
        Control? button = TopBarButton.AddTo(bar);
        Later.Run(0.8, () =>
        {
            button?.GrabFocus();
            Tracker.Note($"preview pad: podium focused {button?.HasFocus()}, controller mode {PadInput.ControllerMode}, " +
                         $"left neighbour {button?.FocusNeighborLeft}");
            Later.Run(0.8, () =>
            {
                root.GetTexture().GetImage().SavePng(Path.Combine(dataDir, "preview-15-podium-focus.png"));
                layer.QueueFree();
                PadInput.Diagnostics = false;
                done();
            });
        });
    }

    /// <summary>
    /// "close" in the flag: in English and then Chinese, hovers the hotkey cap and the close key's cap with a pushed
    /// mouse, requires both to light gold the same way, then clicks the close key's cap and requires the recap to close.
    /// Saves preview-close-{language}.png with the Esc cap hovered. The game's language is put back afterwards.
    /// </summary>
    private static async void CheckCloseKey(string dataDir, Sample sample)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        string original = LocManager.Instance.Language;
        async Task Frames(int count)
        {
            for (int i = 0; i < count; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        void Push(Viewport viewport, InputEvent input) => viewport.PushInput(input, inLocalCoords: true);
        void Move(Viewport viewport, Vector2 at) =>
            Push(viewport, new InputEventMouseMotion { Position = at, GlobalPosition = at });
        // Gold text pixels inside the cap: its idle text is pale or faint, never gold.
        int GoldPixels(Control cap)
        {
            Image screen = tree.Root.GetTexture().GetImage();
            // The screenshot is in window pixels; the cap's rect is in the scaled canvas.
            Transform2D toScreen = tree.Root.GetFinalTransform() * cap.GetGlobalTransformWithCanvas();
            Vector2 from = toScreen * Vector2.Zero, to = toScreen * cap.Size;
            int gold = 0;
            for (int y = Math.Max(0, (int)from.Y); y < Math.Min(screen.GetHeight(), (int)to.Y); y++)
                for (int x = Math.Max(0, (int)from.X); x < Math.Min(screen.GetWidth(), (int)to.X); x++)
                {
                    Color c = screen.GetPixel(x, y);
                    if (Math.Abs(c.R - RecapTheme.Gold.R) < 0.12f && Math.Abs(c.G - RecapTheme.Gold.G) < 0.12f
                        && Math.Abs(c.B - RecapTheme.Gold.B) < 0.15f) gold++;
                }
            return gold;
        }
        async Task<(bool Hovered, int Idle, int Lit)> Hover(Control cap, Vector2 away)
        {
            Viewport viewport = cap.GetViewport();
            Move(viewport, away);
            await Frames(3);
            int idle = GoldPixels(cap);
            Move(viewport, cap.GetGlobalRect().GetCenter());
            await Frames(3);
            bool hovered = viewport.GuiGetHoveredControl() == cap && ((BaseButton)cap).GetDrawMode() == BaseButton.DrawMode.Hover;
            return (hovered, idle, GoldPixels(cap));
        }

        bool passed = true;
        try
        {
            foreach (string language in new[] { "eng", "zhs" })
            {
                LocManager.Instance.SetLanguage(language);
                PanelHandle handle = RecapUi.ShowView(sample.View, sample.Icons, new CardVisuals(sample.CardFor));
                await Frames(10);
                if (handle.Root.FindChild("RecapCloseKey", true, false) is not Button closeKey)
                    throw new InvalidOperationException($"{language}: no close key cap (the game's cancel key couldn't be read)");
                Vector2 away = handle.Tabs.GetGlobalRect().GetCenter();
                (bool hotkeyHovered, int hotkeyIdle, int hotkeyLit) = await Hover(handle.Hotkey.Cap, away);
                (bool closeHovered, int closeIdle, int closeLit) = await Hover(closeKey, away);
                tree.Root.GetTexture().GetImage().SavePng(Path.Combine(dataDir, $"preview-close-{language}.png"));
                bool lit = closeHovered && closeLit > closeIdle + 20;
                Tracker.Note($"close preview {language}: hotkey cap '{handle.Hotkey.Cap.Text}' hovered {hotkeyHovered} gold {hotkeyIdle}->{hotkeyLit}; " +
                             $"close cap '{closeKey.Text}' at {closeKey.GetGlobalRect()} hovered {closeHovered} gold {closeIdle}->{closeLit}");

                Vector2 at = closeKey.GetGlobalRect().GetCenter();
                Push(closeKey.GetViewport(), new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = true });
                Push(closeKey.GetViewport(), new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = false });
                await Frames(5);
                bool closed = RecapUi.Open == null;
                Tracker.Note($"close preview {language}: lights on hover {lit}, click closes {closed}");
                passed &= lit && closed;
                if (RecapUi.Open != null) RecapUi.Hide();
                await Frames(5);
            }
        }
        catch (Exception e)
        {
            passed = false;
            Tracker.LogError("close preview", e);
        }
        finally
        {
            LocManager.Instance.SetLanguage(original);
            if (RecapUi.Open != null) RecapUi.Hide();
        }
        Tracker.Note($"close preview {(passed ? "PASS" : "FAIL")}");
    }

    /// <summary>
    /// "steam" in the flag: keeps the rendered card as steam-reference.png (to compare with Steam's copy), then
    /// presses the recap's own Save and screenshots its status. Adds one screenshot to the Steam library per run.
    /// </summary>
    private static void CheckSteam(string dataDir, Sample sample)
    {
        Tracker.Note($"preview steam: available {SteamScreenshot.Available}");
        PngExporter.Save(SummaryCard.Create(sample.View, sample.Icons), SummaryCard.Width,
            Path.Combine(dataDir, "steam-reference.png"), error =>
            {
                Tracker.Note($"preview steam: reference {error ?? "saved"}");
                PanelHandle handle = RecapUi.ShowView(sample.View, sample.Icons, new CardVisuals(sample.CardFor));
                handle.Save();
                // Two shots: the message while it is up, and the bar once it has reverted. The message clears after
                // four seconds, so a single shot at six only ever caught the empty bar — and the message's own
                // placement, clear of Close, is the thing worth looking at.
                Later.Run(2, () =>
                {
                    ((SceneTree)Engine.GetMainLoop()).Root.GetTexture().GetImage().SavePng(Path.Combine(dataDir, "preview-steam-message.png"));
                    Tracker.Note($"preview steam: message '{handle.Status.Text}'");
                });
                Later.Run(6, () =>
                {
                    ((SceneTree)Engine.GetMainLoop()).Root.GetTexture().GetImage().SavePng(Path.Combine(dataDir, "preview-steam-status.png"));
                    Tracker.Note($"preview steam: status '{handle.Status.Text}'");
                    RecapUi.Hide();
                    Tracker.Note("preview done");
                });
            });
    }

    /// <summary>Renders the Copy to clipboard picture into the data folder and logs its size, then carries on.</summary>
    private static void SaveShare(string dataDir, string file, RecapView view, Func<string?, Texture2D?> icons, Action then)
    {
        try
        {
            PngExporter.Render(ShareCard.Create(view, icons), ShareCard.PixelWidth, (image, error) =>
            {
                if (image == null)
                {
                    Tracker.Note($"preview share: {file} failed: {error}");
                }
                else
                {
                    string? saveError = PngExporter.SavePng(image, Path.Combine(dataDir, file));
                    Tracker.Note($"preview share: {file} {image.GetWidth()}×{image.GetHeight()} {saveError ?? "saved"}");
                    image.Dispose();
                }
                then();
            });
        }
        catch (Exception e)
        {
            // Never lets a broken picture stop the rest of the preview: log it and carry on.
            Tracker.LogError("preview share", e);
            Tracker.Note($"preview share: {file} failed: {e.Message}");
            then();
        }
    }

    /// <summary>Moves a virtual mouse over the chart so the screenshot shows the hover readout.</summary>
    private static void SimulateHover(Node tabs)
    {
        if (tabs.FindChild(TimelineTab.HoverLayerName, recursive: true, owned: false) is not Control layer)
        {
            Tracker.Note("preview hover: chart hover layer not found");
            return;
        }
        var motion = new InputEventMouseMotion { Position = layer.GlobalPosition + layer.Size * new Vector2(0.62f, 0.5f) };
        motion.GlobalPosition = motion.Position;
        Control? under = layer.GetViewport().GuiGetHoveredControl();
        layer.GetViewport().PushInput(motion, inLocalCoords: true);
        Tracker.Note($"preview hover: layer at {layer.GlobalPosition} size {layer.Size} visible {layer.IsVisibleInTree()}, " +
                     $"pushed {motion.Position}, hovered before {under?.Name}, after {layer.GetViewport().GuiGetHoveredControl()?.Name}");
    }

    /// <summary>A sample debuff named the way the game names it (falls back to the given label).</summary>
    private static SourceRef Debuff(string id, string fallback)
    {
        string label = fallback;
        try
        {
            if (ModelDb.AllPowers.FirstOrDefault(p => p.Id.Entry == id) is PowerModel power)
                label = GameText.Title(power.Title, fallback);
        }
        catch (Exception)
        {
            // keep the fallback
        }
        return new SourceRef(SourceKind.Power, id, label);
    }

    private static Sample BuildSample(CharacterModel[] characters)
    {
        string[] names = { "Ash", "Mika", "Sam", "Jo", "Wren" };
        var players = new List<PlayerInfo>();
        var deckModels = new Dictionary<ulong, List<CardModel>>();
        for (int i = 0; i < characters.Length; i++)
        {
            CharacterModel character = characters[i];
            ulong id = (ulong)(i + 1);
            players.Add(new PlayerInfo(id, names[i], GameReader.CharacterName(character),
                GameReader.CharacterHex(character, i), character.Id.Entry));
            deckModels[id] = character.StartingDeck
                .Concat(character.CardPool.AllCards.Where(card => card.Rarity != CardRarity.Basic).Take(10))
                .ToList();
        }

        // Sample roles wrap round a smaller party (a lone player gets everyone's debuffs).
        PlayerInfo P(int i) => players[i % players.Count];
        var stats = new RunStats();
        var rng = new Random(7);
        var help = new Random(11); // its own stream, so the other sample numbers don't change
        SourceRef vulnerable = Debuff("VULNERABLE_POWER", GameText.Native("powers", "VULNERABLE_POWER.title", "VULNERABLE_POWER"));
        SourceRef weak = Debuff("WEAK_POWER", GameText.Native("powers", "WEAK_POWER.title", "WEAK_POWER"));
        SourceRef poison = Debuff("POISON_POWER", GameText.Native("powers", "POISON_POWER.title", "POISON_POWER"));
        SourceRef doom = Debuff("DOOM_POWER", GameText.Native("powers", "DOOM_POWER.title", "DOOM_POWER"));
        SourceRef frail = Debuff("FRAIL_POWER", GameText.Native("powers", "FRAIL_POWER.title", "FRAIL_POWER"));
        SourceRef piercingWail = Debuff("PIERCING_WAIL_POWER", GameText.Native("powers", "PIERCING_WAIL_POWER.title", "PIERCING_WAIL_POWER"));
        var shiv = new SourceRef(SourceKind.Card, "SHIV", GameText.Native("cards", "SHIV.title", "SHIV"));
        int floor = 1;
        for (int act = 1; act <= 3; act++)
        {
            for (int fight = 0; fight < 5; fight++, floor += 3)
            {
                string room = fight == 4 ? "boss" : fight == 2 ? "elite" : fight == 1 && act == 2 ? "unknown" : "monster";
                stats.BeginFight(act, floor, fight == 4 ? Loc.Text("WHO_CARRIED.preview.boss") : fight == 2 ? Loc.Text("WHO_CARRIED.preview.elite") : Loc.Text("WHO_CARRIED.preview.normal"), room);
                foreach (PlayerInfo p in players)
                {
                    List<CardModel> attacks = deckModels[p.NetId].Where(card => card.Type == CardType.Attack).ToList();
                    for (int hit = 0; hit < 4 && attacks.Count > 0; hit++)
                    {
                        CardModel card = attacks[rng.Next(attacks.Count)];
                        int amount = rng.Next(8, 30) * act * (fight == 4 ? 2 : 1);
                        stats.RecordDamage(p.NetId, new SourceRef(SourceKind.Card, card.Id.Entry,
                            GameText.Title(card.TitleLocString, card.Id.Entry)), amount, blocked: rng.Next(0, 3) == 0 ? rng.Next(3, 12) : 0);
                    }
                    stats.RecordBlocked(p.NetId, rng.Next(10, 40) * act);
                }
                stats.RecordDamage(P(1).NetId, new SourceRef(SourceKind.Power, "POISON_POWER", GameText.Native("powers", "POISON_POWER.title", "POISON_POWER")),
                    rng.Next(10, 40) * act);
                if (fight % 2 == 1)
                    stats.RecordDamage(P(2).NetId, new SourceRef(SourceKind.Power, "DOOM_POWER", GameText.Native("powers", "DOOM_POWER.title", "DOOM_POWER")),
                        rng.Next(20, 60) * act);
                stats.RecordPetTanked(P(2).NetId, rng.Next(0, 12) * act); // Osty soaking hits
                stats.RecordDamage(P(3).NetId, new SourceRef(SourceKind.Orb, "LIGHTNING_ORB", GameText.Native("orbs", "LIGHTNING_ORB.title", "LIGHTNING_ORB")), rng.Next(6, 24) * act);

                stats.RecordDebuffApplied(P(0).NetId, vulnerable, rng.Next(2, 5));
                stats.RecordDebuffApplied(P(0).NetId, weak, rng.Next(0, 2));
                stats.RecordDebuffApplied(P(1).NetId, poison, rng.Next(6, 14) * act);
                stats.RecordDebuffApplied(P(1).NetId, weak, rng.Next(1, 4));
                stats.RecordDebuffApplied(P(1).NetId, vulnerable, rng.Next(0, 2));
                stats.RecordDebuffApplied(P(2).NetId, doom, rng.Next(10, 30) * act);
                stats.RecordDebuffApplied(P(2).NetId, vulnerable, rng.Next(0, 3));
                if (fight == 4) stats.RecordDebuffApplied(P(0).NetId, frail, 2);
                stats.RecordDebuffBonus(P(0).NetId, vulnerable, rng.Next(5, 20) * act);
                if (fight % 2 == 0) stats.RecordDebuffBonus(P(2).NetId, vulnerable, rng.Next(2, 8) * act);
                stats.RecordDebuffPrevented(P(1).NetId, weak, rng.Next(4, 12) * act);
                stats.RecordDebuffPrevented(P(0).NetId, weak, rng.Next(0, 4) * act);
                foreach (PlayerInfo p in players)
                {
                    stats.RecordDebuffReceived(p.NetId, weak, rng.Next(0, 3));
                    stats.RecordDebuffReceived(p.NetId, frail, rng.Next(0, 2));
                    stats.RecordDebuffReceived(p.NetId, vulnerable, rng.Next(0, 3));
                    stats.RecordDebuffCost(p.NetId, vulnerable, RunStats.CostTaken, rng.Next(0, 9) * act);
                    stats.RecordDebuffCost(p.NetId, weak, RunStats.CostDealt, rng.Next(0, 12) * act);
                    stats.RecordDebuffCost(p.NetId, frail, RunStats.CostBlock, rng.Next(0, 6) * act);
                }
                if (players.Count > 3)
                {
                    stats.RecordDebuffApplied(P(3).NetId, weak, rng.Next(1, 4));
                    stats.RecordDebuffPrevented(P(3).NetId, weak, rng.Next(2, 9) * act);
                    stats.RecordDebuffBonus(P(3).NetId, vulnerable, rng.Next(0, 6) * act);
                }
                stats.RecordDebuffApplied(P(1).NetId, piercingWail, 6);
                stats.RecordDebuffPrevented(P(1).NetId, piercingWail, rng.Next(4, 14) * act);
                stats.RecordCardGeneration(P(1).NetId, P(1).NetId, shiv, rng.Next(2, 6));
                var soul = new SourceRef(SourceKind.Card, "SOUL", GameText.Native("cards", "SOUL.title", "SOUL"));
                stats.RecordCardGeneration(P(2).NetId, P(2).NetId, soul, 2);
                stats.RecordCardGeneration(P(2).NetId, P(3).NetId, soul, 2);
                if (fight % 2 == 0) stats.RecordCardCreated(P(2).NetId, new SourceRef(SourceKind.Card, "SOVEREIGN_BLADE", GameText.Native("cards", "SOVEREIGN_BLADE.title", "SOVEREIGN_BLADE")));
                // Co-op help: everyone gives the next player round a bit of everything, each leaning on one kind so
                // every support award has a clear winner. In a solo preview these are gifts to yourself, which don't
                // count; the tab still shows the cards created for yourself.
                for (int i = 0; i < 4; i++)
                {
                    ulong from = P(i).NetId, to = P(i + 1).NetId;
                    stats.RecordSupport(from, to, SupportKind.Buffs, help.Next(0, i == 0 ? 4 : 2));
                    stats.RecordSupport(from, to, SupportKind.Energy, help.Next(0, i == 1 ? 3 : 2));
                    stats.RecordSupport(from, to, SupportKind.Block, help.Next(0, i == 2 ? 12 : 4) * act);
                    stats.RecordSupport(from, to, SupportKind.Cards, i == 2 ? fight % 2 + help.Next(0, 2) : help.Next(0, 5) / 4);
                    stats.RecordSupport(from, to, SupportKind.Draws, help.Next(0, i == 3 ? 3 : 2));
                }
                stats.EndFight();
            }
        }

        // A close call for "Clutch", and the game's badges as a finished run would have them.
        stats.RecordHp(P(2).NetId, 6, 72);
        // Co-op deaths: two for one player, one for another, none for the rest (so both card layouts show).
        if (players.Count > 1) stats.RecordDeath(P(1).NetId, 2);
        if (players.Count > 3) stats.RecordDeath(P(3).NetId);
        stats.Finished = true;
        stats.Victory = true;
        static EarnedBadge B(string id, string rarity) => new() { Id = id, Rarity = rarity };
        stats.SetBadges(P(0).NetId, new[] { B("PERFECT", "gold"), B("ELITE", "silver"), B("TEAM_PLAYER", "silver"), B("KACHING", "bronze") });
        if (players.Count > 1) stats.SetBadges(P(1).NetId, new[] { B("DEBUFFER", "silver"), B("SPEEDY", "bronze") });
        if (players.Count > 2) stats.SetBadges(P(2).NetId, Array.Empty<EarnedBadge>());
        if (players.Count > 3) stats.SetBadges(P(3).NetId, new[] { B("ELITE", "bronze"), B("SPEEDY", "silver") });

        // Gold has its own random stream, so the other sample numbers don't change.
        var purse = new Random(13);
        Dictionary<ulong, DefenseTotals> defense = players.ToDictionary(
            p => p.NetId, _ => new DefenseTotals(rng.Next(150, 400), rng.Next(60, 200), Gold: purse.Next(450, 1100)));
        Dictionary<ulong, IReadOnlyList<DeckCard>> decks = deckModels.ToDictionary(
            kv => kv.Key, kv => GameReader.DeckFacts(kv.Value));
        RecapView view = RecapBuilder.Build(stats, players, defense, Loc.Text("WHO_CARRIED.result.victory_floor", 43), victory: true, decks: decks,
            badgeText: GameReader.BadgeText, facts: new RunFacts(43, 6, 4899, "3YKUYH5798ZF"));

        Func<string?, Texture2D?> icons = GameReader.WithPowerIcons(id =>
            characters.FirstOrDefault(c => c.Id.Entry == id) is CharacterModel c ? GameReader.CharacterIcon(c) : null);
        Func<ulong, string, CardModel?> cardFor = (playerId, cardId) =>
            deckModels.TryGetValue(playerId, out List<CardModel>? inDeck) ? inDeck.FirstOrDefault(card => card.Id.Entry == cardId) : null;

        RecapView Advance()
        {
            stats.BeginFight(4, 50, Loc.Text("WHO_CARRIED.preview.final"), "boss");
            CardModel? attack = deckModels[P(0).NetId].FirstOrDefault(card => card.Type == CardType.Attack);
            if (attack != null)
                stats.RecordDamage(P(0).NetId, new SourceRef(SourceKind.Card, attack.Id.Entry,
                    GameText.Title(attack.TitleLocString, attack.Id.Entry)), 1500);
            stats.RecordDamage(P(1).NetId, new SourceRef(SourceKind.Power, "POISON_POWER", GameText.Native("powers", "POISON_POWER.title", "POISON_POWER")), 380);
            stats.RecordDamage(P(2).NetId, new SourceRef(SourceKind.Power, "DOOM_POWER", GameText.Native("powers", "DOOM_POWER.title", "DOOM_POWER")), 420);
            stats.RecordDebuffApplied(P(2).NetId, weak, 6);
            stats.RecordDebuffPrevented(P(2).NetId, weak, 90);
            stats.RecordDebuffBonus(P(0).NetId, vulnerable, 120);
            stats.EndFight();
            return RecapBuilder.Build(stats, players, defense, Loc.Text("WHO_CARRIED.result.victory_floor", 50), victory: true, decks: decks,
                badgeText: GameReader.BadgeText, facts: new RunFacts(50, 6, 5320, "3YKUYH5798ZF"));
        }

        return new Sample(view, icons, cardFor, Advance);
    }
}
