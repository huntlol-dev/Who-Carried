using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Runs;
using WhoCarried.Core;
using WhoCarried.Game;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// Owns the overlay: F8 polling, the canvas layer, opening on the end-of-run screen, image export, and keeping the
/// open recap live (refreshed in place when the stats change, and every couple of seconds for floor, deck and HP).
/// </summary>
internal static class RecapUi
{
    private const double RefreshDelay = 0.25;
    private const double IdleRefresh = 1.5;
    private const double ResizeDelay = 0.3;

    private static CanvasLayer? _layer;
    private static Control? _panel;
    private static CardVisuals? _cards;
    private static bool _hotkeyWasDown;

    private static Live? _live;
    private static IRunState? _liveRun;
    private static RecapView? _currentView;
    private static bool _refreshPending;
    private static int _generation;

    private static PanelHandle? _handle;
    private static Func<string?, Texture2D?>? _icons;
    private static Vector2 _laidOutFor;
    private static bool _resizePending;
    private static bool _copying;
    private static int _statusVersion;
    private static int _copiedVersion;

    /// <summary>The open recap, or null. A resize replaces it, so hold on to this only for the moment.</summary>
    public static PanelHandle? Open => _handle != null && GodotObject.IsInstanceValid(_handle.Root) ? _handle : null;

    public static void Install()
    {
        HotkeyBinding.Load(Tracker.DataDir);
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            tree.ProcessFrame += OnFrame;
            tree.Root.SizeChanged += OnScreenResized;
            Tracker.Changed += OnStatsChanged;
            DevPreview.StartIfFlagged(Tracker.DataDir);
            Replay.StartIfFlagged(Tracker.DataDir);
        }
        else
        {
            Log.Warn("[WhoCarried] no SceneTree at init; F8 toggle unavailable");
        }
    }

    public static void Toggle()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel)) Hide();
        else Show();
    }

    /// <summary>Opens the recap for the current (or just-finished) run and keeps it live.</summary>
    public static void Show()
    {
        IRunState? run = Tracker.CurrentRun;
        if (run == null) return;
        ShowView(BuildView(run), GameReader.WithPowerIcons(id => GameReader.CharacterIcon(run, id)),
            new CardVisuals((playerId, cardId) => GameReader.DeckCardModel(run, playerId, cardId)));
        _liveRun = run;
        int generation = _generation;
        IdleTick(generation);
    }

    public static PanelHandle ShowView(RecapView view, Func<string?, Texture2D?> icons, CardVisuals? cards)
    {
        Hide();
        _cards = cards;
        _currentView = view;
        PanelHandle handle = RecapPanel.Create(view, icons, cards, Hide, h => Export(_currentView ?? view, icons, h),
            h => CopyToClipboard(_currentView ?? view, icons, h));
        _panel = handle.Root;
        _handle = handle;
        _icons = icons;
        _laidOutFor = RecapPanel.ScreenSize();
        _live = handle.Live;
        EnsureLayer().AddChild(handle.Root);
        HotkeyRebind.Attach(handle);
        PadInput.Attach(handle);
        return handle;
    }

    /// <summary>Pushes a newer view into the open recap (used by the dev preview's live check).</summary>
    public static void Apply(RecapView view)
    {
        if (_live == null || _panel == null || !GodotObject.IsInstanceValid(_panel)) return;
        _currentView = view;
        _live.Apply(view);
    }

    /// <summary>Closes the panel. Cards go back to the game's pool first, before their slots are freed.</summary>
    public static void Hide()
    {
        _generation++;
        _live = null;
        _liveRun = null;
        _currentView = null;
        _cards?.ReleaseAll();
        _cards = null;
        if (_panel != null && GodotObject.IsInstanceValid(_panel)) _panel.QueueFree();
        _panel = null;
        _handle = null;
        _icons = null;
    }

    /// <summary>
    /// The game rescales its canvas whenever the window changes (resizing, fullscreen, and with the Auto aspect ratio
    /// on its own as screens load), and the recap is laid out for one size. Rebuild the open recap once it settles.
    /// </summary>
    private static void OnScreenResized()
    {
        if (_resizePending || Open == null) return;
        _resizePending = true;
        Later.Run(ResizeDelay, () =>
        {
            _resizePending = false;
            Relayout();
        });
    }

    /// <summary>Reopens the recap at the new size on the same view, still live if it was.</summary>
    private static void Relayout()
    {
        if (Open is not PanelHandle old || _currentView is not RecapView view || _icons is not { } icons) return;
        Vector2 screen = RecapPanel.ScreenSize();
        if (screen == _laidOutFor) return;
        IRunState? run = _liveRun;
        int tab = old.Tabs.CurrentTab;
        PanelHandle handle = ShowView(view, icons, _cards);
        handle.Tabs.CurrentTab = tab;
        Tracker.Note($"recap laid out again for {screen}");
        if (run == null) return;
        _liveRun = run;
        IdleTick(_generation);
    }

    /// <summary>Called after the victory/defeat screen is ready: open the recap once its banner has animated in.</summary>
    public static void OnGameOverScreen(NGameOverScreen screen)
    {
        screen.TreeExiting += Hide;
        Later.Run(1.5, () =>
        {
            if (GodotObject.IsInstanceValid(screen) && screen.IsInsideTree()) Show();
        });
    }

    private static RecapView BuildView(IRunState run)
    {
        RunStats stats = Tracker.Stats;
        bool? victory = stats.Finished ? stats.Victory : null;
        return RecapBuilder.Build(stats, GameReader.Players(run), GameReader.Defense(run),
            GameReader.Header(run, victory), victory, GameReader.Decks(run), GameReader.BadgeText, GameReader.Facts(run));
    }

    /// <summary>Stats changed: refresh shortly, at most once per <see cref="RefreshDelay"/>, however many hits land.</summary>
    private static void OnStatsChanged()
    {
        if (_live == null || _liveRun == null || _refreshPending) return;
        _refreshPending = true;
        Later.Run(RefreshDelay, () =>
        {
            _refreshPending = false;
            Refresh();
        });
    }

    /// <summary>Floor, deck and HP changes don't raise events; a slow tick picks them up while the recap is open.</summary>
    private static void IdleTick(int generation)
    {
        Later.Run(IdleRefresh, () =>
        {
            if (generation != _generation) return;
            Refresh();
            IdleTick(generation);
        });
    }

    private static void Refresh()
    {
        if (_liveRun == null) return;
        Apply(BuildView(_liveRun));
    }

    /// <summary>
    /// Saves the summary card: into the player's Steam screenshots when the game runs on Steam, the same on every OS;
    /// otherwise as a PNG in the game's data folder.
    /// </summary>
    private static void Export(RecapView view, Func<string?, Texture2D?> icons, PanelHandle handle)
    {
        SetStatus(handle, Loc.Text("WHO_CARRIED.export.saving"));
        PngExporter.Render(SummaryCard.Create(view, icons), SummaryCard.Width, (image, error) =>
        {
            if (image == null)
            {
                Tracker.Note($"export failed: {error}");
                Say(handle, Loc.Text("WHO_CARRIED.export.failed"));
                return;
            }
            if (SteamScreenshot.Available)
            {
                SteamScreenshot.Write(image, $"{RecapTexts.ModName} {view.Header}", steamError =>
                {
                    Tracker.Note(steamError == null ? "exported to Steam screenshots" : $"Steam export failed: {steamError}");
                    Say(handle, steamError == null ? Loc.Text("WHO_CARRIED.export.steam") : Loc.Text("WHO_CARRIED.export.failed"));
                });
                return;
            }
            string result = view.Victory switch { true => "victory", false => "defeat", null => "in-progress" };
            string path = Path.Combine(PngExporter.FallbackFolder, $"run-{DateTime.Now:yyyy-MM-dd_HHmm}-{result}.png");
            string? saveError = PngExporter.SavePng(image, path);
            Tracker.Note(saveError == null ? $"exported {path}" : $"export failed: {saveError}");
            Say(handle, saveError == null ? Loc.Text("WHO_CARRIED.export.saved", PngExporter.FallbackFolder) : Loc.Text("WHO_CARRIED.export.failed"));
        });
    }

    /// <summary>
    /// Copy to clipboard: renders the picture players paste into Discord (<see cref="ShareCard"/>) and puts it on the
    /// clipboard. A click while a copy is under way is ignored. The recap may close before it's done; nothing here then
    /// touches it, and the next copy still works.
    /// </summary>
    private static void CopyToClipboard(RecapView view, Func<string?, Texture2D?> icons, PanelHandle handle)
    {
        if (_copying) return;
        _copying = true;
        // The last copy's Copied look ends now, not two seconds after it started: kept, it would sit through this
        // copy's "Copying...", and beside the failure message if this one fails.
        ++_copiedVersion;
        if (GodotObject.IsInstanceValid(handle.Root)) handle.ShowCopied(false);
        SetStatus(handle, Loc.Text("WHO_CARRIED.copy.copying"));
        void Failed(string? reason)
        {
            _copying = false;
            Tracker.Note($"copy failed: {reason}");
            Say(handle, Loc.Text("WHO_CARRIED.copy.failed", Loc.Text("WHO_CARRIED.action.save_image")));
        }
        try
        {
            PngExporter.Render(ShareCard.Create(view, icons), ShareCard.PixelWidth, (image, error) =>
            {
                if (image == null)
                {
                    Failed(error);
                    return;
                }
                int width = image.GetWidth(), height = image.GetHeight();
                ImageClipboard.Copy(image, (copyError, pngBytes) =>
                {
                    image.Dispose();
                    if (copyError != null)
                    {
                        Failed(copyError);
                        return;
                    }
                    _copying = false;
                    Tracker.Note(string.Create(CultureInfo.InvariantCulture,
                        $"copied summary to clipboard, {width}×{height}, {pngBytes / 1048576.0:0.0} MB"));
                    Say(handle, Loc.Text("WHO_CARRIED.copy.done", OperatingSystem.IsMacOS() ? "Cmd+V" : "Ctrl+V"));
                    if (!GodotObject.IsInstanceValid(handle.Root)) return;
                    int copiedVersion = ++_copiedVersion;
                    handle.ShowCopied(true);
                    Later.Run(2.0, () =>
                    {
                        if (copiedVersion == _copiedVersion && GodotObject.IsInstanceValid(handle.Root)) handle.ShowCopied(false);
                    });
                });
            }, logAs: "copy");
        }
        catch (Exception e)
        {
            Tracker.LogError("copy: building the picture", e);
            Failed(e.Message);
        }
    }

    /// <summary>
    /// Sets the status line and bumps its version, so a stale clear — or a stale write racing in behind a newer one —
    /// can never stomp a message that came after it. A panel that has closed is left alone, version and all: a late
    /// message for it (a copy that outlived it) mustn't cancel the clear of a panel opened since. Returns the new
    /// version, or null when nothing was written.
    /// </summary>
    private static int? SetStatus(PanelHandle handle, string text)
    {
        if (!GodotObject.IsInstanceValid(handle.Status)) return null;
        handle.Status.Text = text;
        return ++_statusVersion;
    }

    /// <summary>A message in the status line, cleared four seconds later — unless something newer has been said since.</summary>
    private static void Say(PanelHandle handle, string text)
    {
        if (SetStatus(handle, text) is not int version) return; // nothing was written, so there's nothing to clear
        Later.Run(4.0, () =>
        {
            if (version == _statusVersion && GodotObject.IsInstanceValid(handle.Status)) handle.Status.Text = "";
        });
    }

    private static void OnFrame()
    {
        try
        {
            bool down = HotkeyBinding.IsDown();
            if (down && !_hotkeyWasDown) Toggle();
            _hotkeyWasDown = down;
        }
        catch (Exception e)
        {
            Tracker.LogError("hotkey", e);
        }
    }

    private static CanvasLayer EnsureLayer()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) return _layer;
        _layer = new CanvasLayer { Layer = 100, Name = "WhoCarriedLayer" };
        ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred(Node.MethodName.AddChild, _layer);
        return _layer;
    }
}
