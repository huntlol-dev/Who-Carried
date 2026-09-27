# Copy to clipboard

Date: 2026-09-27. Draft for review. Branch: `feat/copy-to-clipboard`. Plan: [copy to clipboard](../plans/2026-09-27-copy-to-clipboard.md).

## Intent

Players share runs by screenshotting the scoreboard. Few use **Export as image**, which puts a tall page in their Steam screenshots: 1200×2548 even for a solo run, so a chat preview shrinks it to a strip nobody can read, and getting it out of Steam takes several steps.

**Copy to clipboard** is a second button on the recap. One click puts a landscape picture of the run on the clipboard, ready to paste into Discord. The picture is the scoreboard people already screenshot, without the recap's controls, with what each player gave their teammates along the bottom.

It's done when a click, Alt+Tab and Ctrl+V in Discord give a picture whose result, ranking and damage numbers read in the chat preview. The rest reads once someone opens it.

Export as image stays as it is.

## How the layout was chosen

The owner picked from mockups drawn at chat-preview size:

- Three layouts: the scoreboard without the recap's controls (A), A with the awards instead of the climb (B), and leaderboard rows (C). The owner chose B, with **support instead of awards**: without it, a player who spends the run handing out energy and block looks like dead weight in the one picture that gets shared.
- **Cards created stays off.** It counts cards players made for themselves, so it isn't support, and its per-card table doesn't shrink to picture size.
- **Deaths** come with the scoreboard's card (the skull pill). **Gold earned** goes beside each name in Top sources, spelled out so it can't be read as a damage total.
- After a review: the support card for block is titled **Block given**; the picture grows taller rather than cutting anything off; the support hint line is dropped.
- The button says **Copy to clipboard**.

## The button

- **Copy to clipboard** (复制到剪贴板) is a stone slab on the tab row, left of **Export as image**. Copy gets the recap's bronze-rimmed stone. Export as image moves to a new plain stone (grey rim, same face), since Copy is now the main action.
- Clicking it:
  - The status line says "Copying..." while the picture renders, about half a second.
  - Then the button's word changes to "Copied", in the recap's green, for two seconds, and the status line says "Copied. Paste it anywhere with Ctrl+V" for four. On a Mac it says "Cmd+V".
  - There's no ✓ and no ⌘: the game's font may not have them, and the game has no tick icon (its resources only have the card library's tickbox).
  - The button keeps the width of its longer label, so Export as image doesn't move.
  - Clicks while a copy is under way are ignored.
- If a copy fails, the status line says "Couldn't copy the image. Try Export as image instead.", with the export button's own label filled in so it stays right in every language. `events.log` says why.
- **Controller:** unchanged. The pad's confirm button still exports. Copy has no pad binding and no glyph; it stays visible and can be clicked with a Steam Deck's trackpad. In Game Mode there's nowhere to paste anyway.
- No keyboard shortcut.

## The picture

`UI/ShareCard.cs` builds it the way `SummaryCard` builds the exported image: from parts the recap already has, so a change to the cards or the support row shows up everywhere.

### Size and background

- Laid out 1600 design pixels wide, as the recap is, and drawn with `Kit` at 1.2, so the PNG is 1920 wide, like a 1080p screenshot.
- At least 900 design pixels (1080 px) tall, and taller only when the bottom row needs it.
- The background is the recap's table (`Table.Backdrop`), fully opaque, so nothing shows through in a light or dark chat.

### Layout

In design pixels, as in the approved mockup:

| Part | Where | Built from |
|---|---|---|
| Top bar | 0–78, full width (the exported image's bar height) | The recap's bar without its controls: "Who Carried? · Victory" (or Defeat, or the act mid-run), floor, time, Ascension, team damage, the party's coins, and the date on the right, where the hotkey and Close are in the recap. It shares its code with the exported image's bar. |
| Hand | x 0–1200; the top from a new `HandLayout.TopBelow`: as high as it goes with every card and gem 16 below the bar | `ScoreboardTab.PlayerCard`, placed by `HandLayout.Layout`, neither dealt nor animated. The leader's foil is held still (`foilAt: 0.42f`, as on the export). Skull pills, plaques and badges come with the card. |
| Your run | Solo only, beside the card, as on the scoreboard | `ScoreboardTab.Story` |
| Note | Under the hand, y 608 | The scoreboard's note: bonus damage (parties only) and unattributed damage |
| Top sources | x 1222, y 96, 340 wide | `ScoreboardTab.TopSources`, with a new option that puts each player's gold earned beside their name (below). Rows per player from `ShareLayout.SourceRows`: 6 solo, 4 each for two players, 2 each for three or four, 1 each for five or more. |
| Bottom row | From y 640 (lower if Top sources reaches further), x 40–1560 | **Given to teammates** (below). When nobody gave anything (`HasSupport` is false, which includes every solo run), the climb instead: `Climb.Create(…, interactive: false, live: null)`. |
| Footer | The last 34 | The exported image's footer: seed, party size, the final fight once the run is over, and "Who Carried? · a Slay the Spire 2 mod" |

The page is a column at least 900 tall: the fixed top section (0–640), then the bottom row, then the footer at the bottom. Its height follows the bottom row, so nothing is ever cut off. Five players who all gave block make a block card five lines tall, and the picture about 956 tall.

### Gold earned

Beside each player's name in Top sources, on the right: the gold coin (`GameArt.Gold`, tinted gold) and "{0} gold earned", the Decks tab's own string, in gold. It's hidden when a player earned none. A long name is trimmed with "…" rather than running into it. The value comes from the player's `DeckView.Gold`.

### Given to teammates

The Support tab's cards (`SupportTab.Cards`) with the tab's text sizes, not the export's compact ones, headed "Given to teammates" with no hint line, and with two new options:

- **No award line.** The plaques already name the winners.
- **The block card is titled "Block given"** instead of "Block". On one picture, the card's "241 Block" chip (enemy block knocked off) and a support card titled "Block" would read as the same thing.

Kinds nobody gave are left out, as on the tab. The cards share the row in `ShareLayout.SupportColumns(kinds)` = max(kinds, 3) columns, so one or two kinds keep a third of the row each instead of stretching across the picture.

### Left off

The awards beyond the plaques, Cards created, debuffs, defense, decks, and the climb when there's support. Export as image has all of it.

### Mid-run

Copying works whenever the recap is open. The top bar shows the act, the support row the totals so far, and the footer leaves out the final fight.

## Getting it onto the clipboard

`UI/ImageClipboard.Copy(Image image, Action<string?> onDone)` reports null once the picture is on the clipboard, or the reason it isn't. It never throws.

Godot can read an image from the clipboard (`DisplayServer.ClipboardGetImage`) but has no way to write one, so each system gets its own path.

### Windows, and Proton (Steam Deck, Linux)

The mod runs in the Windows build under Proton, so both take this path.

1. The picture becomes a PNG (`Image.SavePngToBuffer`, from an RGB8 copy: no alpha channel, a smaller file) and a device-independent bitmap. `Core/Dib.cs` builds the bitmap: a 40-byte `BITMAPINFOHEADER`, 32 bits per pixel, `BI_RGB`, rows bottom-up, bytes in BGRA order with alpha 255.
2. `OpenClipboard`, owned by the game's window (`DisplayServer.WindowGetNativeHandle(HandleType.WindowHandle)`). If another program is holding the clipboard, it's retried up to 10 times, 10 ms apart.
3. `EmptyClipboard`, then `SetClipboardData` twice: first the registered format `"PNG"`, which Chromium-based apps (Discord, Slack, Chrome) read first; then `CF_DIB`, for Paint, Word and older apps. Each goes into `GlobalAlloc(GMEM_MOVEABLE)` memory. Windows owns it once `SetClipboardData` succeeds; if that fails, the mod frees it.
4. `CloseClipboard`, always.

Windows keeps the picture after the game closes. Wine passes `"PNG"` to the Linux desktop as `image/png` (and the bitmap as `image/bmp`). There, the picture may only last while the game runs, so Deck players should paste before quitting. Both are to be confirmed on a Deck.

This runs on the main thread, once the status line already says "Copying...".

### Mac

1. The PNG is written to a temporary file (`Path.GetTempPath()`, a random name).
2. On a background task, `/usr/bin/osascript -e 'set the clipboard to (read (POSIX file "<path>") as «class PNGf»)'` runs, with up to 5 seconds to finish before it's killed.
3. The file is deleted, and `onDone` runs back on the main thread (`Callable.From(…).CallDeferred()`).

The worst this path can do is fail with an exit code or a timeout; it doesn't call macOS's system libraries directly, so it can't crash the game.

### Anywhere else

The native Linux build can't load mods. `Copy` reports that copying images isn't supported there.

### Log

- A copy: `copied summary to clipboard, 1920×1080, 1.1 MB`.
- A failure: `copy failed: <reason>`, where the reason is the render's error, "clipboard busy", a Windows error code, or osascript's exit code and error output.

## Wiring

- `HewnStoneArt.Palette` gains `Plain`: a grey rim over the same face. `HewnStone.Slab` takes a palette, bronze by default, and keeps a drawn texture per palette.
- `RecapPanel` places the Copy slab after the tabs and Export as image after it. `PanelHandle` gains `Copy`, and a way to show the Copied look, beside `Save`.
- `RecapUi.CopyToClipboard(view, icons, handle)`, beside `Export`: ignore the click if a copy is under way, say "Copying...", render with `PngExporter.Render(ShareCard.Create(view, icons), 1920, …)`, hand the image to `ImageClipboard.Copy`, then update the button and the status line. Like `Export`, it checks the panel still exists before touching it.

## Localization

Six new keys in `eng.json` and `zhs.json`, in key order:

| Key | English | 简体中文 |
|---|---|---|
| `WHO_CARRIED.action.copied` | Copied | 已复制 |
| `WHO_CARRIED.action.copy_image` | Copy to clipboard | 复制到剪贴板 |
| `WHO_CARRIED.copy.copying` | Copying... | 正在复制… |
| `WHO_CARRIED.copy.done` | Copied. Paste it anywhere with {0} | 已复制，按 {0} 即可粘贴 |
| `WHO_CARRIED.copy.failed` | Couldn't copy the image. Try {0} instead. | 图片复制失败，请改用“{0}” |
| `WHO_CARRIED.support.block_given` | Block given | 给予格挡 |

- `copy.done`'s `{0}` is "Ctrl+V", or "Cmd+V" on a Mac. Key names aren't translated.
- `copy.failed`'s `{0}` is the Export as image button's own label (`WHO_CARRIED.action.save_image`).
- Gold earned reuses `WHO_CARRIED.decks.gold`.
- The card chip's 格挡 and the support card's 给予格挡 differ in Chinese too.

## Developer preview

- Every preview run also saves the picture as `preview-share.png`, after the export, and logs its size.
- A new flag, `copy` (optionally with a language: `copy zhs`), opens the recap with the sample and presses the real Copy to clipboard button. 2.5 seconds later it logs which formats the clipboard holds and the size `DisplayServer.ClipboardGetImage` reads back, saves `preview-copy.png` (the Copied button and the status line), and closes the recap. It overwrites the clipboard, which is why it's a flag of its own.
- The README's developer checks list the new flag.

## Testing

**Core unit tests** (the console runner):

- `Dib.FromRgba`: the header fields (size 40, width, a positive height, 1 plane, 32 bits, `BI_RGB`, the image size); RGBA turned into BGRA with alpha 255; rows flipped bottom-up (a 3×2 image); a 1×1 image; a buffer of the wrong length rejected.
- `ShareLayout.SourceRows`: 6, 4, 2, 2, 1 for 1 to 5 players. `ShareLayout.SupportColumns`: 3, 3, 3, 4, 5 for 1 to 5 kinds.
- `HandLayout.TopBelow`: for 1 to 5 players, the highest card or gem sits exactly the gap below the line.
- `LocalizationTests` already fail if a key is missing its Chinese or goes unused.

**In the game, on Windows** (preview flags):

- `preview-share.png` with 1, 2, 4 and 5 players (`IRONCLAD,SILENT,REGENT,NECROBINDER,DEFECT`), in English and Chinese:
  - it matches the approved mockup;
  - it's 1920×1080 with four players, and with five, the logged height fits everything, with nothing cut off;
  - the climb shows in solo;
  - gold and names stay clear of each other;
  - no text overruns a card.
- The exported image (`preview-10-export.png`) is unchanged from main, pixel for pixel, since its bar and footer code is now shared. Compare screenshots taken on the same day, as the bar shows the date.
- The recap shows both slabs on the tab row, in English and Chinese, with room to spare. In controller mode, Export as image's glyph still fits.
- `copy`: the log lists the PNG and bitmap formats, the size read back equals the rendered size, and `preview-copy.png` shows "Copied" and the status line.
- **By hand, the owner:** after a real run, paste into Discord (the chat preview and the full-size view) and into Paint. A double-click copies once.

**Pending, needs other players:**

- A Steam Deck in Desktop Mode, or Linux through Proton: paste into Discord or a browser. Does the picture survive quitting the game?
- A Mac: paste into Discord. If it fails, the `copy failed` line in `events.log`.

## Changelog and README

- `CHANGELOG.md`, `[Unreleased]` → Added: the button, what the picture shows, and which systems it has been tried on.
- `README.md`: Copy to clipboard beside Export as image, and the `copy` preview flag.
- The Workshop change note is written at release: short and friendly.

## Out of scope

- A keyboard shortcut, and a controller button for Copy.
- The native Linux build.
- Changing the scoreboard card itself.
- Saving the picture to disk or to Steam: Export as image does that.
