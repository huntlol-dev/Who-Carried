# Investigation: ticking timer shifts the recap's top-bar statistics

Investigated 2026-09-24 on `bug/timer-panel-jitter`, starting at `533be17` (1.2.0). Evidence: current mod source, installed game v0.111.0 assembly and packaged scene/font resources. No production code changed, and no game was launched or controlled.

## Finding

The reported width difference is real. The recap places an unconstrained, proportional-font timer label inside nested horizontal containers. Changing the timer text changes its minimum width, which moves the following statistics and controls. This is a source-and-font-confirmed layout defect; its exact on-screen displacement has not been reproduced in Godot.

The report's association with **Always show the Timer** needs a distinction: in the inspected game version, that setting controls the game's own timer visibility. The mod's recap reads elapsed time independently and does not consult the setting. Its jitter should therefore also occur with that setting disabled while elapsed time advances. That prediction remains to be checked in game; the public game branch was not inspected in this investigation.

## Evidence chain

1. `GameReader.Facts` (`src/WhoCarried/Game/GameReader.cs:318`) reads `RunManager.Instance.RunTime` into `RunFacts.Seconds`.
2. `RecapUi` refreshes the open recap every 1.5 seconds, plus updates coalesced over 0.25 seconds after tracker events. `BuildView` reads those facts again; `Live.Apply` invokes the registered UI updates.
3. `RecapTexts.Duration` (`src/WhoCarried/UI/RecapTexts.cs:46`) formats seconds as `M:SS` or `H:MM:SS`. Equal character counts do not imply equal pixel widths.
4. `RecapPanel.TopBar` creates floor, time, ascension and team-damage readouts in that order, followed by party portraits and hotkey controls. Its `Apply` assigns the newly formatted string directly to `timeText.Text`.
5. `RecapPanel.Stat` builds each readout as an `HBoxContainer`: icon, then label. The timer uses the same generic helper with no reserved horizontal space. `Kit.Text` does not assign a minimum width; `Kit.Center` only changes vertical alignment. The outer row's explicit size does not fix each child's width.
6. The timer's bold 24-point label uses the current language's substitute font, or Kreon Bold as the fallback. The English Kreon font has unequal digit advances. The installed `themes/kreon_bold_shared.tres` wraps `fonts/kreon_bold.ttf` without tabular-digit configuration.

At an otherwise unchanged view, the predicted movement is in ascension, damage, party and hotkey controls after the timer. Title and floor precede it; Close is positioned separately. The background bar has a fixed size. This explains the impression of the top panel jittering without implying that the whole overlay is being rebuilt or translated. The trailing expanding spacer normally absorbs the width change before the status label.

## Font measurement

Read the installed game's packed Kreon Bold resource without running the game: decompress the resource, locate its embedded TrueType font and inspect its `cmap`, `head` and `hmtx` tables. Units per em: 1024. Digit `0` advances 604 units; digit `1` advances 373 units.

At the recap's design font size of 24:

| String | Sum of glyph advances |
|---|---:|
| `1:00` | 42.820 px |
| `1:01` | 37.406 px |
| `9:59` | 44.227 px |
| `10:00` | 56.977 px |
| `59:59` | 56.625 px |
| `1:00:00` | 76.898 px |
| `9:59:59` | 75.422 px |
| `10:00:00` | 91.055 px |

The reported `1:00` to `1:01` transition loses approximately **5.414 design pixels** in raw glyph advance. These values establish proportional digits, not exact rendered Godot sizes: shaping, rounding, outline handling and screen scaling affect final label measurements. Other languages can select different fonts.

## What the game setting does

Decompiled installed v0.111.0:

- `NRunTimer.RefreshVisibility` reads `SaveManager.Instance.PrefsSave.ShowRunTimer`. When enabled it shows the game's timer; otherwise it shows it when the map or a capstone screen is open.
- `NRunTimer` updates its own label every second. This is separate from the mod's 1.5-second refresh.
- `RunManager.RunTime` derives elapsed seconds from run timestamps, previous sessions, pause state and win time. It does not depend on `ShowRunTimer`.
- The game's `scenes/ui/top_bar.tscn` already gives its own `TimerLabel` a 120-by-80 minimum size. The recap creates its own controls and does not inherit that reservation. This is a useful comparison, not proof that the game's timer can never outgrow its reservation.

## Recommended fix direction

Reserve stable horizontal space specifically for the recap's timer, using the actual font and scaled size. Keep the displayed time updating within that space. Account for minute/hour format changes and multi-digit hours, and remeasure when the panel is recreated for a different scale or language. Do not recalculate the reservation from each current string: that would preserve the jitter.

A font-aware reservation should cover the widest supported formatted value. Do not assume `8` is the widest digit: in the inspected font, `0` is wider. A minimum width alone is only sufficient when every expected string fits it; longer durations need an explicit overflow/growth policy. Tabular figures alone would not prevent width changes when the number of characters changes, and their availability depends on the font.

Keep the change local to the timer. Other values such as floor and team damage can also change width, but that is separate from a continuously ticking clock. No change to the game's preference or timer patch is indicated by the evidence.

## Verification still needed for a fix

- In a Godot UI reproduction, hold every other field constant and alternate `1:00` / `1:01`; record the next visible stat's horizontal position after layout settles. It should change before the fix and remain constant afterward.
- Exercise `9:59` / `10:00`, `59:59` / `1:00:00`, and `9:59:59` / `10:00:00`, checking both position stability and complete visible text.
- Check English and Simplified Chinese, normal and narrow screen layouts, and resizing while the recap is open.
- In game, compare the setting on/off with the recap open and elapsed time advancing. Distinguish the recap's bar from the underlying game's bar, then check closing/reopening the recap.

The existing standalone test project compiles Core and Localization, not the Godot UI. Passing those tests would not demonstrate this layout fix. No build or test-suite run was needed for this investigation-only change.
