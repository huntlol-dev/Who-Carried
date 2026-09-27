# Copy to clipboard — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A **Copy to clipboard** button on the recap that puts a landscape picture of the run on the clipboard, ready to paste into Discord.

**Architecture:** `ShareCard` builds the picture from the recap's own parts: the scoreboard's cards and Top sources (now with gold earned), the Support tab's cards, and the exported image's bar and footer. It lays the picture out in 1600 design pixels and draws it at 1.2×. `PngExporter.Render` turns it into an `Image`, and `ImageClipboard` puts that on the clipboard: through the Win32 clipboard (PNG and DIB) on Windows and under Proton, and through `osascript` on a Mac. The pure rules live in Core with unit tests: `Dib` (the bitmap's bytes), `ShareLayout` (sizes, rows, columns) and `HandLayout.TopBelow` (how high the hand sits).

**Tech Stack:** C# / .NET 9, the game's GodotSharp API, Win32 through `DllImport` (user32, kernel32), `/usr/bin/osascript` on macOS, and the repo's console test runner.

**Spec:** [Copy to clipboard](../specs/2026-09-27-copy-to-clipboard-design.md).

## Global constraints

- Work on `feat/copy-to-clipboard`, which already exists (made from `main`; its commits so far are the spec and this plan). Commit after each task as its steps say. Don't push.
- Build and test with `C:\Program Files\dotnet\dotnet.exe`; the x86 dotnet first on PATH can't see the x64 .NET 9 SDK. In bash:
  - build: `"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release`
  - all tests: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
  - some tests: add `-- <part of the test name>`, e.g. `-- Dib`
- `src/WhoCarried/Core` stays free of Godot and game types: the test project compiles it directly.
- Every new on-screen English string gets its `zhs.json` entry in the same commit as the code that uses it, in key order in both catalogs. Keys appear in code as whole string literals, like `"WHO_CARRIED.copy.done"`. `LocalizationTests` finds used keys by scanning the source, and fails when a key is used but missing, is never used, or lacks its Chinese.
- The exported image (`SummaryCard`) renders exactly as before. Task 6 compares it with a baseline taken before any UI change (Task 3 Step 1).
- The scoreboard card itself doesn't change.
- Nothing throws into the game: the clipboard, render and preview code catch errors and report them.
- The picture is 1600 design pixels wide at scale 1.2 (1920 px), and at least 900 design pixels (1080 px) tall.
- Copy: the label is "Copy to clipboard" (复制到剪贴板). After a copy, the button reads "Copied" in `RecapTheme.Green` for 2 s, and the status line reads "Copied. Paste it anywhere with {0}" for 4 s, where {0} is "Ctrl+V", or "Cmd+V" on a Mac. No ✓ or ⌘ glyphs.
- Screenshots and logs from the checks go in `runs/2026-09-27-copy-to-clipboard/`, which is git-ignored because they show player names.
- Commit messages end with a `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` paragraph, as the commit steps show.

## Running a preview in the game

Tasks 3 to 6 check the picture in the real game with the dev preview. Each run:

1. The game must be closed: `tasklist //FI "IMAGENAME eq SlayTheSpire2.exe"` lists nothing. (`tools/deploy.ps1` refuses otherwise.)
2. Deploy: `powershell -File tools/deploy.ps1` → `Deployed to E:\Games\steamapps\common\Slay the Spire 2\mods\WhoCarried`.
3. Write the flag as plain ASCII: `printf '%s' '<case>' > "$APPDATA/SlayTheSpire2/WhoCarried/preview.flag"`.
4. Launch through Steam, since the game won't start without it: `"/e/Steam/steam.exe" -applaunch 2868840`.
5. Wait for a `preview done` line in `events.log` in `%APPDATA%\SlayTheSpire2\WhoCarried\`. It usually takes 30–60 s, and the game starts a fresh `events.log` at each launch. Foreground `sleep` is blocked in this harness, so wait with the Monitor tool and an until-loop, e.g. `until grep -q "preview done" "$APPDATA/SlayTheSpire2/WhoCarried/events.log"; do sleep 3; done`.
6. Close the game, which the preview doesn't do: `taskkill //IM SlayTheSpire2.exe //F`.
7. Check that `%APPDATA%\SlayTheSpire2\logs\godot.log` loaded this build: its `Loading assembly DLL` line should name `mods\WhoCarried\WhoCarried.dll`, not the Workshop copy (`workshop\content\2868840\3802205096`). If it names the Workshop copy, stop and ask the owner to switch copies in the game's mod settings.
8. Copy `preview-*.png` and `events.log` from the data folder into `runs/2026-09-27-copy-to-clipboard/<case>/`, because filenames are reused between runs.
9. After the last run, delete `preview.flag`, or the preview runs at every launch.

A flag with a language (`4 zhs`) switches the game's language, and it stays switched. Task 6's last run uses `copy eng`, which switches it back. With a language in the flag, `preview-1-scoreboard.png` can catch the cards mid-deal, so judge the scoreboard from `preview-13-pad-card.png`.

## Review focus

1. **Chinese text makes Top sources taller**, because its line boxes are taller than Kreon's. The bottom row must start below the column, not overlap it. `ShareCard` measures the column (`Fit`), and Task 6 Step 3 checks the 4-player Chinese picture.
2. **Five players, or a kind of help five players gave.** The picture must grow with nothing cut off, and each player gets one top source. Task 6 Step 4 checks the 5-player picture and its logged height.
3. **A long Steam name beside gold earned.** The name trims with "…" and the gold stays whole. The `copy` flag's sample gives its first player a 29-character name, and Task 4 Step 5 and Task 6 Step 6 check `preview-copy-share.png`.
4. **The recap closed while a copy is under way,** with Esc right after the click, or a window resize rebuilding it. There must be no error, and the next copy must still work. The `copy` check closes the recap straight after one click, and Task 5 Step 10 checks the log.
5. **A double-click** makes one copy, not two. The `copy` check clicks twice in a row, and Task 5 Step 10 counts the log's copy lines.

## Files

| File | Change |
|---|---|
| `src/WhoCarried/Core/Dib.cs` | New: RGBA pixels → a Windows DIB (`CF_DIB`) |
| `src/WhoCarried/Core/ShareLayout.cs` | New: the picture's sizes, top-source rows, support columns |
| `src/WhoCarried/Core/HandLayout.cs` | `TopBelow`: the hand as high as it goes below a line |
| `src/WhoCarried/Core/HewnStoneArt.cs` | `Palette.Plain`: the grey-rimmed stone |
| `src/WhoCarried/UI/ShareCard.cs` | New: the picture |
| `src/WhoCarried/UI/ImageClipboard.cs` | New: the picture onto the clipboard (Windows/Proton, Mac) |
| `src/WhoCarried/UI/SummaryCard.cs` | The bar takes a width and the party; `Note` and `Footer` are shared |
| `src/WhoCarried/UI/ScoreboardTab.cs` | `TopSources` can show gold earned beside each name |
| `src/WhoCarried/UI/SupportTab.cs` | `Cards` can leave out the award line and say "Block given" |
| `src/WhoCarried/UI/HewnStone.cs` | A stone per palette; `Lift` can follow a moving rest |
| `src/WhoCarried/UI/RecapPanel.cs` | The Copy slab, Export as image on the plain stone, `PanelHandle.Copy` and `ShowCopied` |
| `src/WhoCarried/UI/RecapUi.cs` | `CopyToClipboard`; status messages shared with `Export` |
| `src/WhoCarried/UI/DevPreview.cs` | `preview-share.png`; the `copy` flag |
| `src/WhoCarried/Localization/eng.json`, `zhs.json` | Six keys |
| `tests/WhoCarried.Tests/DibTests.cs`, `ShareLayoutTests.cs` | New |
| `tests/WhoCarried.Tests/HandLayoutTests.cs`, `HewnStoneArtTests.cs` | One test each |
| `README.md`, `CHANGELOG.md` | Document it |

---

### Task 1: The bitmap Windows gets (`Dib`)

**Files:**
- Create: `src/WhoCarried/Core/Dib.cs`
- Test: `tests/WhoCarried.Tests/DibTests.cs`

**Interfaces:**
- Produces: `public static class Dib` in `WhoCarried.Core`, with:
  - `public const int HeaderSize = 40;`
  - `public static byte[] FromRgba(int width, int height, ReadOnlySpan<byte> rgba)`, which throws `ArgumentException` for a side that isn't positive, or a buffer that isn't width × height × 4 bytes.

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/DibTests.cs`:

```csharp
using System.Buffers.Binary;
using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>The bitmap Windows gets on the clipboard: its header, its colour order and its row order.</summary>
public static class DibTests
{
    private static int Int(byte[] bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));

    private static int Short(byte[] bytes, int at) => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at));

    [Test]
    public static void TheHeaderDescribesA32BitBottomUpBitmap()
    {
        byte[] dib = Dib.FromRgba(3, 2, new byte[3 * 2 * 4]);
        Check.Equal(40, Int(dib, 0), "header size");
        Check.Equal(3, Int(dib, 4), "width");
        Check.Equal(2, Int(dib, 8), "height, positive: the bottom row comes first");
        Check.Equal(1, Short(dib, 12), "planes");
        Check.Equal(32, Short(dib, 14), "bits per pixel");
        Check.Equal(0, Int(dib, 16), "BI_RGB");
        Check.Equal(24, Int(dib, 20), "image size");
        for (int at = 24; at < 40; at += 4) Check.Equal(0, Int(dib, at), $"the field at {at}");
        Check.Equal(40 + 24, dib.Length, "the header, then the pixels");
    }

    [Test]
    public static void PixelsTurnBlueGreenRedAndOpaque()
    {
        byte[] dib = Dib.FromRgba(1, 1, new byte[] { 10, 20, 30, 40 });
        Check.Equal("30,20,10,255", string.Join(",", dib.Skip(40)), "blue, green, red, alpha 255");
    }

    [Test]
    public static void RowsAreStoredBottomUp()
    {
        // 3×2: the top row red, the bottom row blue.
        byte[] rgba = new byte[3 * 2 * 4];
        for (int x = 0; x < 3; x++)
        {
            rgba[x * 4] = 255;
            rgba[12 + x * 4 + 2] = 255;
        }
        byte[] dib = Dib.FromRgba(3, 2, rgba);
        Check.Equal("255,0,0,255", string.Join(",", dib.Skip(40).Take(4)), "the first stored row is the bottom one: blue");
        Check.Equal("0,0,255,255", string.Join(",", dib.Skip(40 + 12).Take(4)), "the last stored row is the top one: red");
    }

    [Test]
    public static void AWrongSizeIsRejected()
    {
        foreach ((int w, int h, int bytes) in new[] { (3, 2, 23), (3, 2, 25), (0, 2, 0), (3, -1, 0) })
        {
            bool threw = false;
            try { Dib.FromRgba(w, h, new byte[bytes]); }
            catch (ArgumentException) { threw = true; }
            Check.True(threw, $"{w}×{h} with {bytes} bytes");
        }
    }
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- Dib`
Expected: the build fails with `error CS0103: The name 'Dib' does not exist in the current context`.

- [ ] **Step 3: Write `Dib`**

Create `src/WhoCarried/Core/Dib.cs`:

```csharp
using System.Buffers.Binary;

namespace WhoCarried.Core;

/// <summary>
/// A device-independent bitmap, Windows' <c>CF_DIB</c> clipboard format, from RGBA pixels: a 40-byte
/// BITMAPINFOHEADER, then 32-bit pixels in blue, green, red, alpha order, bottom row first. Alpha is always 255: the
/// picture is opaque, and apps disagree about what a DIB's fourth byte means.
/// </summary>
public static class Dib
{
    /// <summary>The BITMAPINFOHEADER's size, which is also where the first pixel starts.</summary>
    public const int HeaderSize = 40;

    /// <param name="rgba">Four bytes a pixel, top row first, as Godot's <c>Image.GetData()</c> gives an RGBA8 image.</param>
    /// <exception cref="ArgumentException">A side isn't positive, or the buffer isn't width × height × 4 bytes.</exception>
    public static byte[] FromRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException($"no bitmap is {width}×{height}");
        long size = (long)width * height * 4;
        if (rgba.Length != size) throw new ArgumentException($"{width}×{height} needs {size} bytes, not {rgba.Length}");

        var dib = new byte[HeaderSize + size];
        Span<byte> header = dib.AsSpan(0, HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header, HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], height); // positive: stored bottom-up
        BinaryPrimitives.WriteInt16LittleEndian(header[12..], 1); // planes
        BinaryPrimitives.WriteInt16LittleEndian(header[14..], 32); // bits per pixel
        // 16: compression 0, BI_RGB. 24 to 39: resolution and palette, all 0.
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], (int)size);

        int stride = width * 4;
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> from = rgba.Slice(y * stride, stride);
            Span<byte> to = dib.AsSpan(HeaderSize + (height - 1 - y) * stride, stride);
            for (int x = 0; x < stride; x += 4)
            {
                to[x] = from[x + 2];
                to[x + 1] = from[x + 1];
                to[x + 2] = from[x];
                to[x + 3] = 255;
            }
        }
        return dib;
    }
}
```

- [ ] **Step 4: Run the tests and see them pass**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- Dib`
Expected: four `PASS DibTests.…` lines, then `4/4 passed`.

- [ ] **Step 5: Run every test**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: the last line reads `N/N passed`, with no `FAIL` lines.

- [ ] **Step 6: Commit**

```bash
git add src/WhoCarried/Core/Dib.cs tests/WhoCarried.Tests/DibTests.cs
git commit -m "feat: build the Windows clipboard bitmap from RGBA pixels" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The picture's layout rules (`ShareLayout`, `HandLayout.TopBelow`)

**Files:**
- Create: `src/WhoCarried/Core/ShareLayout.cs`
- Modify: `src/WhoCarried/Core/HandLayout.cs` (add `TopBelow` after `TopClearOfTabs`)
- Test: `tests/WhoCarried.Tests/ShareLayoutTests.cs` (new), `tests/WhoCarried.Tests/HandLayoutTests.cs`

**Interfaces:**
- Produces, in `WhoCarried.Core`:
  - `public static class ShareLayout`, with these constants (all `float`):
    - `Width` = 1600, `MinHeight` = 900, `Scale` = 1.2f
    - `BarHeight` = 78, `HandGap` = 16
    - `SourcesTop` = 96, `NoteTop` = 608, `BottomTop` = 640
    - `Side` = 40, `Gap` = 12
  - `ShareLayout` also has `static int PixelWidth` (1920), `static int SourceRows(int players)` and `static int SupportColumns(int kinds)`.
  - `public static float HandLayout.TopBelow(float line, int n, float handWidth)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/ShareLayoutTests.cs`:

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>The copied picture's size, and the rows and columns its parts get.</summary>
public static class ShareLayoutTests
{
    [Test]
    public static void ThePictureIsA1080pScreenshotAtItsShortest()
    {
        Check.Equal(1920, ShareLayout.PixelWidth, "width in pixels");
        Check.Near(1080, ShareLayout.MinHeight * ShareLayout.Scale, "height in pixels at its shortest", tolerance: 0.01);
    }

    [Test]
    public static void TopSourcesGetTheScoreboardsRowsAndOneEachFromFivePlayers()
    {
        Check.Equal("6,4,2,2,1,1", string.Join(",", Enumerable.Range(1, 6).Select(ShareLayout.SourceRows)), "rows for 1 to 6 players");
    }

    [Test]
    public static void OneOrTwoKindsOfSupportDontStretchAcrossThePicture()
    {
        Check.Equal("3,3,3,4,5", string.Join(",", Enumerable.Range(1, 5).Select(ShareLayout.SupportColumns)), "columns for 1 to 5 kinds");
    }
}
```

In `tests/WhoCarried.Tests/HandLayoutTests.cs`, add inside the class, after `OneAndTwoPlayerHandsBarelyMove`:

```csharp
    [Test]
    public static void TheCopiedPicturesHandSitsJustBelowItsBar()
    {
        const float line = ShareLayout.BarHeight + ShareLayout.HandGap;
        for (int n = 1; n <= 5; n++)
        {
            float top = HandLayout.TopBelow(line, n, 1200);
            Check.Near(line, Highest(n, top, hovered: false), $"{n} players: the highest card or gem is on the line", tolerance: 0.01);
            Check.True(top < HandLayout.TopClearOfTabs(n, 1200), $"{n} players: higher than in the recap, which has tabs to clear");
        }
    }
```

- [ ] **Step 2: Run them and see them fail**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- Share`
Expected: the build fails with `error CS0103: The name 'ShareLayout' does not exist in the current context`, and `error CS0117: 'HandLayout' does not contain a definition for 'TopBelow'`.

- [ ] **Step 3: Write `ShareLayout`**

Create `src/WhoCarried/Core/ShareLayout.cs`:

```csharp
namespace WhoCarried.Core;

/// <summary>
/// The copied picture's layout in the recap's design pixels: its size and scale, where its parts start, and how many
/// rows and columns they get. UI/ShareCard draws to it.
/// </summary>
public static class ShareLayout
{
    /// <summary>The design width, the recap's own.</summary>
    public const float Width = 1600;

    /// <summary>The shortest the picture gets (16:9); a taller bottom row makes it longer.</summary>
    public const float MinHeight = 900;

    /// <summary>Design pixels to picture pixels: 1600 becomes 1920, like a 1080p screenshot.</summary>
    public const float Scale = 1.2f;

    /// <summary>The picture's width in pixels.</summary>
    public static int PixelWidth => (int)MathF.Round(Width * Scale);

    /// <summary>The top bar's height (the exported image's bar), and the room the hand keeps below it.</summary>
    public const float BarHeight = 78, HandGap = 16;

    /// <summary>Where Top sources starts, and where the note under the hand sits.</summary>
    public const float SourcesTop = 96, NoteTop = 608;

    /// <summary>Where the bottom row (support, or the climb) starts, unless Top sources reaches lower.</summary>
    public const float BottomTop = 640;

    /// <summary>The margin either side of the full-width parts, and the gap round the footer and below Top sources.</summary>
    public const float Side = 40, Gap = 12;

    /// <summary>
    /// Top sources per player: as on the scoreboard (6 solo, 4 each for two players, 2 each for three or four), and 1
    /// each from five, so the column still ends above the bottom row.
    /// </summary>
    public static int SourceRows(int players) => players switch
    {
        <= 1 => 6,
        2 => 4,
        <= 4 => 2,
        _ => 1,
    };

    /// <summary>Columns for the support cards: one per kind given, but at least three, so one or two kinds keep a card's width.</summary>
    public static int SupportColumns(int kinds) => Math.Max(3, kinds);
}
```

- [ ] **Step 4: Write `HandLayout.TopBelow`**

In `src/WhoCarried/Core/HandLayout.cs`, add after the `TopClearOfTabs` method:

```csharp
    /// <summary>
    /// The top for a hand of <paramref name="n"/> cards at rest that sits as high as it can with every card and gem at or
    /// below <paramref name="line"/>: the copied picture's hand, which has no tabs to clear and never hovers.
    /// </summary>
    public static float TopBelow(float line, int n, float handWidth)
    {
        (float w, Slot[] slots) = Layout(n, handWidth, PreferredTop);
        float highest = slots.Min(s => Reach(w, s, hovered: false));
        return PreferredTop + (line - highest);
    }
```

- [ ] **Step 5: Run the tests and see them pass**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- Share`
Expected: `PASS ShareLayoutTests.…` three times, and `PASS HandLayoutTests.TheCopiedPicturesHandSitsJustBelowItsBar`: `4/4 passed`.

- [ ] **Step 6: Run every test**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: no `FAIL` lines.

- [ ] **Step 7: Commit**

```bash
git add src/WhoCarried/Core/ShareLayout.cs src/WhoCarried/Core/HandLayout.cs tests/WhoCarried.Tests/ShareLayoutTests.cs tests/WhoCarried.Tests/HandLayoutTests.cs
git commit -m "feat: layout rules for the copied picture" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The picture (`ShareCard`)

**Files:**
- Create: `src/WhoCarried/UI/ShareCard.cs`
- Modify: `src/WhoCarried/UI/SummaryCard.cs`, `src/WhoCarried/UI/ScoreboardTab.cs`, `src/WhoCarried/UI/SupportTab.cs`, `src/WhoCarried/UI/DevPreview.cs`
- Modify: `src/WhoCarried/Localization/eng.json`, `src/WhoCarried/Localization/zhs.json`

**Interfaces:**
- Consumes: `ShareLayout` and `HandLayout.TopBelow` (Task 2).
- Produces:
  - `internal static class ShareCard`, with `static int PixelWidth` (1920) and `static Control Create(RecapView view, Func<string?, Texture2D?> icons, DateTime? date = null)`.
  - `SummaryCard.TopBar(Kit k, RecapView view, DateTime date, float width = Width, bool party = false)` (now `internal`); `internal static string SummaryCard.Note(RecapView view)`; `internal static Control SummaryCard.Footer(Kit k, RecapView view)`.
  - `ScoreboardTab.TopSources(Kit k, RecapView view, int rows, Live? live, bool gold = false)`.
  - `SupportTab.Cards(Kit k, RecapView view, float width, int columns, Live? live, bool compact = false, bool showAwards = true, bool givenTitles = false)`.
  - The key `WHO_CARRIED.support.block_given`.
  - `DevPreview.SaveShare(string dataDir, string file, RecapView view, Func<string?, Texture2D?> icons, Action then)` (private), used again in Tasks 4 and 5.

- [ ] **Step 1: Take the baseline export, before any UI changes**

The branch's UI still matches `main`, since Tasks 1 and 2 only added Core code. Follow "Running a preview in the game" with the flag `4`, then copy the data folder's `preview-10-export.png` to `runs/2026-09-27-copy-to-clipboard/before/preview-10-export.png`.

The export's bar shows today's date, so Task 6 has to compare against a baseline taken the same day. If Task 6 runs on a later day, retake this baseline from `main` first:
1. `git worktree add --detach "$TEMP/who-carried-main" main`
2. Copy `local.props` in.
3. Run that worktree's `tools/deploy.ps1` and the `4` preview.
4. Redeploy this branch.

- [ ] **Step 2: Share the exported image's bar, note and footer**

In `src/WhoCarried/UI/SummaryCard.cs`, replace the `TopBar` method with:

```csharp
    /// <summary>The game's top bar: "Who Carried? · Victory", floor, time, ascension, team damage, and the date.</summary>
    /// <param name="width">The bar's design width: the exported image's, or the copied picture's.</param>
    /// <param name="party">The party's coins after the numbers, as the recap's own bar has them (the copied picture).</param>
    internal static Control TopBar(Kit k, RecapView view, DateTime date, float width = Width, bool party = false)
    {
        Control bar = k.Box(width, 78);
        if (GameArt.Get(GameArt.TopBar) is Texture2D art) bar.AddChild(k.Stretch(art, width, 78));
        else bar.AddChild(k.Swatch(new Color("1b2635"), width, 78, 0));
        HBoxContainer row = k.Row(24);
        bar.AddChild(k.At(row, 30, 0, width - 60, 72));
        HBoxContainer title = k.Row(0);
        title.AddChild(Kit.Center(k.Strong(RecapTexts.ModName + " · ", 32)));
        (string result, Color tone) = RecapTexts.Result(view);
        title.AddChild(Kit.Center(k.Strong(result, 32, tone)));
        row.AddChild(Kit.Center(title));
        void Stat(Texture2D? icon, string text)
        {
            if (text.Length == 0) return;
            HBoxContainer stat = k.Row(6);
            if (icon != null) stat.AddChild(Kit.Center(k.Pic(icon, 30, 30)));
            stat.AddChild(Kit.Center(k.Text(text, 22, RecapTheme.Text, true, Ink.Soft)));
            row.AddChild(Kit.Center(stat));
        }
        int floor = RecapTexts.Floor(view);
        Stat(GameArt.Get(GameArt.Floor), floor > 0 ? floor.ToString(CultureInfo.InvariantCulture) : "");
        Stat(GameArt.Get(GameArt.Timer), RecapTexts.Duration(view.Facts?.Seconds ?? 0));
        Stat(GameArt.Get(GameArt.Ascension), (view.Facts?.Ascension ?? 0) > 0 ? view.Facts!.Ascension.ToString(CultureInfo.InvariantCulture) : "");
        Stat(GameArt.Get(GameArt.Swords), Kit.Num(RecapTexts.TeamDamage(view)));
        if (party)
        {
            HBoxContainer coins = k.Row(-8);
            foreach (BarRow p in ScoreboardTab.Players(view))
                coins.AddChild(Kit.Center(RecapPanel.Coin(k, p.IconKey, RecapTheme.Accent(p.ColorHex), 38)));
            row.AddChild(Kit.Center(coins));
        }
        row.AddChild(Kit.Fill());
        row.AddChild(Kit.Center(k.Text(Loc.Text("WHO_CARRIED.summary.date", date), 16, RecapTheme.Muted)));
        return bar;
    }
```

In the same file:
- change `private static string Note(RecapView view)` to `internal static string Note(RecapView view)`;
- change `private static Control Footer(Kit k, RecapView view)` to `internal static Control Footer(Kit k, RecapView view)`.

The export's own call, `TopBar(k, view, date ?? DateTime.Now)`, stays as it is: its defaults draw the same bar as before.

- [ ] **Step 3: Gold earned beside each name in Top sources**

In `src/WhoCarried/UI/ScoreboardTab.cs`, replace the whole `TopSources` method with:

```csharp
    /// <summary>Each player's top few damage sources with their art, on one shared scale.</summary>
    /// <param name="gold">Each player's gold earned beside their name, as the copied picture shows it.</param>
    public static Control TopSources(Kit k, RecapView view, int rows, Live? live, bool gold = false)
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

        (Control, Action<(SourcesView Source, int Max, int Gold)>) Group((SourcesView Source, int Max, int Gold) item)
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
            HBoxContainer who = k.Who(RecapTexts.Name(item.Source.PlayerLabel), item.Source.IconKey, color);
            HBoxContainer coin = k.Row(5);
            Label earned = k.Text("", 15, RecapTheme.Gold, true, Ink.Soft);
            if (gold)
            {
                // The name gives way to the gold: it trims with "…" before the two can touch.
                var name = (Label)who.GetChild(who.GetChildCount() - 1);
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                name.CustomMinimumSize = k.V(24, 0);
                // The game's coin is a white text glyph, tinted like the words beside it, as in the Decks tab.
                coin.AddChild(Kit.Center(k.Pic(GameArt.Get(GameArt.Gold), 18, 18, RecapTheme.Gold)));
                coin.AddChild(Kit.Center(earned));
                who.AddChild(Kit.Center(coin));
            }
            group.AddChild(who);
            Label none = k.Text(Loc.Text("WHO_CARRIED.empty.damage"), 14, RecapTheme.Muted);
            group.AddChild(none);
            panel.AddChild(group);
            var lines = new KeyedRows<(BarRow Row, int Max)>(group, l => RecapTexts.SourceKey(l.Row), l => Line(l, color), offset: 2);
            void Apply((SourcesView Source, int Max, int Gold) it)
            {
                List<BarRow> top = it.Source.Rows.Where(r => !RecapTexts.IsOther(r)).Take(rows).ToList();
                none.Visible = top.Count == 0;
                lines.Sync(top.Select(r => (r, it.Max)));
                earned.Text = Loc.Text("WHO_CARRIED.decks.gold", Kit.Num(it.Gold));
                coin.Visible = gold && it.Gold > 0;
            }
            Apply(item);
            return (panel, Apply);
        }

        var groups = new KeyedRows<(SourcesView Source, int Max, int Gold)>(box, g => g.Source.PlayerLabel, Group, offset: 1);
        void Sync(RecapView v)
        {
            int max = v.Sources.SelectMany(s => s.Rows).Where(r => !RecapTexts.IsOther(r)).Select(r => r.Value).DefaultIfEmpty(1).Max();
            // Sources and Decks label a player the same way, "name · character".
            groups.Sync(v.Sources.Where(s => s.PlayerLabel != RecapBuilder.UnattributedLabel)
                .Select(s => (s, max, v.Decks.FirstOrDefault(d => d.PlayerLabel == s.PlayerLabel)?.Gold ?? 0)));
        }
        Sync(view);
        live?.On(Sync);
        return box;
    }
```

The scoreboard's own call, `TopSources(k, view, n == 1 ? 6 : n == 2 ? 4 : 2, live)`, leaves `gold` off, so the scoreboard doesn't change.

- [ ] **Step 4: Support cards without the award line, with "Block given"**

In `src/WhoCarried/UI/SupportTab.cs`, replace the `Kind` record and the `Kinds` array with:

```csharp
    /// <summary>
    /// One kind of help: its name, colour, value, and the award its leader can win; and the name it goes by on the
    /// copied picture, when that differs.
    /// </summary>
    private sealed record Kind(string Words, Color Tone, Func<SupportRow, int> Value, string Award, string? GivenWords = null);

    private static readonly Kind[] Kinds =
    {
        new("WHO_CARRIED.support.energy", RecapTheme.Gold, r => r.Energy, AwardBuilder.Battery),
        new("WHO_CARRIED.support.cards", RecapTheme.Text, r => r.Cards, AwardBuilder.CarePackage),
        // On the copied picture the scoreboard cards' own Block chip (enemy block knocked off) sits just above.
        new("WHO_CARRIED.support.block", RecapTheme.Blocked, r => r.Block, AwardBuilder.Bodyguard, "WHO_CARRIED.support.block_given"),
        new("WHO_CARRIED.support.buffs", RecapTheme.Taken, r => r.Buffs, AwardBuilder.Coach),
        new("WHO_CARRIED.support.draws", RecapTheme.Teal, r => r.Draws, AwardBuilder.Playmaker),
    };
```

Replace the `Cards` doc comment and signature:

```csharp
    /// <summary>
    /// The cards in a grid, in the order of <see cref="Kinds"/>. Compact (the saved image) drops the award line and uses
    /// one row of cards across. The copied picture keeps the tab's sizes, but drops the award line
    /// (<paramref name="showAwards"/>) and names a kind as given where that reads better (<paramref name="givenTitles"/>).
    /// </summary>
    public static Control Cards(Kit k, RecapView view, float width, int columns, Live? live, bool compact = false,
                                bool showAwards = true, bool givenTitles = false)
```

In `Cards`' `Apply`, change

```csharp
                (Control card, Action<RecapView> update) = Card(k, v, kind, cardW, compact);
```

to

```csharp
                (Control card, Action<RecapView> update) = Card(k, v, kind, cardW, compact, showAwards, givenTitles);
```

Change `Card`'s signature to

```csharp
    private static (Control, Action<RecapView>) Card(Kit k, RecapView view, Kind kind, float width, bool compact, bool showAwards,
                                                     bool givenTitles)
```

and in `Card`, change

```csharp
        Label name = k.Text(Loc.Text(kind.Words), compact ? 14 : 22, kind.Tone, true, Ink.Soft);
```

to

```csharp
        Label name = k.Text(Loc.Text(givenTitles && kind.GivenWords != null ? kind.GivenWords : kind.Words), compact ? 14 : 22,
            kind.Tone, true, Ink.Soft);
```

and

```csharp
        if (!compact) column.AddChild(awardLine);
```

to

```csharp
        if (!compact && showAwards) column.AddChild(awardLine);
```

- [ ] **Step 5: The new key**

In `src/WhoCarried/Localization/eng.json`, after the `"WHO_CARRIED.support.block": "Block",` line, add:

```json
  "WHO_CARRIED.support.block_given": "Block given",
```

In `src/WhoCarried/Localization/zhs.json`, after the `"WHO_CARRIED.support.block": "格挡",` line, add:

```json
  "WHO_CARRIED.support.block_given": "给予格挡",
```

- [ ] **Step 6: Write `ShareCard`**

Create `src/WhoCarried/UI/ShareCard.cs`:

```csharp
using Godot;
using WhoCarried.Core;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>
/// The picture Copy to clipboard puts on the clipboard, for pasting into Discord: the scoreboard as players already
/// screenshot it, without the recap's controls. The bar with the date, the hand and its note, Top sources with each
/// player's gold earned, then what players gave their teammates (the climb when nobody gave anything), and the footer.
/// Laid out in the recap's design pixels (<see cref="ShareLayout"/>) and drawn at its scale; taller when the bottom row
/// needs it, never cut off.
/// </summary>
internal static class ShareCard
{
    public static int PixelWidth => ShareLayout.PixelWidth;

    /// <param name="date">The run's date (defaults to today, for a run just played).</param>
    public static Control Create(RecapView view, Func<string?, Texture2D?> icons, DateTime? date = null)
    {
        var k = new Kit(ShareLayout.Scale, icons);
        var page = new PanelContainer { CustomMinimumSize = k.V(ShareLayout.Width, ShareLayout.MinHeight), MouseFilter = Control.MouseFilterEnum.Ignore };
        // Solid all over: a see-through corner would show the chat's own background.
        page.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = RecapTheme.TableDark });
        page.AddChild(Table.Backdrop(k));

        VBoxContainer column = k.Column(0);
        page.AddChild(column);
        column.AddChild(Top(k, view, date ?? DateTime.Now));
        Control bottom = view.HasSupport
            ? Support(k, view)
            : Climb.Create(k, view, ShareLayout.Width - 2 * ShareLayout.Side, 204, 92, interactive: false, live: null);
        column.AddChild(Sides(k, bottom));
        // Takes up whatever the rest leaves of the picture's height, so the footer sits at its foot.
        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        MarginContainer footer = Sides(k, SummaryCard.Footer(k, view));
        footer.AddThemeConstantOverride("margin_top", k.F(ShareLayout.Gap));
        footer.AddThemeConstantOverride("margin_bottom", k.F(ShareLayout.Gap));
        column.AddChild(footer);
        return page;
    }

    /// <summary>The bar, the hand and its note, and Top sources: the scoreboard's parts, placed as the recap places them.</summary>
    private static Control Top(Kit k, RecapView view, DateTime date)
    {
        Control top = k.Box(ShareLayout.Width, ShareLayout.BottomTop);
        top.AddChild(SummaryCard.TopBar(k, view, date, ShareLayout.Width, party: true));

        List<BarRow> players = ScoreboardTab.Players(view);
        int n = players.Count;
        if (n > 0)
        {
            // The scoreboard's hand, held still, as high as it goes below the bar: there are no tabs to clear.
            float handTop = HandLayout.TopBelow(ShareLayout.BarHeight + ShareLayout.HandGap, n, 1200);
            (float w, HandLayout.Slot[] slots) = HandLayout.Layout(n, 1200, handTop);
            for (int i = 0; i < n; i++)
            {
                var card = new ScoreboardTab.PlayerCard(k, view, players[i], w, n, foilAt: 0.42f);
                card.Update(view, players[i], i, n);
                card.Place(slots[i], i + 1, animate: false);
                top.AddChild(card.Face.Root);
            }
            // A lone player's card has their run beside it, as on the scoreboard.
            if (n == 1) top.AddChild(k.At(ScoreboardTab.Story(k, view, null), 590, slots[0].Y + 17, 520, -1));
        }

        string note = SummaryCard.Note(view);
        if (note.Length > 0)
        {
            Label line = k.Text(note, 14, RecapTheme.Muted);
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            top.AddChild(k.At(line, ShareLayout.Side, ShareLayout.NoteTop, 1120, -1));
        }

        Control sources = ScoreboardTab.TopSources(k, view, ShareLayout.SourceRows(n), live: null, gold: true);
        top.AddChild(k.At(sources, 1222, ShareLayout.SourcesTop, 340, -1));
        // Top sources can reach lower than planned (Chinese lines are taller): the bottom row then starts below it.
        void Fit() => top.CustomMinimumSize = new Vector2(top.CustomMinimumSize.X,
            Math.Max(k.U(ShareLayout.BottomTop), sources.Position.Y + sources.GetCombinedMinimumSize().Y + k.U(ShareLayout.Gap)));
        sources.MinimumSizeChanged += Fit;
        Fit();
        return top;
    }

    /// <summary>
    /// What players gave their teammates: the Support tab's cards under its heading, without the tab's hint or award
    /// lines, and with block titled "Block given", since the cards above have their own Block chip.
    /// </summary>
    private static Control Support(Kit k, RecapView view)
    {
        VBoxContainer section = k.Column(14);
        section.AddChild(k.Heading(Loc.Text("WHO_CARRIED.support.heading"), SupportTab.HeadingArt(k, view), null, 26));
        section.AddChild(SupportTab.Cards(k, view, ShareLayout.Width - 2 * ShareLayout.Side,
            ShareLayout.SupportColumns(SupportTab.KindsGiven(view)), live: null, showAwards: false, givenTitles: true));
        return section;
    }

    /// <summary>The picture's side margins round a full-width part.</summary>
    private static MarginContainer Sides(Kit k, Control content)
    {
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", k.F(ShareLayout.Side));
        margin.AddThemeConstantOverride("margin_right", k.F(ShareLayout.Side));
        margin.AddChild(content);
        return margin;
    }
}
```

- [ ] **Step 7: The preview saves the picture**

In `src/WhoCarried/UI/DevPreview.cs`, in `CaptureLive`, replace

```csharp
                PngExporter.Save(SummaryCard.Create(later, sample.Icons), SummaryCard.Width,
                    Path.Combine(dataDir, $"preview-{TabNames.Length + 2}-export.png"), error =>
                    {
                        if (error != null) Tracker.Note($"preview export failed: {error}");
                        RecapUi.Hide();
                        CaptureTopBar(dataDir, () => CapturePad(dataDir, sample, () => Tracker.Note("preview done")));
                    });
```

with

```csharp
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
```

and add this method to the class, after `CheckSteam`:

```csharp
    /// <summary>Renders the Copy to clipboard picture into the data folder and logs its size, then carries on.</summary>
    private static void SaveShare(string dataDir, string file, RecapView view, Func<string?, Texture2D?> icons, Action then)
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
```

- [ ] **Step 8: Build and run every test**

Run: `"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release`
Expected: `Build succeeded`, with 0 errors.

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: no `FAIL` lines. `LocalizationTests.EveryKeyTheCodeUsesExistsAndNoneAreLeftOver` passes, because `block_given` is both used and in both catalogs.

- [ ] **Step 9: Look at the picture in the game**

Follow "Running a preview in the game" with the flag `4`. `events.log` should read `preview share: preview-share.png 1920×1080 saved`. Open `preview-share.png` and compare it with the approved mockup (see the spec's Layout table):
- the bar reads "Who Carried? · Victory" and shows floor, time, Ascension, team damage, four party coins, and today's date on the right;
- the four cards sit just under the bar, with the gems clear of it, and skull pills on Mika's card (2) and Jo's card (1);
- in Top sources, each name has its coin and "… gold earned" on the right, and the column ends above "Given to teammates";
- the note sits under the hand, clear of every card's badges;
- the support cards have no award line and no hint line, and the block card reads "Block given";
- the footer reads "Seed … · 4 players · …" on the left and "Who Carried? · a Slay the Spire 2 mod" on the right, at the foot.

Fix anything that doesn't match before committing.

- [ ] **Step 10: Commit**

```bash
git add src/WhoCarried/UI/ShareCard.cs src/WhoCarried/UI/SummaryCard.cs src/WhoCarried/UI/ScoreboardTab.cs src/WhoCarried/UI/SupportTab.cs src/WhoCarried/UI/DevPreview.cs src/WhoCarried/Localization/eng.json src/WhoCarried/Localization/zhs.json
git commit -m "feat: the picture Copy to clipboard copies" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Onto the clipboard (`ImageClipboard`)

**Files:**
- Create: `src/WhoCarried/UI/ImageClipboard.cs`
- Modify: `src/WhoCarried/UI/DevPreview.cs`

**Interfaces:**
- Consumes: `Dib.FromRgba` (Task 1); `ShareCard.Create` and `PixelWidth`, and `DevPreview.SaveShare` (Task 3).
- Produces:
  - `internal static class ImageClipboard`, with:
    - `static void Copy(Image image, Action<string?, int> onDone)`. It calls `onDone` once, on the main thread, with null or the reason it failed, and the PNG's size in bytes (0 if it never made one).
    - `static string Describe()`, the clipboard's formats, for the preview.
  - The `copy` preview flag, whose `CheckCopy` Task 5 rewrites.

- [ ] **Step 1: Write `ImageClipboard`**

Create `src/WhoCarried/UI/ImageClipboard.cs`:

```csharp
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Godot;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// Puts a picture on the system clipboard, for pasting into Discord and the like. Godot can read an image from the
/// clipboard but can't write one, so each system gets its own path: the Windows clipboard, which Proton passes on to
/// the Linux desktop, or osascript on a Mac. Reports back exactly once; never throws.
/// </summary>
internal static class ImageClipboard
{
    /// <param name="onDone">
    /// Called on the main thread with null once the picture is on the clipboard, or the reason it isn't; and the PNG's
    /// size in bytes (0 if it was never made).
    /// </param>
    public static void Copy(Image image, Action<string?, int> onDone)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            onDone("copying images isn't supported on this system", 0);
            return;
        }
        byte[] png;
        try
        {
            // No alpha channel: the picture is opaque, and the file comes out smaller.
            using var rgb = (Image)image.Duplicate();
            if (rgb.GetFormat() != Image.Format.Rgb8) rgb.Convert(Image.Format.Rgb8);
            png = rgb.SavePngToBuffer();
        }
        catch (Exception e)
        {
            Tracker.LogError("copy: encoding the picture", e);
            onDone(e.Message, 0);
            return;
        }
        if (OperatingSystem.IsWindows()) onDone(Win32.Copy(image, png), png.Length);
        else Mac.Copy(png, onDone);
    }

    /// <summary>The clipboard's formats right now ("PNG, DIB, 17, 2"), for the dev preview's check.</summary>
    public static string Describe() => OperatingSystem.IsWindows() ? Win32.Describe() : "(only listed on Windows)";

    /// <summary>
    /// The Windows clipboard: the PNG, which Chromium apps (Discord, Slack, Chrome) read first, and a DIB for everything
    /// else. Under Proton, Wine hands both to the Linux desktop (image/png, image/bmp).
    /// </summary>
    private static class Win32
    {
        private const uint CfDib = 8, GmemMoveable = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "RegisterClipboardFormatW")]
        private static extern uint RegisterClipboardFormat(string name);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint EnumClipboardFormats(uint format);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClipboardFormatNameW")]
        private static extern int GetClipboardFormatName(uint format, StringBuilder name, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr memory);

        public static string? Copy(Image image, byte[] png)
        {
            byte[] dib;
            try
            {
                using var rgba = (Image)image.Duplicate();
                if (rgba.GetFormat() != Image.Format.Rgba8) rgba.Convert(Image.Format.Rgba8);
                dib = Dib.FromRgba(rgba.GetWidth(), rgba.GetHeight(), rgba.GetData());
            }
            catch (Exception e)
            {
                Tracker.LogError("copy: building the bitmap", e);
                return e.Message;
            }
            try
            {
                // The clipboard belongs to a window: SetClipboardData can fail without one.
                var owner = new IntPtr(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle));
                if (owner == IntPtr.Zero) return "no game window to own the clipboard";
                if (!Open(owner)) return "clipboard busy";
                try
                {
                    if (!EmptyClipboard()) return $"EmptyClipboard failed ({Marshal.GetLastWin32Error()})";
                    uint pngFormat = RegisterClipboardFormat("PNG");
                    if (pngFormat == 0) return $"RegisterClipboardFormat failed ({Marshal.GetLastWin32Error()})";
                    return Put(pngFormat, png) ?? Put(CfDib, dib);
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch (Exception e)
            {
                Tracker.LogError("copy: the Windows clipboard", e);
                return e.Message;
            }
        }

        public static string Describe()
        {
            try
            {
                if (!Open(IntPtr.Zero)) return "clipboard busy";
                try
                {
                    var formats = new List<string>();
                    for (uint format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
                    {
                        var name = new StringBuilder(128);
                        formats.Add(GetClipboardFormatName(format, name, name.Capacity) > 0 ? name.ToString()
                            : format == CfDib ? "DIB" : format.ToString(CultureInfo.InvariantCulture));
                    }
                    return formats.Count == 0 ? "empty" : string.Join(", ", formats);
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        /// <summary>Another program may hold the clipboard for a moment: try ten times, 10 ms apart.</summary>
        private static bool Open(IntPtr owner)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (OpenClipboard(owner)) return true;
                System.Threading.Thread.Sleep(10);
            }
            return false;
        }

        /// <summary>One format's bytes onto the clipboard. Windows owns the memory once it takes it; until then it's ours to free.</summary>
        private static string? Put(uint format, byte[] bytes)
        {
            IntPtr memory = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
            if (memory == IntPtr.Zero) return $"GlobalAlloc failed ({Marshal.GetLastWin32Error()})";
            IntPtr target = GlobalLock(memory);
            if (target == IntPtr.Zero)
            {
                int lockError = Marshal.GetLastWin32Error();
                GlobalFree(memory);
                return $"GlobalLock failed ({lockError})";
            }
            Marshal.Copy(bytes, 0, target, bytes.Length);
            GlobalUnlock(memory);
            if (SetClipboardData(format, memory) != IntPtr.Zero) return null;
            int error = Marshal.GetLastWin32Error();
            GlobalFree(memory);
            return $"SetClipboardData failed ({error})";
        }
    }

    /// <summary>
    /// A Mac: osascript reads the PNG from a temporary file onto the pasteboard, off the main thread. It never calls the
    /// system's libraries by hand, so the worst it can do is fail with an exit code or a timeout.
    /// </summary>
    private static class Mac
    {
        private const int TimeoutMs = 5000;

        public static void Copy(byte[] png, Action<string?, int> onDone)
        {
            string path = Path.Combine(Path.GetTempPath(), $"who-carried-{Guid.NewGuid():N}.png");
            try
            {
                File.WriteAllBytes(path, png);
            }
            catch (Exception e)
            {
                Tracker.LogError("copy: writing the temporary picture", e);
                onDone(e.Message, png.Length);
                return;
            }
            // The answer goes back to the main thread: the caller touches the recap.
            Task.Run(() => Run(path)).ContinueWith(done =>
            {
                string? result = done.IsFaulted ? done.Exception?.GetBaseException().Message ?? "osascript failed" : done.Result;
                Callable.From(() => onDone(result, png.Length)).CallDeferred();
            });
        }

        /// <summary>Runs osascript, then deletes the file. Null on success, or what went wrong. Never throws.</summary>
        private static string? Run(string path)
        {
            try
            {
                // An AppleScript string: escape backslashes and quotes (a temporary path has neither, but be sure).
                string file = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
                var start = new ProcessStartInfo("/usr/bin/osascript")
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                start.ArgumentList.Add("-e");
                start.ArgumentList.Add($"set the clipboard to (read (POSIX file \"{file}\") as «class PNGf»)");
                using Process? process = Process.Start(start);
                if (process == null) return "osascript didn't start";
                Task<string> errors = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(TimeoutMs))
                {
                    process.Kill(entireProcessTree: true);
                    return "osascript timed out";
                }
                return process.ExitCode == 0 ? null : $"osascript exit {process.ExitCode}: {errors.Result.Trim()}";
            }
            catch (Exception e)
            {
                return e.Message;
            }
            finally
            {
                try { File.Delete(path); }
                catch (Exception) { /* a leftover temporary file is harmless */ }
            }
        }
    }
}
```

- [ ] **Step 2: A `copy` flag for the preview**

In `src/WhoCarried/UI/DevPreview.cs`:

Add these fields to the class, after `TabNames`:

```csharp
    /// <summary>A Steam name near the 32-character limit, for the copy check: it must trim beside the gold.</summary>
    private const string LongName = "Ashenvale_the_Unyielding_1987";
```

Change `BuildSample`'s signature and its first line from

```csharp
    private static Sample BuildSample(CharacterModel[] characters)
    {
        string[] names = { "Ash", "Mika", "Sam", "Jo", "Wren" };
```

to

```csharp
    private static Sample BuildSample(CharacterModel[] characters, string? firstName = null)
    {
        string[] names = { firstName ?? "Ash", "Mika", "Sam", "Jo", "Wren" };
```

In `Run`, change

```csharp
        Sample sample = BuildSample(characters);
```

to

```csharp
        // The copy check's first player has a long name, to see it trimmed beside their gold.
        Sample sample = BuildSample(characters, wanted == "copy" ? LongName : null);
```

and, after the block that ends with `CheckSteam(dataDir, sample); return; }`, add:

```csharp
        if (wanted == "copy")
        {
            CheckCopy(dataDir, sample);
            return;
        }
```

Add these methods after `SaveShare`:

```csharp
    /// <summary>
    /// "copy" in the flag: the Copy to clipboard picture for a sample whose first player has a long name, saved as
    /// preview-copy-share.png (and a mid-run one as preview-share-midrun.png), then put on the clipboard and read back.
    /// The log names the clipboard's formats and the size Godot reads back. Overwrites the clipboard.
    /// </summary>
    private static void CheckCopy(string dataDir, Sample sample)
    {
        SaveShare(dataDir, "preview-share-midrun.png", sample.View with { Victory = null }, sample.Icons, () =>
            PngExporter.Render(ShareCard.Create(sample.View, sample.Icons), ShareCard.PixelWidth, (image, error) =>
            {
                if (image == null)
                {
                    Tracker.Note($"preview copy: render failed: {error}");
                    Tracker.Note("preview done");
                    return;
                }
                PngExporter.SavePng(image, Path.Combine(dataDir, "preview-copy-share.png"));
                ImageClipboard.Copy(image, (copyError, bytes) =>
                {
                    Tracker.Note($"preview copy: {copyError ?? "copied"}, {image.GetWidth()}×{image.GetHeight()}, {bytes} bytes");
                    image.Dispose();
                    ReadBack();
                    Tracker.Note("preview done");
                });
            }));
    }

    /// <summary>What's on the clipboard now: its formats (on Windows), and the size of the picture Godot reads from it.</summary>
    private static void ReadBack()
    {
        Tracker.Note($"preview copy: formats {ImageClipboard.Describe()}");
        using Image? back = DisplayServer.ClipboardHasImage() ? DisplayServer.ClipboardGetImage() : null;
        Tracker.Note($"preview copy: read back {(back == null ? "nothing" : $"{back.GetWidth()}×{back.GetHeight()}")}");
    }
```

- [ ] **Step 3: Build and run every test**

Run: `"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release`
Expected: `Build succeeded`, with 0 errors. Platform-compatibility warnings (CA1416) for the Win32 calls are fine: they only run behind `OperatingSystem.IsWindows()`.

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: no `FAIL` lines.

- [ ] **Step 4: Copy in the game**

Follow "Running a preview in the game" with the flag `copy eng`. `events.log` should have:
- `preview share: preview-share-midrun.png 1920×1080 saved`
- `preview copy: copied, 1920×1080, <n> bytes`, with n somewhere around 0.5–2.5 MB
- `preview copy: formats PNG, DIB, …`: PNG first and DIB second. Windows may add the formats it makes from the DIB, such as 17 and 2.
- `preview copy: read back 1920×1080`
- no `ERROR` lines

- [ ] **Step 5: Check the clipboard from outside the game, and the pictures**

With the game closed, which also shows the picture outlives it on Windows, run in PowerShell (Windows PowerShell 5.1):

```powershell
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Clipboard]::GetDataObject().GetFormats() -join ', '
$image = Get-Clipboard -Format Image
"$($image.Width)x$($image.Height)"
```

Expected: a list that includes `PNG` and `DeviceIndependentBitmap`, then `1920x1080`.

Open `preview-copy-share.png`. The first player's name, "Ashenvale_the_Unyielding_1987", should end in "…" in Top sources, with their coin and "… gold earned" whole beside it. On their card, the banner fits the name as the scoreboard does.

Open `preview-share-midrun.png`. The bar should read "Who Carried? · Act 3", or the sample's act, in grey, and the footer should have no final fight after "4 players".

- [ ] **Step 6: Commit**

```bash
git add src/WhoCarried/UI/ImageClipboard.cs src/WhoCarried/UI/DevPreview.cs
git commit -m "feat: put the picture on the clipboard (Windows and Proton, Mac)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: The button

**Files:**
- Modify: `src/WhoCarried/Core/HewnStoneArt.cs`
- Test: `tests/WhoCarried.Tests/HewnStoneArtTests.cs`
- Modify: `src/WhoCarried/UI/HewnStone.cs`, `src/WhoCarried/UI/RecapPanel.cs`, `src/WhoCarried/UI/RecapUi.cs`, `src/WhoCarried/UI/DevPreview.cs`
- Modify: `src/WhoCarried/Localization/eng.json`, `src/WhoCarried/Localization/zhs.json`

**Interfaces:**
- Consumes: `ShareCard` (Task 3); `ImageClipboard.Copy`, and `DevPreview.SaveShare` and `ReadBack` (Tasks 3 and 4).
- Produces:
  - `HewnStoneArt.Palette.Plain`.
  - `HewnStone.Slab(Kit k, string text, float height, HewnStoneArt.Palette? palette = null)` and `HewnStone.Lift(Button button, Kit k, Func<Vector2>? rest = null)`.
  - `PanelHandle(…, Action Close, Action Save, Action Copy, Action<bool> ShowCopied)`.
  - `RecapPanel.Create(…, Action<PanelHandle> onSave, Action<PanelHandle> onCopy)`.
  - Five keys: `WHO_CARRIED.action.copied`, `.action.copy_image`, `.copy.copying`, `.copy.done`, `.copy.failed`.

- [ ] **Step 1: Write the failing test for the plain stone**

In `tests/WhoCarried.Tests/HewnStoneArtTests.cs`, add inside the class:

```csharp
    [Test]
    public static void ThePlainStoneHasTheSameShapeAndFaceWithAGreyRim()
    {
        HewnStoneArt.Result bronze = Tile();
        HewnStoneArt.Result? drawn = HewnStoneArt.Draw(HewnStoneArt.Palette.Plain);
        Check.True(drawn != null, "the plain tile draws");
        HewnStoneArt.Result plain = drawn!.Value;
        int middle = plain.Width / 2;
        Check.Equal(At(bronze, middle, middle), At(plain, middle, middle), "the same face");
        Check.Equal(HewnStoneArt.Rgba.Clear, At(plain, 0, 0), "the same cut corner");
        Check.Equal(HewnStoneArt.Palette.Plain.Rim, At(plain, middle, 1), "the plain rim");
        Check.True(At(bronze, middle, 1) != At(plain, middle, 1), "not the bronze");
    }
```

- [ ] **Step 2: Run it and see it fail**

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- HewnStone`
Expected: the build fails with `error CS0117: 'HewnStoneArt.Palette' does not contain a definition for 'Plain'`.

- [ ] **Step 3: The plain palette**

In `src/WhoCarried/Core/HewnStoneArt.cs`, inside `Palette`, after `Default`, add:

```csharp
        /// <summary>Iron: the same face with a grey rim, for the stone beside the main action (Export as image).</summary>
        public static readonly Palette Plain = new(
            Rim: new Rgba(0x6F, 0x7C, 0x8C, 0xFF),
            RimLit: new Rgba(0xA9, 0xB4, 0xC2, 0xFF),
            RimShade: new Rgba(0x3A, 0x43, 0x4F, 0xFF),
            Face: Default.Face,
            FaceLit: Default.FaceLit,
            FaceShade: Default.FaceShade);
```

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests -- HewnStone`
Expected: every `HewnStoneArtTests` test passes.

- [ ] **Step 4: A stone per palette, and a lift that follows a moving stone**

In `src/WhoCarried/UI/HewnStone.cs`, replace everything from `private static ImageTexture? _texture;` down to the end of the `Box` method with:

```csharp
    private static readonly Dictionary<HewnStoneArt.Palette, ImageTexture?> Stones = new();

    /// <summary>The stone in a palette, drawn once. Null if it couldn't be built — callers fall back to a plain box.</summary>
    private static ImageTexture? Texture(HewnStoneArt.Palette palette)
    {
        // The game disposes textures it unloads; a dead handle has to be redrawn like GameArt re-looks-up its own.
        if (Stones.TryGetValue(palette, out ImageTexture? stone) && (stone == null || GodotObject.IsInstanceValid(stone))) return stone;
        try
        {
            stone = HewnStoneArt.Draw(palette) is HewnStoneArt.Result tile
                ? ImageTexture.CreateFromImage(Image.CreateFromData(tile.Width, tile.Height, false, Image.Format.Rgba8, tile.Pixels))
                : null;
        }
        catch (Exception e)
        {
            stone = null;
            Tracker.LogError("drawing the button stone (a plain box instead)", e);
        }
        Stones[palette] = stone;
        return stone;
    }

    /// <summary>
    /// The stone as a style box. TextureMargin is in texture pixels and must not be scaled — that is the whole point
    /// of a nine-slice, and dropping it is what made the old buttons stretch. ContentMargin is on screen, so it is.
    /// </summary>
    private static StyleBox Box(Kit k, Color tint, HewnStoneArt.Palette palette)
    {
        if (Texture(palette) is Texture2D stone)
            return new StyleBoxTexture
            {
                Texture = stone,
                ModulateColor = tint,
                TextureMarginLeft = HewnStoneArt.Margin,
                TextureMarginRight = HewnStoneArt.Margin,
                TextureMarginTop = HewnStoneArt.Margin,
                TextureMarginBottom = HewnStoneArt.Margin,
                ContentMarginLeft = k.U(18),
                ContentMarginRight = k.U(18),
                ContentMarginTop = k.U(6),
                ContentMarginBottom = k.U(8),
            };
        Color edge = palette == HewnStoneArt.Palette.Default ? RecapTheme.Gold : RecapTheme.Muted;
        return RecapTheme.Box(RecapTheme.Table, k.U(4), edge, k.U(2), k.U(18), k.U(7));
    }
```

Replace `Slab`'s doc comment and signature, and its four stone style boxes. The doc comment and signature become:

```csharp
    /// <summary>
    /// The panel's real buttons: a stone that lights on hover and sinks when pressed. Bronze for the main action (Copy
    /// to clipboard), <see cref="HewnStoneArt.Palette.Plain"/> for the one beside it.
    /// </summary>
    public static Button Slab(Kit k, string text, float height, HewnStoneArt.Palette? palette = null)
    {
        HewnStoneArt.Palette stone = palette ?? HewnStoneArt.Palette.Default;
```

The rest of `Slab` stays as it is, except the four `normal`/`hover`/`pressed`/`hover_pressed` lines:

```csharp
        button.AddThemeStyleboxOverride("normal", Box(k, new Color(1, 1, 1), stone));
        button.AddThemeStyleboxOverride("hover", Box(k, new Color(1.12f, 1.12f, 1.12f), stone));
        button.AddThemeStyleboxOverride("pressed", Box(k, new Color(0.78f, 0.78f, 0.78f), stone));
        button.AddThemeStyleboxOverride("hover_pressed", Box(k, new Color(0.9f, 0.9f, 0.9f), stone));
```

In `Shadow`, change

```csharp
        shadow.AddThemeStyleboxOverride("panel", Box(k, new Color(0.08f, 0.1f, 0.13f, 0.75f)));
```

to

```csharp
        shadow.AddThemeStyleboxOverride("panel", Box(k, new Color(0.08f, 0.1f, 0.13f, 0.75f), HewnStoneArt.Palette.Default));
```

Replace `Lift` with:

```csharp
    /// <summary>
    /// Hover lifts the stone off its shadow, press drops it into it. The shadow stays where it is — that is what makes
    /// the movement read as the stone moving rather than the whole control sliding. <paramref name="rest"/> is where the
    /// stone lies, for a stone that's moved after this (one that follows its neighbour); by default, where it is now.
    /// </summary>
    public static void Lift(Button button, Kit k, Func<Vector2>? rest = null)
    {
        Vector2 now = button.Position;
        rest ??= () => now;
        bool down = false;
        void Place(float y) => button.Position = rest() + k.V(0, y);
        button.MouseEntered += () => Place(down ? 2 : -2);
        button.MouseExited += () => { down = false; Place(0); };
        button.ButtonDown += () => { down = true; Place(2); };
        button.ButtonUp += () => { down = false; Place(-2); };
    }
```

- [ ] **Step 5: The Copy slab on the panel**

In `src/WhoCarried/UI/RecapPanel.cs`, replace the `PanelHandle` doc comment and record with:

```csharp
/// <summary>
/// What the caller needs to drive an open recap: switch views, show a status message, push live updates, and what the
/// controller and mouse can do (each view's rows, close, save, copy), and the Copied look on the copy button.
/// </summary>
internal sealed record PanelHandle(Control Root, TabContainer Tabs, Label Status, HotkeyLine Hotkey, Live Live, IReadOnlyList<PadTab> Pads,
                                   Action Close, Action Save, Action Copy, Action<bool> ShowCopied);
```

Change `Create`'s parameters from

```csharp
                                     Action onClose, Action<PanelHandle> onSave)
```

to

```csharp
                                     Action onClose, Action<PanelHandle> onSave, Action<PanelHandle> onCopy)
```

Replace

```csharp
        Button save = HewnStone.Slab(k, Loc.Text("WHO_CARRIED.action.save_image"), HewnStone.SlabHeight);
```

with

```csharp
        // Copy to clipboard is the main action, on the bronze stone; Export as image sits beside it on the plain one.
        Button copy = HewnStone.Slab(k, Loc.Text("WHO_CARRIED.action.copy_image"), HewnStone.SlabHeight);
        HoldWidth(copy, Loc.Text("WHO_CARRIED.action.copied"));
        Color copyIdle = copy.GetThemeColor("font_color"), copyHover = copy.GetThemeColor("font_hover_color");
        Button save = HewnStone.Slab(k, Loc.Text("WHO_CARRIED.action.save_image"), HewnStone.SlabHeight, HewnStoneArt.Palette.Plain);
```

Replace

```csharp
        void Save() => onSave(handle!);
```

with

```csharp
        void Save() => onSave(handle!);
        void Copy() => onCopy(handle!);
        // "Copied", in green, while the status line says how to paste; then the label again.
        void ShowCopied(bool copied)
        {
            copy.Text = Loc.Text(copied ? "WHO_CARRIED.action.copied" : "WHO_CARRIED.action.copy_image");
            copy.AddThemeColorOverride("font_color", copied ? RecapTheme.Green : copyIdle);
            copy.AddThemeColorOverride("font_hover_color", copied ? RecapTheme.Green : copyHover);
        }
```

Replace

```csharp
        handle = new PanelHandle(root, tabs, status, hotkeyLine, live, pads, onClose, Save);
```

with

```csharp
        handle = new PanelHandle(root, tabs, status, hotkeyLine, live, pads, onClose, Save, Copy, ShowCopied);
```

Replace

```csharp
        save.Pressed += Save;
```

with

```csharp
        copy.Pressed += Copy;
        save.Pressed += Save;
```

Replace

```csharp
        stage.AddChild(Nav(k, tabs, hints, save));
```

with

```csharp
        stage.AddChild(Nav(k, tabs, hints, copy, save));
```

Change `Nav`'s signature from

```csharp
    private static Control Nav(Kit k, TabContainer tabs, PadHints hints, Button save)
```

to

```csharp
    private static Control Nav(Kit k, TabContainer tabs, PadHints hints, Button copy, Button save)
```

In `Nav`, replace

```csharp
        // Leave space after the measured, translated tabs (including the controller hint).
        save.Position = k.V(x + 20, HandLayout.TabLine - HewnStone.SlabHeight - HewnStone.ShadowDrop - top);
        nav.AddChild(HewnStone.Shadow(k, save));
        nav.AddChild(save);
        HewnStone.Lift(save, k);
```

with

```csharp
        // Leave space after the measured, translated tabs (including the controller hint): Copy to clipboard, then
        // Export as image.
        float slabY = HandLayout.TabLine - HewnStone.SlabHeight - HewnStone.ShadowDrop - top;
        copy.Position = k.V(x + 20, slabY);
        nav.AddChild(HewnStone.Shadow(k, copy));
        nav.AddChild(copy);
        HewnStone.Lift(copy, k);
        // Export as image follows Copy, and moves along if Copy's word comes out wider once it's drawn: a substitute
        // font (Chinese) can widen a word after the first measure, as it did Close's.
        Panel saveShadow = HewnStone.Shadow(k, save);
        Vector2 saveRest = Vector2.Zero;
        void PlaceSave()
        {
            float copyWidth = Math.Max(copy.Size.X, copy.GetCombinedMinimumSize().X);
            saveRest = new Vector2(copy.Position.X + copyWidth + k.U(16), k.U(slabY));
            save.Position = saveRest;
            saveShadow.Position = saveRest + k.V(0, HewnStone.ShadowDrop);
        }
        PlaceSave();
        copy.MinimumSizeChanged += PlaceSave;
        copy.Resized += PlaceSave;
        nav.AddChild(saveShadow);
        nav.AddChild(save);
        HewnStone.Lift(save, k, () => saveRest);
```

Add this method to `RecapPanel`, after `Coin`:

```csharp
    /// <summary>Keeps a button wide enough for either of its labels, so switching between them moves nothing beside it.</summary>
    private static void HoldWidth(Button button, string other)
    {
        string shown = button.Text;
        float width = button.GetCombinedMinimumSize().X;
        button.Text = other;
        width = Math.Max(width, button.GetCombinedMinimumSize().X);
        button.Text = shown;
        button.CustomMinimumSize = new Vector2(width, button.CustomMinimumSize.Y);
        button.Size = new Vector2(width, button.Size.Y);
    }
```

- [ ] **Step 6: Copying, and the status line**

In `src/WhoCarried/UI/RecapUi.cs`:

Add `using System.Globalization;` at the top, with the other usings.

Add a field after `_resizePending`:

```csharp
    private static bool _copying;
```

In `ShowView`, replace

```csharp
        PanelHandle handle = RecapPanel.Create(view, icons, cards, Hide, h => Export(_currentView ?? view, icons, h));
```

with

```csharp
        PanelHandle handle = RecapPanel.Create(view, icons, cards, Hide, h => Export(_currentView ?? view, icons, h),
            h => CopyToClipboard(_currentView ?? view, icons, h));
```

Replace the whole `Export` method with these three methods:

```csharp
    /// <summary>
    /// Saves the summary card: into the player's Steam screenshots when the game runs on Steam, the same on every OS;
    /// otherwise as a PNG in the game's data folder.
    /// </summary>
    private static void Export(RecapView view, Func<string?, Texture2D?> icons, PanelHandle handle)
    {
        handle.Status.Text = Loc.Text("WHO_CARRIED.export.saving");
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
        if (GodotObject.IsInstanceValid(handle.Status)) handle.Status.Text = Loc.Text("WHO_CARRIED.copy.copying");
        void Failed(string? reason)
        {
            _copying = false;
            Tracker.Note($"copy failed: {reason}");
            Say(handle, Loc.Text("WHO_CARRIED.copy.failed", Loc.Text("WHO_CARRIED.action.save_image")));
        }
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
                handle.ShowCopied(true);
                Later.Run(2.0, () =>
                {
                    if (GodotObject.IsInstanceValid(handle.Root)) handle.ShowCopied(false);
                });
            });
        });
    }

    /// <summary>A message in the status line, cleared four seconds later, if the recap is still open.</summary>
    private static void Say(PanelHandle handle, string text)
    {
        if (GodotObject.IsInstanceValid(handle.Status)) handle.Status.Text = text;
        Later.Run(4.0, () =>
        {
            if (GodotObject.IsInstanceValid(handle.Status)) handle.Status.Text = "";
        });
    }
```

- [ ] **Step 7: The five keys**

In `src/WhoCarried/Localization/eng.json`, after the `"WHO_CARRIED.action.close": "Close",` line, add:

```json
  "WHO_CARRIED.action.copied": "Copied",
  "WHO_CARRIED.action.copy_image": "Copy to clipboard",
```

and after the `"WHO_CARRIED.awards.wait": …` line, add:

```json
  "WHO_CARRIED.copy.copying": "Copying...",
  "WHO_CARRIED.copy.done": "Copied. Paste it anywhere with {0}",
  "WHO_CARRIED.copy.failed": "Couldn't copy the image. Try {0} instead.",
```

In `src/WhoCarried/Localization/zhs.json`, after the `"WHO_CARRIED.action.close": "关闭",` line, add:

```json
  "WHO_CARRIED.action.copied": "已复制",
  "WHO_CARRIED.action.copy_image": "复制到剪贴板",
```

and after the `"WHO_CARRIED.awards.wait": …` line, add:

```json
  "WHO_CARRIED.copy.copying": "正在复制…",
  "WHO_CARRIED.copy.done": "已复制，按 {0} 即可粘贴",
  "WHO_CARRIED.copy.failed": "图片复制失败，请改用“{0}”",
```

- [ ] **Step 8: The copy check presses the real button**

In `src/WhoCarried/UI/DevPreview.cs`, replace `CheckCopy` with:

```csharp
    /// <summary>
    /// "copy" in the flag: Copy to clipboard for real, on a sample whose first player has a long name. Saves the picture
    /// (preview-copy-share.png, and a mid-run one as preview-share-midrun.png). Clicks the button and closes the recap
    /// straight away, which must not throw; then opens it again and double-clicks, which must copy once. Screenshots the
    /// Copied button and the status line (preview-copy.png), and reads the clipboard back. Overwrites the clipboard.
    /// </summary>
    private static void CheckCopy(string dataDir, Sample sample)
    {
        SaveShare(dataDir, "preview-share-midrun.png", sample.View with { Victory = null }, sample.Icons, () =>
            SaveShare(dataDir, "preview-copy-share.png", sample.View, sample.Icons, () =>
            {
                RecapUi.ShowView(sample.View, sample.Icons, new CardVisuals(sample.CardFor)).Copy();
                RecapUi.Hide();
                Later.Run(2.5, () =>
                {
                    PanelHandle handle = RecapUi.ShowView(sample.View, sample.Icons, new CardVisuals(sample.CardFor));
                    handle.Copy();
                    handle.Copy(); // a double-click: events.log gains one copy line for the two
                    // A copy takes about half a second; "Copied" then shows for two, the status line for four.
                    Later.Run(1.5, () =>
                    {
                        ((SceneTree)Engine.GetMainLoop()).Root.GetTexture().GetImage().SavePng(Path.Combine(dataDir, "preview-copy.png"));
                        Tracker.Note($"preview copy: status '{handle.Status.Text}'");
                        ReadBack();
                        RecapUi.Hide();
                        Tracker.Note("preview done");
                    });
                });
            }));
    }
```

- [ ] **Step 9: Build and run every test**

Run: `"/c/Program Files/dotnet/dotnet.exe" build src/WhoCarried -c Release`
Expected: `Build succeeded`, with 0 errors.

Run: `"/c/Program Files/dotnet/dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: no `FAIL` lines. The localization tests pass with the five new keys used and translated.

- [ ] **Step 10: Press it in the game**

Follow "Running a preview in the game" with the flag `copy eng`. In `events.log`:
- **exactly two** `copied summary to clipboard, 1920×1080, … MB` lines: one for the copy made while the recap closed, and one for the double-click;
- no `copy failed` and no `ERROR`;
- `preview copy: status 'Copied. Paste it anywhere with Ctrl+V'`;
- `preview copy: formats PNG, DIB, …` and `preview copy: read back 1920×1080`.

In `preview-copy.png`:
- the tab row shows "Copied" in green on the bronze stone, then "Export as image" on the grey stone, a small gap between them;
- the top bar's status line reads "Copied. Paste it anywhere with Ctrl+V", clear of the F8 hint and of Close.

If the status line runs into the F8 hint or Close, shorten the English to "Copied. Paste with {0}" in `eng.json` and tell the owner.

If the screenshot shows "Copying...", the copy took longer than 1.5 s. Move the screenshot later, keeping it under 2 s after the copy line, and run again.

- [ ] **Step 11: Commit**

```bash
git add src/WhoCarried/Core/HewnStoneArt.cs tests/WhoCarried.Tests/HewnStoneArtTests.cs src/WhoCarried/UI/HewnStone.cs src/WhoCarried/UI/RecapPanel.cs src/WhoCarried/UI/RecapUi.cs src/WhoCarried/UI/DevPreview.cs src/WhoCarried/Localization/eng.json src/WhoCarried/Localization/zhs.json
git commit -m "feat: Copy to clipboard on the recap" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Check it in the game

No new code: run the matrix below, save each run's files under `runs/2026-09-27-copy-to-clipboard/<case>/`, and fix and commit anything that's off. Each step is one preview run (see "Running a preview in the game").

- [ ] **Step 1: 4 players, English (flag `4`)**

- `preview-share.png` passes Task 3 Step 9's checks, and `events.log` says it's `1920×1080`.
- The export is unchanged. `sha256sum runs/2026-09-27-copy-to-clipboard/before/preview-10-export.png runs/2026-09-27-copy-to-clipboard/4/preview-10-export.png` should print the same hash twice. If it doesn't, find where the images differ with the script in Step 8, and fix `SummaryCard.TopBar` until the hashes match.
- `preview-1-scoreboard.png` shows the tab row with "Copy to clipboard" on the bronze stone, then "Export as image" on the grey one, not touching.
- `preview-12-pad-tab.png` is controller mode: Export as image wears the controller's glyph and still clears everything to its right.

- [ ] **Step 2: 2 players, English (flag `2 eng`)**

`preview-share.png`: two cards, four top sources each, with the gold beside each name. The support row and the footer are in place.

- [ ] **Step 3: 4 players, Chinese (flag `4 zhs`)**

`preview-share.png` (Review focus 1):
- the support heading reads 给队友的支援, and the block card reads 给予格挡;
- the gold reads "获得 … 金币";
- Top sources' last group ends above the support heading, not over it, and the logged height fits.

`preview-13-pad-card.png`: 复制到剪贴板 and 导出战绩图 sit on the tab row without touching.

- [ ] **Step 4: 5 players, Chinese (flag `IRONCLAD,SILENT,REGENT,NECROBINDER,DEFECT zhs`)**

`preview-share.png` (Review focus 2):
- one top source each;
- the log's height is 1080 or taller;
- the footer shows whole at the foot;
- no card runs into Top sources;
- if a support card lists five players, the picture is taller than 1080.

- [ ] **Step 5: 1 player, English (flag `1 eng`)**

This run also sets the game back to English. In `preview-share.png`:
- one card, with "Your run" beside it;
- Top sources has six rows, with the gold beside the name;
- the climb sits in the bottom row, since nobody can give a teammate anything in solo;
- no support heading.

- [ ] **Step 6: Copy, final build (flag `copy eng`)**

Repeat Task 4 Step 5 and Task 5 Step 10's checks on this build:
- two copy lines;
- the formats;
- the read-back size;
- the PowerShell check from outside the game;
- the long name trimmed in `preview-copy-share.png` (Review focus 3);
- `preview-share-midrun.png`.

- [ ] **Step 7: Ask the owner to paste it**

Send the owner:
- `preview-share.png` from Steps 1, 3 and 4;
- `preview-copy.png`;
- `preview-copy-share.png`.

Then ask them to open the recap after a real run (or mid-run with F8), click Copy to clipboard, and paste into Discord and into Paint. Ask whether:
- the chat preview reads: the result, names and damage numbers;
- the full-size view is sharp;
- Paint shows the same picture.

- [ ] **Step 8 (only if Step 1's hashes differ): Find where two images differ**

Save this as `runs/2026-09-27-copy-to-clipboard/same-pixels.ps1`, then run `powershell -File runs/2026-09-27-copy-to-clipboard/same-pixels.ps1 <before.png> <after.png>`:

```powershell
param([string]$A, [string]$B)
Add-Type -AssemblyName System.Drawing
function Pixels([string]$path) {
    $bitmap = [System.Drawing.Bitmap]::FromFile((Resolve-Path $path))
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bitmap.Width, $bitmap.Height
    $data = $bitmap.LockBits($rect, 'ReadOnly', 'Format32bppArgb')
    $bytes = New-Object byte[] ($data.Stride * $bitmap.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $bitmap.UnlockBits($data)
    $result = @{ W = $bitmap.Width; H = $bitmap.Height; Bytes = $bytes }
    $bitmap.Dispose()
    return $result
}
$a = Pixels $A
$b = Pixels $B
if ($a.W -ne $b.W -or $a.H -ne $b.H) { "size differs: $($a.W)x$($a.H) vs $($b.W)x$($b.H)"; exit 1 }
$count = 0; $first = -1
for ($i = 0; $i -lt $a.Bytes.Length; $i++) {
    if ($a.Bytes[$i] -ne $b.Bytes[$i]) { $count++; if ($first -lt 0) { $first = $i } }
}
if ($count -eq 0) { "identical"; exit 0 }
"$count bytes differ; the first in row $([math]::Floor($first / ($a.W * 4)))"
```

- [ ] **Step 9: Tidy up and commit any fixes**

Delete `preview.flag`. If any step needed a fix, commit it with a message saying what it fixed, for example:

```bash
git add <the changed files>
git commit -m "fix: <what the check found>" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Document it

**Files:**
- Modify: `README.md`, `CHANGELOG.md`

- [ ] **Step 1: README**

In "What it shows", replace

```markdown
**Export as image** renders the whole run as one tall summary card and puts it in the player's Steam screenshots.
```

with

```markdown
**Copy to clipboard** puts a picture of the scoreboard on the clipboard, ready to paste into Discord: everyone's card, their top sources and gold earned, and what they gave their teammates. **Export as image** renders the whole run as one tall summary card and puts it in the player's Steam screenshots.
```

In "Developer checks", in the `preview.flag` bullet, replace

```markdown
screenshots every view into the data folder, then closes it.
```

with

```markdown
screenshots every view into the data folder, with the exported image and the Copy to clipboard picture (`preview-share.png`), then closes it.
```

At the end of that bullet, add:

```markdown
Use `copy` (or `copy zhs`) to check Copy to clipboard for real, on a sample whose first player has a long name. It saves the picture (`preview-copy-share.png`, and a mid-run one as `preview-share-midrun.png`), clicks the button with the recap closing straight after, then double-clicks it. It logs the clipboard's formats and the size read back from it, and saves `preview-copy.png` showing the button and status line. It overwrites your clipboard.
```

- [ ] **Step 2: CHANGELOG**

Under `## [Unreleased]`, at the end of `### Added`, add:

```markdown
- **Copy to clipboard**, beside Export as image on the recap: one click puts a picture of the run on the clipboard, ready to paste into Discord. It's the scoreboard without the recap's buttons, with each player's gold earned and what they gave their teammates. Tried on Windows; Steam Deck (Desktop Mode) and Mac haven't been tried yet.
```

At the end of `### Changed`, add:

```markdown
- Export as image moves to a plain stone beside Copy to clipboard, which takes the bronze one.
```

If Task 6 found Steam Deck or Mac working, say so instead of "haven't been tried yet".

- [ ] **Step 3: Commit**

```bash
git add README.md CHANGELOG.md
git commit -m "docs: Copy to clipboard in the README and changelog" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Still open after this plan

- **Steam Deck (Desktop Mode, Proton) and Mac need players to try them.** Ask:
  - Does clicking Copy to clipboard and pasting into Discord, or a browser, show the picture?
  - On a Deck, is it still there after quitting the game?
  - If it fails, what's the `copy failed` line in `events.log`?

  If a Deck loses the picture on quitting, add "paste before quitting the game" to the README's Steam Deck section.
- **The Workshop change note is written at release:** short, friendly bullets under New / Improved / Fixed.
