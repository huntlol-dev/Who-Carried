# Stable recap timer — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task, or superpowers:subagent-driven-development if the user selects delegation. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop timer updates moving the recap's neighboring statistics and controls.

**Architecture:** Reserve font-scaled space on the timer label in `RecapPanel`, sized for two hour digits before ordinary ticks begin. Retain any necessary growth for longer times or changed font metrics. A dedicated developer preview drives the real panel with changing seconds and records actual geometry.

**Tech Stack:** C# / .NET 9; the installed game's GodotSharp API; existing `DevPreview`, `RecapUi` and console test runner.

**Spec:** [Stable timer width in the recap](../specs/2026-09-24-timer-panel-jitter-design.md).

**Status:** Draft for review. No implementation has been performed or compiled from this plan. The user requested documents only and no commits. Native execution is recommended for this small, sequential fix; execution is not started by creating these documents.

## Global constraints

- Do not commit, push, stash, merge, or change branches for this work unless the user later requests it.
- Do not deploy, launch, or control the game without explicit user authorization.
- No new dependencies, game patches, settings, localization keys, or Core changes.
- Use the existing duration format and refresh cadence.
- Use the label's actual font metrics and scaled size; additional padding is four design pixels through `k.U(4)`.
- Keep the timer left aligned and fully visible; do not add trimming, clipping, font shrinking, or a replacement font.
- Use `C:\Program Files\dotnet\dotnet.exe` for builds and standalone tests.

## Review focus

1. Duration format boundaries must not escape the initial reservation; Task 1 includes every boundary through two-digit hours.
2. Longer runs must remain readable and retain their expanded width; Task 1 separately checks three-digit hours and reopening.
3. Hidden-to-visible transitions must preserve today's visibility rules; Task 1 checks null facts and zero outside the stability sequence.
4. Substitute fonts and scaled canvases must fit without squeezing controls off the screen; Task 3 checks both languages, three canvas sizes and resize/reopen.
5. Setting-on/off and unrelated stat updates must not be confused with this bug; Task 1 changes only seconds, and Task 3 compares the game preference with otherwise stable data.

## Files and responsibilities

| File | Planned change |
|---|---|
| `src/WhoCarried/UI/RecapPanel.cs` | Timer-width helper and call; stable names for the timer label/stat and team stat used by the preview |
| `src/WhoCarried/UI/DevPreview.cs` | Explicit `timer` preview mode, isolated from the existing screenshot/controller preview |
| `README.md` | Document the new developer preview flag after implementation |
| `docs/investigations/2026-09-24-timer-panel-jitter.md` | Append measured before/after results and remaining limits |

Read `RecapPanel.TopBar`/`Stat`, `Kit.Text`/`Measure`, `RecapUi.ShowView`/`Apply`, `RecapTexts.Duration`, and the investigation before editing. `RunFacts` and `RecapView` are records in `Core/RecapBuilder.cs`, so a preview can replace only `Seconds` with `with` expressions. `ShowView` does not attach the live-run idle refresh, which makes sample updates deterministic.

## Task 1: Add a failing Godot layout reproduction

**Interfaces:** Consume `RecapUi.ShowView(RecapView, Func<string?, Texture2D?>, CardVisuals?)` and `RecapUi.Apply(RecapView)`. Add private `DevPreview.CheckTimer(string dataDir, Sample sample)`. Do not change public handles or add Core tests for Godot layout.

- [ ] **Name the three existing controls** immediately after the stat declarations in `RecapPanel.TopBar`. This adds diagnostic identity without fixing their layout:

```csharp
time.Name = "RecapTimerStat";
timeText.Name = "RecapTimerText";
team.Name = "RecapTeamStat";
```

- [ ] **Route the opt-in preview.** Immediately after `Sample sample = BuildSample(characters);` in `DevPreview.Run`, before the existing `steam` branch, add:

```csharp
if (wanted == "timer")
{
    CheckTimer(dataDir, sample);
    return;
}
```

The existing options parser already supports a language after the first word. `timer eng` and `timer zhs` therefore use the existing language-selection path and four default sample characters. Preserve every existing flag mode.

- [ ] **Implement the focused preview.** Use the following body and helpers inside `DevPreview`. It tests ordinary ticks against the first reservation, then larger-hour growth separately. The async entry catches and logs failures rather than bringing down the game. It never injects controller input or calls the existing `CaptureTopBar` path.

```csharp
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
    SceneTree tree = root.GetTree();
    Vector2 previous = new(float.NaN, float.NaN);
    int stable = 0;
    for (int frame = 0; frame < 30; frame++)
    {
        await root.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (!GodotObject.IsInstanceValid(root) || !root.IsInsideTree())
            throw new InvalidOperationException("timer preview panel closed");
        Vector2 current = new(label.Size.X, team.GlobalPosition.X);
        stable = current == previous ? stable + 1 : 0;
        if (stable >= 3) return current;
        previous = current;
    }
    throw new InvalidOperationException("timer layout did not settle within 30 frames");
}
```

- [ ] **Build the diagnostic-only change** with `& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release`. Resolve API/compiler issues before treating the preview as usable. This plan's code is a proposed implementation, not a previously compiled artifact.
- [ ] **Capture the failing baseline before adding the fix**, once game deployment/launch is authorized. Use `preview.flag` content `timer eng` in the mod's data directory and the established Steam launch route. Verify the loaded DLL is the build just produced. Expected: `timer preview FAIL` reporting changed width/team X at 61 seconds. A load error, missing control, or timeout is a broken reproduction, not evidence of the timer defect.
- [ ] **If runtime authorization is absent, leave this checkpoint pending.** Build results cannot replace the failing layout evidence. Do not silently run the game.

## Task 2: Reserve the timer width locally

**Interface:** Add private `RecapPanel.ReserveTimerWidth(Kit k, Label label, string duration)`. Consume the same formatted string already assigned to `timeText.Text`; preserve all other `Apply` behavior.

- [ ] **Add this helper near `Stat`:**

```csharp
private static void ReserveTimerWidth(Kit k, Label label, string duration)
{
    if (duration.Length == 0) return;
    string[] parts = duration.Split(':');
    int hourDigits = parts.Length == 3 ? Math.Max(2, parts[0].Length) : 2;
    Font? font = label.GetThemeFont("font");
    int size = label.GetThemeFontSize("font_size");
    float Width(string text) => font?.GetStringSize(text, HorizontalAlignment.Left, -1, size).X
        ?? text.Length * size;
    float widestDigit = 0;
    for (char digit = '0'; digit <= '9'; digit++)
        widestDigit = Math.Max(widestDigit, Width(digit.ToString()));
    float reserved = (hourDigits + 4) * widestDigit + 2 * Width(":")
        + label.GetThemeConstant("outline_size");
    float width = MathF.Ceiling(Math.Max(reserved, Kit.Measure(label)) + k.U(4));
    if (width > label.CustomMinimumSize.X)
        label.CustomMinimumSize = new Vector2(width, label.CustomMinimumSize.Y);
}
```

The fallback uses one em per character as a conservative starting reservation when a theme font cannot be read; the label's natural minimum and the actual-text check still protect rendering. This fallback requires visual verification, not an assertion that every possible font fits an em.

- [ ] **Call the helper immediately after assigning `timeText.Text`:**

```csharp
string duration = RecapTexts.Duration(v.Facts?.Seconds ?? 0);
timeText.Text = duration;
ReserveTimerWidth(k, timeText, duration);
time.Visible = duration.Length > 0;
```

Do not modify `Stat`, `Kit.Text`, duration formatting, font selection, timer polling, or the game's own controls. No event subscriptions or static cache are necessary. The maximum is retained by the label itself and reset when the panel is recreated.

- [ ] **Build and run the existing regression suite:**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests
```

Require successful compilation and no test failures. Record actual totals; do not copy a historical test count.

- [ ] **Repeat the authorized timer preview.** Expected: `timer preview PASS`; ordinary widths/team positions stay constant, three-digit-hour growth is retained, and the duration remains visible. Compare the same failing baseline transition from Task 1. If the old failure remains, return to the measured geometry before adding unrelated layout changes.
- [ ] **Exercise initial long-run reservation.** Temporarily change the preview's initial `ShowView(At(60), ...)` and baseline `Read(60)` to 360000, rerun the same sequence, then restore both to 60. Expected: the panel opens at the larger reservation and never contracts. Keep the output as evidence; this is a test-fixture variation, not a product default.

## Task 3: Visual acceptance and documentation

- [ ] **Run `timer eng` and `timer zhs`** with permission, at 1600×900, 1920×1080 and 1280×1024 canvas sizes. Record actual canvas sizes from Godot, not just the operating-system window dimensions. Copy both diagnostic images (`preview-timer-normal.png` for the ordinary reservation and `preview-timer.png` after long-run growth) and output to uniquely named local evidence files before the next run overwrites them.
- [ ] **Inspect the complete bar** with four players: timer text/outline fully visible; spare space acceptable; ascension, damage, party, hotkey and Close do not overlap. Repeat once with the normal language font fallback path in a disposable diagnostic build by bypassing `LoadSubstituteFont` only for that check; restore `RecapTheme.cs` immediately afterward and ensure it is absent from the final diff. Do not claim this simulation proves all third-party fonts.
- [ ] **Check real panel lifecycle with permission:** open the recap during a running game, resize it, close/reopen, and verify the time is current and all controls remain usable. The preview deliberately aborts on replacement instead of driving a different panel; rerun it for each settled canvas.
- [ ] **Compare “Always show the Timer” on/off** during a stable run state. Confirm neighboring recap statistics stay still in both cases. Let the owner change the setting or obtain permission to control the game; restore the original setting after the comparison. Changes caused by actual floor/damage updates are outside this timer-only assertion.
- [ ] **Document the preview** by adding to the existing `preview.flag` bullet in `README.md`: `Use timer eng or timer zhs to check that changing elapsed time leaves the recap's neighboring statistics in place; results are logged and preview-timer.png is saved on success.` Format the flag values and filename as inline code.
- [ ] **Append verification evidence to the investigation:** exact game version, branch/build, language/canvas, before/after width/team-X values, outcome of boundary/long-run tests, visual observations, and any checks still pending. Do not replace the original investigation's limits with an unsupported success claim.
- [ ] **Review the final diff** for timer-only scope and run `git diff --check`. If the fallback experiment changed and restored source, rebuild the final tree. Re-run the suite only if subsequent production changes justify it.
- [ ] **Report the result and remaining checks. Leave all changes uncommitted.** No deployment, publication, or version bump is part of this plan.
