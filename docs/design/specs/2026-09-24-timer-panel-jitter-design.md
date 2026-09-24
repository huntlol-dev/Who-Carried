# Stable timer width in the recap

Date: 2026-09-24. Draft for review; documentation only. Branch: `bug/timer-panel-jitter`. Do not commit.

## Problem and evidence

With the statistics panel open, its ticking timer changes width and moves the statistics and controls to its right. The [investigation](../../investigations/2026-09-24-timer-panel-jitter.md) traced this to the proportional-font label in `RecapPanel.TopBar`, with no reserved width. The installed English font's raw advances differ by about 5.4 design pixels between `1:00` and `1:01`.

The report mentions **Always show the Timer**. In the inspected game v0.111.0 that setting affects the game's timer visibility, while the recap independently reads elapsed time. The fix belongs in the recap layout. Live reproduction, including a setting-on/off comparison, is still pending.

## Intent and scope

Keep the recap's neighboring statistics still while its time updates. Preserve the current font, size, colour, icon, left alignment, duration format and refresh cadence. This is a bounded UI change; both this spec and its [plan](../plans/2026-09-24-timer-panel-jitter.md) were explicitly requested.

Production changes stay in `RecapPanel.cs`. A focused developer preview may exercise the real Godot layout. No timer patches, preference changes, Core changes, new dependencies, localization keys, changes to other statistics, or changes to the exported summary card.

## Decision

Give the timer's **text label** a font-aware minimum width large enough for a two-digit hour duration (`HH:MM:SS`) from the first positive time. Keep the icon in the existing stat container. Shorter strings remain left aligned, leaving spare space after the text.

Calculate the reservation from the label's actual theme font and scaled font size:

1. Measure each digit `0` through `9`; use the largest advance. Do not assume `8` is widest.
2. Reserve six such digit advances and two colon advances, plus the label's outline allowance and four design pixels of padding, rounded up in UI units.
3. Measure the actual text as a safety check. The reservation must cover it too.
4. Assign the larger of the reservation and the label's existing minimum width. Never shrink it during the lifetime of that panel.

This is a conservative reservation, not a claim that isolated glyph advances perfectly describe every font's shaping. The actual-text check protects unusual fonts; verification must show that the shipped English and Simplified Chinese fonts do not outgrow the initial reservation on ordinary ticks. Any safety growth remains retained, preventing repeated expansion/contraction.

Measuring a handful of characters on each existing refresh is sufficient. Do not introduce a static cache, per-frame update, or a global change to `Kit.Text`/`Stat`. Re-reading the label's actual font metrics avoids a separate cache-invalidation mechanism.

### Duration boundaries

| Situation | Required behavior |
|---|---|
| `1:00` → `1:01`, or any other ordinary digit change | Text changes; neighboring positions do not |
| `9:59` → `10:00` | Uses the space already reserved |
| `59:59` → `1:00:00` | Uses the space already reserved; retain existing formatting |
| `9:59:59` → `10:00:00` | Uses the space already reserved |
| `99:59:59` → `100:00:00` | One expansion to a three-hour-digit reservation is allowed; later ticks and shorter updates must not shrink it |
| An already long run when the panel opens | Reserve enough hour digits immediately, with no truncation |
| Missing facts or nonpositive seconds | Timer stays hidden as today; the first appearance may move the row once |
| Font metrics grow | May increase the reservation to keep the full text visible; never oscillate |
| Panel recreated after a resize or reopening | Recalculate for its current scaled font and initial duration |

For hours beyond two digits, count the hour digits in the formatted `H:MM:SS` text, reserve that many plus four minute/second digits, and retain the largest width seen. This defines long-run behavior without setting a gameplay time limit. Extremely long strings remain subject to the existing top bar's total available space; the fix must not introduce clipping or ellipsis to hide them.

### Alternatives considered

- **Tabular/monospace numerals:** changes typography or depends on font support, and still changes width at format transitions. Not selected.
- **Keep the largest current-text width seen:** stops contraction but still shifts the row as wider digits first occur. Not sufficient by itself.
- **Hard-coded pixel width:** small implementation, but ignores language and scale. Use measured reservation instead.

The trade-off is a larger gap after short durations. At normal supported layouts it must still leave the hotkey and Close usable. If visual checks expose new overlap, revisit the reservation/layout before calling the fix complete; do not silently reduce the timer font or clip its text.

## Global constraints

- Do not commit, push, stash, merge, or change branches for this work unless the user later requests it.
- Do not deploy, launch, or control the game without explicit user authorization.
- No new dependencies, game patches, settings, localization keys, or Core changes.
- Use the existing duration format and refresh cadence.
- Use the label's actual font metrics and scaled size; additional padding is four design pixels through `k.U(4)`.
- Keep the timer left aligned and fully visible; do not add trimming, clipping, font shrinking, or a replacement font.
- Use `C:\Program Files\dotnet\dotnet.exe` for builds and standalone tests.

## Acceptance and evidence

Use the actual `RecapPanel` with a constant sample view and only `RunFacts.Seconds` changing. After Godot finishes layout, compare the timer label's width and the team-damage stat's horizontal position. Consecutive positive times within the initial two-hour-digit capacity must stay within 0.5 UI pixels of the baseline. Check the specific transitions above and a sweep of all seconds in representative minutes, forward and backward, so a growing-only workaround cannot pass accidentally.

Check missing facts/zero separately: visibility changes are allowed to reflow. Check three-digit hours separately: one expansion is allowed, followed by stability and no contraction. Reopen with a long duration as well as reaching it through updates.

Run English and Simplified Chinese checks and inspect the whole bar at 1600×900, 1920×1080, and a narrow 1280×1024 canvas. Check resizing an open panel, a four-player party, the hotkey controls, and Close. Verify font fallback if a language font is unavailable. Final live checks compare the game setting on/off while elapsed time advances.

The current standalone test suite does not compile Godot UI. Build and Core test success are regression checks, not proof that jitter is fixed. Preserve failing-before/passing-after layout measurements and visual evidence. If permission for the runtime check is absent, report it as pending rather than claiming visual success.
