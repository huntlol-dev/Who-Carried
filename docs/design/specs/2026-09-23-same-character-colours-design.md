# Telling apart players on the same character

Date: 2026-09-23. Requested by players; designed with the owner in one session, with mockups and a shade playground.

## Problem

Each player's colour is their character's own `NameColor` (`GameReader.CharacterHex`). Two Ironclads get the same hex, and nothing tells them apart.

Most views are fine, because a name or portrait sits beside the colour: the scoreboard cards, Sources, Decks, Defense, Awards. Colour alone identifies a player in three places, and those are where they blend:

- **Timeline:** two lines of one colour, and two identical pills in the legend.
- **The climb** (the scoreboard's stacked bars, and the exported image's): segments have no gaps, so two same-coloured players merge into one block.
- **The hover readout** on either chart: identical swatches beside the names.

## Decision

Four changes, all generic — nothing names a character or a colour:

1. **Players who share a colour get gentle shades of it**, assigned in `Core` when the recap is built, and used everywhere the player's colour is: charts, legend, readout, names, bars, card frame and gem.
2. **The climb draws a thin gap between every segment**, for every player.
3. **The Timeline names each line at its right-hand end.**
4. **Scoreboard card portraits are zoomed out partway**, the strips this opens at the sides filled with the portrait's own edge colour.

A dev-preview mode with chosen characters is added to check all four in game, the exported image included.

No new on-screen text, so no new `eng.json`/`zhs.json` keys.

## 1. Shades

### Who gets one

- Players are grouped by **identical colour** (hex compared case-insensitively, `#` ignored), not by character. That catches two Ironclads, and two modded characters whose authors chose the same `NameColor`.
- In each group the **first player in join order keeps the true colour**. Every later one gets a shade. Join order doesn't change during a run, so no one's colour shifts mid-run; ranking would repaint players whenever the lead changed.
- A player alone on their colour is never touched.
- Only the colour changes. Name, portrait, gem art and icons stay the character's own.

### Where

A pure function, `PlayerShades.Assign(players)`, runs at the top of `RecapBuilder.Build` and returns the players with shaded `ColorHex`. Every view model is built from that list, so every tab, the cards and the exported image pick the shade up with no drawing changes.

- `events.log` keeps the character's true colour (`Tracker` writes it before any of this), so replaying an old log also gets the fix.
- `DevPreview` and `Replay` go through `RecapBuilder.Build` too, so they need nothing extra.

### How a shade is chosen

The owner's words: the shades must still read as the character. The playground showed a 32° hue shift turning Ironclad yellow or purple, and hue shifts in the warm colours drifting Regent into Ironclad. So brightness does the work, and hue only nudges.

Colours are handled in HSL. Distances are CIE76 ΔE in Lab (under 10 is hard to tell apart at small sizes; over 20 is clearly different).

1. **Readable first.** The base is the colour after the readability rule `RecapTheme.Accent` applies today (mix 15% toward white until luminance `0.2126r + 0.7152g + 0.0722b` ≥ 0.45, at most ten times). That rule moves into `Core` as `ColourMath.Readable`, and `Accent` calls it, so on-screen behaviour is unchanged for everyone who isn't shaded.
2. **Settled colours.** Every solo player's and every group's first player's readable colour is settled before any shade is chosen. Shades are then chosen in join order, each joining the settled list once chosen.
3. **Candidates.** Same saturation as the base. HSL lightness from the **floor** to the **ceiling** in steps of 0.01, and hue from 0° to 12° in steps of 3°, in one direction only: away from the nearest hue among the run's other colours (for Ironclad beside a Regent, toward crimson, away from orange). With no other colours in the run, toward higher hue.
   - The ceiling is lightness 0.78, so nothing washes to white.
   - The floor is where luminance falls to 0.45, so `Accent` never lightens a shade back toward its base. Each candidate is rounded to 8-bit channels and dropped if rounding took it under 0.45. The floor is why warm, mid-dark colours like Ironclad's red only ever go lighter: they're already on it.
4. **Pick the smallest change that's clearly different.** Cost is |Δlightness| × 100 + |Δhue| × 0.5. The cheapest candidate at least **ΔE 20** from every settled colour wins. If none reaches 20 (four or five of a dull colour), the candidate furthest from its nearest settled colour wins.
5. The result is written back as a six-digit lowercase hex.

Every step is deterministic: the same players give the same colours on every rebuild.

Measuring distance instead of taking fixed steps is what keeps dull colours honest: a prototype with fixed 0.10 lightness steps left three Tailors ΔE 10 apart, the search gets them to 12 and pairs of every colour tried to 20.

The numbers above (0.78, 12°, the cost weights, ΔE 20) are starting values. They're constants in one place, tuned in the dev preview; the tests below are the limits tuning must stay inside.

### What it guarantees (tested)

- Solo players and the first of each group are byte-for-byte unchanged.
- Every shade is readable: `Readable(shade) == shade`.
- Every shade stays in the family: hue within 12° of its base, lightness within [floor, 0.78]. A base already above 0.78 (a modded near-white) can only go darker.
- Measured on the test palette below, from each shade to every other colour in its run (two different characters' own colours are never changed, so they're not the rule's to separate):
  - pairs: at least ΔE 20;
  - three of a kind: at least ΔE 11;
  - four or five of a kind: all distinct and at least ΔE 5. Red, pink, brown, deep blue and near-white have little room, so this is tight; the names and gaps carry the rest.
- Deterministic.

The test palette stands in for the game's colours and a few awkward modded ones: `d85a30` (red), `e46fd6` (pink), `5ec2e0` (light blue), `3040ff` (deep blue), `ffa518` (orange), `8fd46a` (green), `7a4f2c` (dark brown), `f0f0f0` (near-white), and the mixed lobbies red + three orange, three red + orange, two red + two green. A prototype of the rule measured: pairs 20.0–22.6, threes 11.6–20.9, fours and fives 5.9–20.3.

A saturated deep blue only becomes readable near lightness 0.8, above the ceiling. Its range is then floor to floor + 0.12, so it still has somewhere to go.

## 2. Gaps in the climb

- Every segment above the bottom one in a stack gives up a gap at its bottom edge: 2 design px, or a third of the segment if that's smaller, so a player who did almost nothing in a fight doesn't vanish. The dark table shows through.
- Stack height, the outline round the stack, the hover highlight and the order (leader at the bottom) stay as they are.
- The gap size is `ChartMath.SegmentGap`, tested.
- The exported image uses the same climb, so it gets the gaps. It has no hover readout; its small legend already lists players left to right in the stacks' bottom-to-top order, and stays that way.

## 3. Names at the ends of the Timeline's lines

- With two or more players, each line ends in the player's name, in their colour, with a dark outline, just right of the last point. A solo run's chart stays as it is.
- The chart gives up 150 design px on the right for them. A name wider than that is cut with "…".
- When line ends are close, names push apart to keep at least 20 design px between them (18 px text), staying inside the plot. A name that moved gets a short tick from its line's end. The spreading is `EndLabels.Spread`, in `Core`, tested.
- Names follow the lines as they animate.
- The legend above the chart stays.

## 4. Scoreboard portraits

Defense shows portraits in a tall window, so the whole picture fits. The scoreboard card's window is wide (0.82 × 0.61 of the card's width, about 4:3), and `Kit.Cover` scales the picture to its width, cropping the top and bottom. That's the zoomed-in look.

- `Kit.Cover` gains `zoom`: 1 is today's cover, 0 is the whole picture; in between scales between the two. The card passes **0.8** as a starting value, tuned in the dev preview.
- Where the scaled picture is narrower than the window, the strips at either side are filled with the average colour of the picture's outermost two pixel columns on that side, read once per texture and cached. If the pixels can't be read, the strips use the card's colour darkened.
- If a portrait's edges aren't a flat colour (Necrobinder has dark shapes near them), the join may show. If it does, the fix is less zoom, not more machinery.
- The placement maths is `PictureFit`, in `Core`, tested. Defense and every other `Cover` caller pass no zoom and don't change.
- The exported image draws the same card, so it gets the zoom.

## 5. Checking it

`preview.flag` accepts a comma-separated list of character ids, for example `IRONCLAD,IRONCLAD,IRONCLAD,REGENT` or `NECROBINDER,NECROBINDER,NECROBINDER,NECROBINDER,NECROBINDER`, up to five. The sample run is built for those characters, and the usual screenshots, including `preview-9-export.png`, show the shades, gaps, names and portraits together. The existing forms (`1`–`4`, `m1`…, `steam`, and a language after a space) keep working.

Tuning is done here; the constants from section 1 and the zoom are the knobs.

## Deliberately not doing

- **Numbers or initials on the swatches.** The cards' gems already carry rank numbers, and a second numbering would clash with them.
- **Dashed or hatched marks.** They read poorly on thin lines and short stack segments.
- **A separate colour altogether** (the second Ironclad in blue). The colour should still say which character someone is.
- **Darker shades for warm, mid-dark colours.** They sit at the readability floor, so a darker shade would be lightened straight back to its base.
- **Per-character tables.** Everything is measured from the colours in the run.

## Files

| File | Change |
|---|---|
| `src/WhoCarried/Core/ColourMath.cs` | **New.** Hex, luminance, `Readable`, HSL, ΔE. |
| `src/WhoCarried/Core/PlayerShades.cs` | **New.** The rule in section 1. |
| `src/WhoCarried/Core/EndLabels.cs` | **New.** Spreading the Timeline's names. |
| `src/WhoCarried/Core/PictureFit.cs` | **New.** Where a zoomed picture and its side strips go. |
| `src/WhoCarried/Core/ChartMath.cs` | `SegmentGap`. |
| `src/WhoCarried/Core/RecapBuilder.cs` | Shade the players first. |
| `src/WhoCarried/UI/RecapTheme.cs` | `Accent` calls `ColourMath.Readable`. |
| `src/WhoCarried/UI/Climb.cs` | The gaps. |
| `src/WhoCarried/UI/TimelineTab.cs` | The names. |
| `src/WhoCarried/UI/Kit.cs` | `Cover(…, zoom, sides)`. |
| `src/WhoCarried/UI/GameArt.cs` | A portrait's edge colours, cached. |
| `src/WhoCarried/UI/CardFace.cs` | Passes the zoom and side colours. |
| `src/WhoCarried/UI/DevPreview.cs` | The character-list flag; a fifth sample name. |
| `tests/WhoCarried.Tests/` | `ColourMathTests`, `PlayerShadesTests`, `EndLabelsTests`, `PictureFitTests`; `ChartMathTests` grows. |
| `README.md`, `docs/README.md`, `CHANGELOG.md` | The new preview flag form; the spec and plan rows; the `[Unreleased]` entry. |
