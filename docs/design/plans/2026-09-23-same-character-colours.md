# Telling apart players on the same character — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Players on the same character stop blending together. Everyone after the first on a colour gets a gentle shade of it, used everywhere. The climb gets a gap between every segment, and the Timeline names each line at its end. Separately, scoreboard portraits zoom out partway.

**Architecture:** All the rules are plain C# in `Core/`, unit-tested:
- `ColourMath`: hex, readability, HSL, ΔE.
- `PlayerShades`: who gets which shade.
- `EndLabels`: spreading the Timeline's names.
- `PictureFit`: zoomed picture placement.
- `ChartMath.SegmentGap`: the gap under each stack segment.

`RecapBuilder.Build` shades the players before building any view, so every tab, card and the exported image pick the shades up with no drawing changes. The UI tasks only draw: gaps in `Climb`, names in `TimelineTab`, zoom and side strips in `Kit.Cover`, `GameArt` and `CardFace`.

**Tech Stack:** C# / .NET 9, Godot 4.5.1 mono (the game's `GodotSharp.dll`), the repo's own console test runner.

Spec: [`docs/design/specs/2026-09-23-same-character-colours-design.md`](../specs/2026-09-23-same-character-colours-design.md)

## Global Constraints

- **No git operations.** No branch, no commit, no push, no stash, unless the owner asks in the session. Each task ends at a checkpoint instead of a commit: the build and tests pass and the task is reported.
- **Nothing names a character or a colour** in shipped code. The shade rule works only from the colours in the run. (The test palette and dev-preview flag values are data, not rules.)
- **`Core/` has no Godot or game types.** `Color`, `Texture2D`, `Image` only under `UI/`.
- **No new on-screen text,** so no new `eng.json`/`zhs.json` keys. The "…" that shortens a long name is punctuation, not a key. `LocalizationTests` must stay green.
- **Nothing here may take the recap down.** Reading a texture's pixels is wrapped in `try`, with a fallback.
- **No new dependencies,** no NuGet packages, no new `GameCompat` entries.
- **Build with the x64 SDK.** An x86 `dotnet.exe` shadows it on PATH, so always call `C:\Program Files\dotnet\dotnet.exe` by full path.
- **Don't deploy or launch the game without asking.** The game locks the DLL, and the owner may be playing.
- **Design pixels, not screen pixels.** UI sizes here are in the panel's 1600×900 design space and go through `k.U`/`k.V`/`k.F`.
- **Starting values** (spec): lightness ceiling **0.78**; hue nudge **≤ 12°** in **3°** steps; lightness steps **0.01**; target **ΔE 20**; cost **|Δlightness| × 100 + |Δhue| × 0.5**; floor range **0.12**; segment gap **2 px**; name room **150 px**, size **18 px**, gap **20 px**; portrait zoom **0.8**. The in-game check in Task 7 may tune them, but the tests are the limits.

**Commands** (PowerShell, from the repo root):

| What | Command |
|---|---|
| Build | `& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release` |
| All tests | `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests` |
| One test class | `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests PlayerShades` |

The runner takes an optional name filter (matched against `Class.Method`) as its first argument. Tests are `public static` methods marked `[Test]`, asserted with `Check.Equal(expected, actual, label)`, `Check.True(condition, label)` and `Check.Near(expected, actual, label, tolerance)`.

**The suite is 238 tests before this work.** Running total after each task: 1 → 243, 2 → 257, 3 → 258, 4 → 264, 5 → 269.

**This plan's code has been compiled and its tests run once already.** The Core files and tests were run in a scratch copy against the current suite: all 31 new tests pass. The UI edits were built against the game's DLLs: 0 warnings. If something here doesn't compile, the repo has moved since 2026-09-23. Re-read the lines being edited rather than forcing the plan's text in.

## File structure

| File | Responsibility |
|---|---|
| `src/WhoCarried/Core/ColourMath.cs` | **New.** Hex in and out, luminance, `Readable` (the rule `Accent` used), HSL, hue gap, ΔE. |
| `src/WhoCarried/Core/PlayerShades.cs` | **New.** Players sharing a colour get shades of it. |
| `src/WhoCarried/Core/EndLabels.cs` | **New.** Spreading names at the ends of lines. |
| `src/WhoCarried/Core/PictureFit.cs` | **New.** Where a picture goes in a box, from cover to contain. |
| `src/WhoCarried/Core/ChartMath.cs` | `SegmentGap`. |
| `src/WhoCarried/Core/RecapBuilder.cs` | Shades the players first. |
| `src/WhoCarried/UI/RecapTheme.cs` | `Accent` calls `ColourMath.Readable`. |
| `src/WhoCarried/UI/Climb.cs` | Gaps between segments. |
| `src/WhoCarried/UI/TimelineTab.cs` | Names at the lines' ends. |
| `src/WhoCarried/UI/Kit.cs` | `Cover` takes `zoom` and `sides`. |
| `src/WhoCarried/UI/GameArt.cs` | `EdgeColours`: a picture's edge colours, cached. |
| `src/WhoCarried/UI/CardFace.cs` | Portrait zoom and side strips. |
| `src/WhoCarried/UI/DevPreview.cs` | The character-list flag; a fifth sample name. |
| `tests/WhoCarried.Tests/ColourMathTests.cs` | **New.** |
| `tests/WhoCarried.Tests/PlayerShadesTests.cs` | **New.** |
| `tests/WhoCarried.Tests/EndLabelsTests.cs` | **New.** |
| `tests/WhoCarried.Tests/PictureFitTests.cs` | **New.** |
| `tests/WhoCarried.Tests/ChartMathTests.cs` | One new test. |
| `tests/WhoCarried.Tests/RecapBuilderTests.cs` | One new test. |
| `README.md`, `docs/README.md`, `CHANGELOG.md` | The preview flag's new form, the spec/plan row, the `[Unreleased]` entry. |

Seven tasks. Tasks 1–2 are the colours, 3 the gaps, 4 the names and 5 the portraits: each ends with a build and a green suite, and leaves the recap working. Task 6 is the dev-preview flag and docs. Task 7 is the in-game check and tuning, and needs the owner.

**Where tests can and can't go:** everything in `Core/` is unit-tested first (TDD). The `UI/` edits have no unit tests, because the repo never mocks Godot; they're checked by the build and in game (Task 7). Don't add Godot mocks.

---

### Task 1: Colour maths, and `Accent` on top of it

**Files:**
- Create: `src/WhoCarried/Core/ColourMath.cs`
- Modify: `src/WhoCarried/UI/RecapTheme.cs` (`Accent`, around line 82)
- Test: `tests/WhoCarried.Tests/ColourMathTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (namespace `WhoCarried.Core`):
  - `ColourMath.Rgb(double R, double G, double B)`, a readonly record struct, channels 0–1.
  - `const double ReadableLuminance = 0.45`.
  - `Rgb? Parse(string? hex)` accepts `"d85a30"`, `"#D85A30"`, `"fff"`, and returns null otherwise. `string Normalize(string? hex)` lower-cases and strips `#`. `string Hex(Rgb)` gives six lower-case digits. `Rgb Round(Rgb)` rounds to 8-bit.
  - `double Luminance(Rgb)`, `Rgb Readable(Rgb)`, `string Readable(string hex)` (unreadable hex comes back unchanged).
  - `(double H, double S, double L) ToHsl(Rgb)`, `Rgb FromHsl(double h, double s, double l)`, `double HueGap(double from, double to)` (signed, −180 to 180).
  - `double DeltaE(Rgb a, Rgb b)`: CIE76.

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/ColourMathTests.cs`:

```csharp
using WhoCarried.Core;
using static WhoCarried.Core.ColourMath;

namespace WhoCarried.Tests;

/// <summary>Hex, the readability rule, HSL and ΔE: the numbers the player shades are built on.</summary>
public static class ColourMathTests
{
    [Test]
    public static void HexReadsTheFormsTheModMeets()
    {
        Check.Equal("d85a30", Hex(Parse("d85a30")!.Value), "game's ToHtml(false)");
        Check.Equal("d85a30", Hex(Parse("#D85A30")!.Value), "hash and capitals");
        Check.Equal("ffffff", Hex(Parse("fff")!.Value), "three digits");
        Check.True(Parse("nope") == null, "not a colour");
        Check.True(Parse("") == null, "empty");
        Check.True(Parse(null) == null, "null");
        Check.Equal("d85a30", Normalize(" #D85A30 "), "normalized for comparing");
    }

    [Test]
    public static void ReadableMatchesTheRecapsOldAccentRule()
    {
        // Accent mixed 15% toward white until luminance reached 0.45. Ironclad's red sits just under it: one mix.
        Rgb red = Parse("d85a30")!.Value;
        Check.True(Luminance(red) < ReadableLuminance, "Ironclad starts under the line");
        Check.Equal("de734f", Readable("d85a30"), "one mix toward white");
        Check.Equal("8fd46a", Readable("8fd46a"), "a bright colour is left alone");
        Check.True(Luminance(Parse(Readable("3a2010"))!.Value) >= ReadableLuminance, "a dark brown is lightened until readable");
        Check.Equal("not a colour", Readable("not a colour"), "unreadable hex comes back as it was");
    }

    [Test]
    public static void HslRoundTrips()
    {
        foreach (string hex in new[] { "d85a30", "e46fd6", "5ec2e0", "3040ff", "ffa518", "8fd46a", "7a4f2c", "f0f0f0", "000000" })
        {
            (double h, double s, double l) = ToHsl(Parse(hex)!.Value);
            Check.Equal(hex, Hex(FromHsl(h, s, l)), $"{hex} through HSL and back");
        }
        (double hue, _, _) = ToHsl(Parse("ff0000")!.Value);
        Check.Near(0, hue, "red is hue 0");
        (hue, _, _) = ToHsl(Parse("0000ff")!.Value);
        Check.Near(240, hue, "blue is hue 240");
    }

    [Test]
    public static void HueGapTakesTheShortWayRound()
    {
        Check.Near(20, HueGap(10, 30), "forward");
        Check.Near(-20, HueGap(30, 10), "back");
        Check.Near(20, HueGap(350, 10), "forward through 0");
        Check.Near(-20, HueGap(10, 350), "back through 0");
    }

    [Test]
    public static void DeltaEOrdersHowDifferentColoursLook()
    {
        Rgb red = Parse("d85a30")!.Value;
        Check.Near(0, DeltaE(red, red), "a colour against itself");
        Check.Near(100, DeltaE(Parse("000000")!.Value, Parse("ffffff")!.Value), "black to white", 0.5);
        Check.True(DeltaE(red, Parse("da5c32")!.Value) < 2, "a hair's difference is tiny");
        Check.True(DeltaE(red, Parse("5ec2e0")!.Value) > 50, "red and light blue are far apart");
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests ColourMath`
Expected: build error, `The name 'ColourMath' does not exist`, or `CS0246` for `ColourMath`.

- [ ] **Step 3: Write `ColourMath`**

Create `src/WhoCarried/Core/ColourMath.cs`:

```csharp
using System.Globalization;

namespace WhoCarried.Core;

/// <summary>
/// Player colours as plain numbers: hex in and out, the recap's readability rule, HSL, and how different two colours
/// look (CIE76 ΔE). No Godot types, so the rules built on it are unit-tested.
/// </summary>
public static class ColourMath
{
    /// <summary>A colour with channels from 0 to 1, as the hex says (sRGB, no alpha).</summary>
    public readonly record struct Rgb(double R, double G, double B);

    /// <summary>What the recap needs a colour's luminance to be before it draws text or thin bars in it.</summary>
    public const double ReadableLuminance = 0.45;

    /// <summary>"d85a30", "#D85A30" or "fff"; null for anything else.</summary>
    public static Rgb? Parse(string? hex)
    {
        string h = Normalize(hex);
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return null;
        return new Rgb((v >> 16 & 0xff) / 255.0, (v >> 8 & 0xff) / 255.0, (v & 0xff) / 255.0);
    }

    /// <summary>Lower case, without a leading '#': how two colours are compared.</summary>
    public static string Normalize(string? hex) => (hex ?? "").Trim().TrimStart('#').ToLowerInvariant();

    /// <summary>Six lower-case hex digits, each channel rounded and clamped.</summary>
    public static string Hex(Rgb c) => $"{Byte(c.R):x2}{Byte(c.G):x2}{Byte(c.B):x2}";

    /// <summary>The colour as 8-bit channels would store it.</summary>
    public static Rgb Round(Rgb c) => new(Byte(c.R) / 255.0, Byte(c.G) / 255.0, Byte(c.B) / 255.0);

    private static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);

    /// <summary>Godot's <c>Color.Luminance</c>: the weighted channels, as stored.</summary>
    public static double Luminance(Rgb c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    /// <summary>
    /// A colour made readable on the dark table: mixed 15% toward white until it's bright enough, at most ten times.
    /// This is <c>RecapTheme.Accent</c>'s rule, which now calls it.
    /// </summary>
    public static Rgb Readable(Rgb c)
    {
        for (int i = 0; i < 10 && Luminance(c) < ReadableLuminance; i++)
            c = new Rgb(c.R + (1 - c.R) * 0.15, c.G + (1 - c.G) * 0.15, c.B + (1 - c.B) * 0.15);
        return c;
    }

    /// <summary>The readable version of a hex colour, as hex; a hex that can't be read comes back unchanged.</summary>
    public static string Readable(string hex) => Parse(hex) is Rgb c ? Hex(Readable(c)) : hex;

    /// <summary>Hue in degrees (0–360), saturation and lightness (0–1).</summary>
    public static (double H, double S, double L) ToHsl(Rgb c)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
        double l = (max + min) / 2;
        if (max == min) return (0, 0, l);
        double d = max - min;
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == c.R ? (c.G - c.B) / d + (c.G < c.B ? 6 : 0)
                 : max == c.G ? (c.B - c.R) / d + 2
                 : (c.R - c.G) / d + 4;
        return (h * 60, s, l);
    }

    public static Rgb FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360;
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double Channel(double t)
        {
            t = ((t % 1) + 1) % 1;
            return t < 1 / 6.0 ? p + (q - p) * 6 * t
                 : t < 0.5 ? q
                 : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6
                 : p;
        }
        return new Rgb(Channel(h + 1 / 3.0), Channel(h), Channel(h - 1 / 3.0));
    }

    /// <summary>The signed turn from one hue to another, -180 to 180 degrees.</summary>
    public static double HueGap(double from, double to) => ((to - from) % 360 + 540) % 360 - 180;

    /// <summary>
    /// How different two colours look (CIE76 ΔE in Lab, D65 white). Under 10 is hard to tell apart at small sizes;
    /// over 20 is clearly different.
    /// </summary>
    public static double DeltaE(Rgb a, Rgb b)
    {
        (double L, double A, double B) x = Lab(a), y = Lab(b);
        return Math.Sqrt((x.L - y.L) * (x.L - y.L) + (x.A - y.A) * (x.A - y.A) + (x.B - y.B) * (x.B - y.B));
    }

    private static (double L, double A, double B) Lab(Rgb c)
    {
        static double Linear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16 / 116.0;
        double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);
        double x = (r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047;
        double y = r * 0.2126 + g * 0.7152 + b * 0.0722;
        double z = (r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests ColourMath`
Expected: `5/5 passed`.

- [ ] **Step 5: Point `Accent` at it**

In `src/WhoCarried/UI/RecapTheme.cs`, replace the body of `Accent`, keeping its doc comment:

```csharp
    public static Color Accent(string hex)
    {
        Color color = FromHex(hex);
        for (int i = 0; i < 10 && color.Luminance < 0.45f; i++) color = color.Lightened(0.15f);
        return color;
    }
```

with:

```csharp
    public static Color Accent(string hex) => FromHex(Core.ColourMath.Readable(hex));
```

This is the same rule. The only difference is rounding to 8-bit channels (under 1/255). A hex that can't be read still turns grey through `FromHex`.

- [ ] **Step 6: Build and run everything**

Run the build, then all tests.
Expected: `Build succeeded` with 0 warnings, and `243/243 passed`.

- [ ] **Checkpoint:** report the task done. No commit.

---

### Task 2: Shades for players who share a colour

**Files:**
- Create: `src/WhoCarried/Core/PlayerShades.cs`
- Modify: `src/WhoCarried/Core/RecapBuilder.cs` (the start of `Build`, around line 99)
- Test: `tests/WhoCarried.Tests/PlayerShadesTests.cs` (new), `tests/WhoCarried.Tests/RecapBuilderTests.cs` (one test)

**Interfaces:**
- Consumes: `ColourMath` (Task 1); `PlayerInfo(ulong NetId, string Name, string Character, string ColorHex, string CharacterId = "")` from `Core/Model.cs`.
- Produces: `PlayerShades.Assign(IReadOnlyList<PlayerInfo> players) → IReadOnlyList<PlayerInfo>`, the same players in the same order, with `ColorHex` replaced for everyone after the first on a colour. Public constants: `Ceiling = 0.78`, `MinRange = 0.12`, `HueCap = 12`, `HueStep = 3`, `LightStep = 0.01`, `Apart = 20`, `LightCost = 100`, `HueCost = 0.5`.

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/PlayerShadesTests.cs`:

```csharp
using WhoCarried.Core;
using static WhoCarried.Core.ColourMath;

namespace WhoCarried.Tests;

/// <summary>
/// Players who share a colour get shades of it. The palette stands in for the game's colours and a few awkward
/// modded ones; the limits are what the design promises, and tuning the constants must stay inside them.
/// </summary>
public static class PlayerShadesTests
{
    private const string Red = "d85a30", Pink = "e46fd6", LightBlue = "5ec2e0", DeepBlue = "3040ff", Orange = "ffa518",
        Green = "8fd46a", Brown = "7a4f2c", NearWhite = "f0f0f0";

    private static readonly string[] Palette = { Red, Pink, LightBlue, DeepBlue, Orange, Green, Brown, NearWhite };

    private static PlayerInfo[] Lobby(params string[] hexes) =>
        hexes.Select((h, i) => new PlayerInfo((ulong)(i + 1), $"P{i + 1}", "Someone", h)).ToArray();

    private static Rgb Shown(PlayerInfo p) => Readable(Parse(p.ColorHex)!.Value);

    /// <summary>The smallest ΔE from a shaded player to anyone else in the run, as the recap draws them.</summary>
    private static double ClosestToAShade(IReadOnlyList<PlayerInfo> before, IReadOnlyList<PlayerInfo> after)
    {
        double closest = double.MaxValue;
        for (int i = 0; i < after.Count; i++)
        {
            if (after[i].ColorHex == before[i].ColorHex) continue;
            for (int j = 0; j < after.Count; j++)
                if (j != i) closest = Math.Min(closest, DeltaE(Shown(after[i]), Shown(after[j])));
        }
        return closest;
    }

    [Test]
    public static void PlayersOnTheirOwnColourAreUntouched()
    {
        PlayerInfo[] lobby = Lobby(Red, Green, LightBlue, Orange);
        IReadOnlyList<PlayerInfo> shaded = PlayerShades.Assign(lobby);
        for (int i = 0; i < lobby.Length; i++) Check.Equal(lobby[i], shaded[i], $"player {i + 1}");
    }

    [Test]
    public static void TheFirstOfEachColourKeepsItAndTheRestGetShades()
    {
        PlayerInfo[] lobby = Lobby(Red, Green, Red, Green, Red);
        IReadOnlyList<PlayerInfo> shaded = PlayerShades.Assign(lobby);
        Check.Equal(lobby[0], shaded[0], "first red");
        Check.Equal(lobby[1], shaded[1], "first green");
        foreach (int i in new[] { 2, 3, 4 })
        {
            Check.True(shaded[i].ColorHex != lobby[i].ColorHex, $"player {i + 1} shaded");
            Check.Equal(lobby[i] with { ColorHex = shaded[i].ColorHex }, shaded[i], $"player {i + 1}: only the colour changes");
        }
    }

    [Test]
    public static void ColoursAreComparedIgnoringCaseAndHash()
    {
        IReadOnlyList<PlayerInfo> shaded = PlayerShades.Assign(Lobby("d85a30", "#D85A30"));
        Check.True(Normalize(shaded[1].ColorHex) != "d85a30", "the same colour written differently is still shared");
    }

    [Test]
    public static void ShadesAreReadableSoAccentLeavesThemAlone()
    {
        foreach (string hex in Palette)
        foreach (int n in new[] { 2, 3, 5 })
        foreach (PlayerInfo p in PlayerShades.Assign(Lobby(Enumerable.Repeat(hex, n).ToArray())).Skip(1))
            Check.Equal(p.ColorHex, Readable(p.ColorHex), $"{n} × {hex}: {p.ColorHex}");
    }

    [Test]
    public static void ShadesStayInTheCharactersFamily()
    {
        foreach (string hex in Palette)
        foreach (int n in new[] { 2, 3, 5 })
        {
            (double h, double s, _) = ToHsl(Readable(Parse(hex)!.Value));
            foreach (PlayerInfo p in PlayerShades.Assign(Lobby(Enumerable.Repeat(hex, n).ToArray())).Skip(1))
            {
                (double ph, _, double pl) = ToHsl(Parse(p.ColorHex)!.Value);
                if (s > 0.05) Check.True(Math.Abs(HueGap(h, ph)) <= PlayerShades.HueCap + 1, $"{n} × {hex}: {p.ColorHex} hue");
                Check.True(pl <= Math.Max(PlayerShades.Ceiling, Floor(hex) + PlayerShades.MinRange) + 0.01,
                    $"{n} × {hex}: {p.ColorHex} no lighter than the ceiling");
            }
        }
    }

    /// <summary>Where this colour becomes readable, found the long way for the test.</summary>
    private static double Floor(string hex)
    {
        (double h, double s, _) = ToHsl(Readable(Parse(hex)!.Value));
        for (double l = 0; l <= 1; l += 0.001)
            if (Luminance(FromHsl(h, s, l)) >= ReadableLuminance) return l;
        return 1;
    }

    [Test]
    public static void PairsAreClearlyDifferent()
    {
        foreach (string hex in Palette)
        {
            PlayerInfo[] lobby = Lobby(hex, hex);
            double closest = ClosestToAShade(lobby, PlayerShades.Assign(lobby));
            Check.True(closest >= 20, $"2 × {hex}: ΔE {closest:0.0}");
        }
    }

    [Test]
    public static void ThreesCanBeToldApart()
    {
        foreach (string hex in Palette)
        {
            PlayerInfo[] lobby = Lobby(hex, hex, hex);
            double closest = ClosestToAShade(lobby, PlayerShades.Assign(lobby));
            Check.True(closest >= 11, $"3 × {hex}: ΔE {closest:0.0}");
        }
    }

    [Test]
    public static void FoursAndFivesAreAllDistinct()
    {
        foreach (string hex in Palette)
        foreach (int n in new[] { 4, 5 })
        {
            PlayerInfo[] lobby = Lobby(Enumerable.Repeat(hex, n).ToArray());
            IReadOnlyList<PlayerInfo> shaded = PlayerShades.Assign(lobby);
            Check.Equal(n, shaded.Select(p => Normalize(p.ColorHex)).Distinct().Count(), $"{n} × {hex}: distinct");
            double closest = ClosestToAShade(lobby, shaded);
            Check.True(closest >= 5, $"{n} × {hex}: ΔE {closest:0.0}");
        }
    }

    [Test]
    public static void ShadesKeepClearOfOtherCharacters()
    {
        foreach (string[] hexes in new[]
                 {
                     new[] { Red, Orange, Orange, Orange },
                     new[] { Red, Red, Red, Orange },
                     new[] { Red, Red, Green, Green },
                 })
        {
            PlayerInfo[] lobby = Lobby(hexes);
            double closest = ClosestToAShade(lobby, PlayerShades.Assign(lobby));
            Check.True(closest >= 15, $"{string.Join(" + ", hexes)}: ΔE {closest:0.0}");
        }
    }

    [Test]
    public static void RedBesideOrangeLeansAwayFromIt()
    {
        IReadOnlyList<PlayerInfo> shaded = PlayerShades.Assign(Lobby(Red, Red, Orange));
        double red = ToHsl(Readable(Parse(Red)!.Value)).H, shade = ToHsl(Parse(shaded[1].ColorHex)!.Value).H;
        Check.True(HueGap(red, shade) <= 1, $"the second red ({shaded[1].ColorHex}) turns toward crimson, not orange");
    }

    [Test]
    public static void TheSamePlayersGetTheSameShadesEveryTime()
    {
        PlayerInfo[] lobby = Lobby(Red, Red, Red, Orange, Pink);
        string first = string.Join(",", PlayerShades.Assign(lobby).Select(p => p.ColorHex));
        for (int i = 0; i < 3; i++)
            Check.Equal(first, string.Join(",", PlayerShades.Assign(lobby).Select(p => p.ColorHex)), $"rebuild {i + 1}");
    }

    [Test]
    public static void AColourThatCantBeReadIsLeftAlone()
    {
        PlayerInfo[] lobby = Lobby("??", "??");
        IReadOnlyList<PlayerInfo> shaded = PlayerShades.Assign(lobby);
        Check.Equal("??", shaded[1].ColorHex, "nothing to shade from");
    }
}
```

Add to `tests/WhoCarried.Tests/RecapBuilderTests.cs`, inside the class. It uses the file's existing `NoDefense` and `Card` helpers:

```csharp
    [Test]
    public static void PlayersSharingAColourAreShadedInEveryView()
    {
        var alice = new PlayerInfo(1, "Alice", "Ironclad", "d85a30", "IRONCLAD");
        var bob = new PlayerInfo(2, "Bob", "Ironclad", "d85a30", "IRONCLAD");
        var s = new RunStats();
        s.BeginFight(1, 1, "f1"); s.RecordDamage(1, Card("A"), 5); s.RecordDamage(2, Card("A"), 9); s.EndFight();
        RecapView v = RecapBuilder.Build(s, new[] { alice, bob }, NoDefense, "h");
        string bobs = v.Overview.Single(r => r.Label == "Bob").ColorHex;
        Check.True(bobs != "d85a30", "Bob is shaded");
        Check.Equal("d85a30", v.Overview.Single(r => r.Label == "Alice").ColorHex, "Alice, first to join, keeps the colour");
        Check.Equal(bobs, v.Timeline.Single(t => t.Label == "Bob").ColorHex, "timeline");
        Check.Equal(bobs, v.Defense.Single(d => d.Label == "Bob").ColorHex, "defense");
        Check.Equal(bobs, v.Sources.Single(x => x.PlayerLabel.StartsWith("Bob")).ColorHex, "sources");
        Check.True(v.Sources.Single(x => x.PlayerLabel.StartsWith("Bob")).Rows.All(r => r.ColorHex == bobs), "source bars");
        Check.True(v.Awards.Where(a => a.PlayerName == "Bob").All(a => a.ColorHex == bobs), "awards");
    }
```

- [ ] **Step 2: Run to see it fail**

Run: `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests PlayerShades`
Expected: build error, `CS0103: The name 'PlayerShades' does not exist`.

- [ ] **Step 3: Write `PlayerShades`**

Create `src/WhoCarried/Core/PlayerShades.cs`:

```csharp
using static WhoCarried.Core.ColourMath;

namespace WhoCarried.Core;

/// <summary>
/// Players who share a colour (two Ironclads) get gentle shades of it, so they can be told apart. The first in join
/// order keeps the true colour; each later one takes the smallest change of lightness, and a small hue nudge away from
/// the run's other colours, that makes it clearly different from everyone. Nothing here names a character or a colour.
/// </summary>
public static class PlayerShades
{
    /// <summary>The lightest a shade may be (HSL), so nothing washes out to white.</summary>
    public const double Ceiling = 0.78;

    /// <summary>How far above the readability floor a shade may go when the floor is already above the ceiling.</summary>
    public const double MinRange = 0.12;

    /// <summary>The largest hue nudge, and the steps tried up to it (degrees).</summary>
    public const double HueCap = 12, HueStep = 3;

    public const double LightStep = 0.01;

    /// <summary>A shade this far (ΔE) from every settled colour is clearly different, and the cheapest such one wins.</summary>
    public const double Apart = 20;

    /// <summary>What a change costs: lightness is weighed per whole unit, hue per degree.</summary>
    public const double LightCost = 100, HueCost = 0.5;

    public static IReadOnlyList<PlayerInfo> Assign(IReadOnlyList<PlayerInfo> players)
    {
        var result = players.ToArray();
        List<IGrouping<string, int>> groups = Enumerable.Range(0, players.Count)
            .GroupBy(i => Normalize(players[i].ColorHex))
            .ToList();
        if (groups.All(g => g.Count() == 1)) return result;

        // Everyone keeping their colour is settled first; shades are then chosen in join order.
        var settled = new List<Rgb>();
        foreach (IGrouping<string, int> g in groups)
            if (Parse(players[g.First()].ColorHex) is Rgb c) settled.Add(Readable(c));

        var hues = groups.Select(g => Parse(g.Key) is Rgb c ? ToHsl(Readable(c)).H : (double?)null).ToList();
        for (int gi = 0; gi < groups.Count; gi++)
        {
            if (Parse(groups[gi].Key) is not Rgb raw) continue; // a colour we can't read is left as it is
            Rgb baseColour = Readable(raw);
            double direction = AwayFrom(ToHsl(baseColour).H, hues.Where((h, i) => i != gi && h != null).Select(h => h!.Value));
            foreach (int i in groups[gi].Skip(1))
            {
                Rgb shade = Pick(baseColour, direction, settled);
                settled.Add(shade);
                result[i] = players[i] with { ColorHex = Hex(shade) };
            }
        }
        return result;
    }

    /// <summary>+1 or -1: the way round the hue circle that leads away from the nearest other hue (+1 with none).</summary>
    private static double AwayFrom(double hue, IEnumerable<double> others)
    {
        double? nearest = null;
        foreach (double other in others)
        {
            double gap = HueGap(hue, other);
            if (nearest == null || Math.Abs(gap) < Math.Abs(nearest.Value)) nearest = gap;
        }
        return nearest > 0 ? -1 : 1;
    }

    private static Rgb Pick(Rgb baseColour, double direction, IReadOnlyList<Rgb> settled)
    {
        (double h, double s, double l) = ToHsl(baseColour);
        double floor = Floor(h, s), top = Math.Max(Ceiling, floor + MinRange);
        Rgb? best = null;
        double bestCost = double.MaxValue, bestNearest = -1;
        bool clear = false;
        for (int li = 0; floor + li * LightStep <= top + 1e-9; li++)
        for (int hi = 0; hi * HueStep <= HueCap + 1e-9; hi++)
        {
            double light = floor + li * LightStep, turn = direction * hi * HueStep;
            Rgb candidate = Round(FromHsl(h + turn, s, light));
            if (Luminance(candidate) < ReadableLuminance) continue; // Accent would lighten it back toward its base
            double nearest = settled.Count == 0 ? double.MaxValue : settled.Min(c => DeltaE(candidate, c));
            double cost = Math.Abs(light - l) * LightCost + Math.Abs(turn) * HueCost;
            if (nearest >= Apart)
            {
                if (!clear || cost < bestCost) (best, bestCost, clear) = (candidate, cost, true);
            }
            else if (!clear && nearest > bestNearest)
            {
                (best, bestNearest) = (candidate, nearest);
            }
        }
        return best ?? Round(baseColour);
    }

    /// <summary>The lowest HSL lightness at which this hue and saturation is readable.</summary>
    private static double Floor(double h, double s)
    {
        double lo = 0, hi = 1;
        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) / 2;
            if (Luminance(FromHsl(h, s, mid)) < ReadableLuminance) lo = mid;
            else hi = mid;
        }
        return hi;
    }
}
```

- [ ] **Step 4: Run the shade tests**

Run: `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests PlayerShades`
Expected: `13/13 passed`. For reference, the rule gave these (second player onward) when it was proved:

| Lobby | Result |
|---|---|
| 2 × `d85a30` | `d85a30 e0985c` |
| 3 × `d85a30` + `ffa518` | `d85a30 e7997f e47872 ffa518` |
| `d85a30` + 3 × `ffa518` | `d85a30 ffa518 ffc818 ffbf5a` |
| 3 × `7a4f2c` | `7a4f2c c6b4a5 af9e80` |

- [ ] **Step 5: Shade the players in `Build`**

In `src/WhoCarried/Core/RecapBuilder.cs`, at the very start of `Build`'s body, before the line `// "Damage dealt" everywhere is HP removed; …`, insert:

```csharp
        // Players sharing a colour (two Ironclads) get shades of it; every view below is built from these.
        players = PlayerShades.Assign(players);

```

Every view model (overview, sources, timeline, defense, decks, debuffs, awards, badges) is built from `players` further down, so this one line reaches them all. `events.log` is written by `Tracker` from `GameReader.Players`, before this, so the log keeps the true colour.

- [ ] **Step 6: Run everything**

Run the build, then all tests.
Expected: `Build succeeded`, and `257/257 passed`. That includes `RecapBuilderTests.PlayersSharingAColourAreShadedInEveryView`.

- [ ] **Checkpoint:** report the task done. No commit.

---

### Task 3: A gap between every segment in the climb

**Files:**
- Modify: `src/WhoCarried/Core/ChartMath.cs` (add one method at the end of the class)
- Modify: `src/WhoCarried/UI/Climb.cs` (a constant under `IgnoreHover`; the segment loop, around line 77)
- Test: `tests/WhoCarried.Tests/ChartMathTests.cs`

**Interfaces:**
- Produces: `ChartMath.SegmentGap(float segment, float gap) → float`, which is `max(0, min(gap, segment / 3))`.

- [ ] **Step 1: Write the failing test**

Add to `ChartMathTests`:

```csharp
    [Test]
    public static void SegmentGapIsFullSizeOnTallSegmentsAndAThirdOnThinOnes()
    {
        Check.Near(2, ChartMath.SegmentGap(40, 2), "a tall segment gives the full gap");
        Check.Near(2, ChartMath.SegmentGap(6, 2), "exactly three gaps tall");
        Check.Near(1, ChartMath.SegmentGap(3, 2), "a thin one gives a third");
        Check.Near(0, ChartMath.SegmentGap(0, 2), "nothing gives nothing");
        Check.Near(0, ChartMath.SegmentGap(-5, 2), "never negative");
    }
```

- [ ] **Step 2: Run to see it fail**

Run: `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests ChartMath`
Expected: build error, `'ChartMath' does not contain a definition for 'SegmentGap'`.

- [ ] **Step 3: Write it**

At the end of `ChartMath`, after `GridCeiling`:

```csharp

    /// <summary>
    /// The gap a stacked segment gives up at its bottom edge, so two segments of one colour don't merge: the full
    /// <paramref name="gap"/>, or a third of a thin segment, so a small share doesn't vanish.
    /// </summary>
    public static float SegmentGap(float segment, float gap) => Math.Max(0, Math.Min(gap, segment / 3));
```

- [ ] **Step 4: Run it**

Run: `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests ChartMath`
Expected: `3/3 passed`.

- [ ] **Step 5: Draw the gaps**

In `src/WhoCarried/UI/Climb.cs`, under `public static bool IgnoreHover { get; set; }`, add:

```csharp

    /// <summary>The gap between two players' segments in a stack, in design pixels.</summary>
    private const float SegmentGap = 2;
```

In the draw loop, replace:

```csharp
                if (stack > 0.5f)
                {
                    foreach (int s in order)
                    {
                        float segment = Value(i, s) / total * stack;
                        if (segment <= 0) continue;
                        chart.DrawRect(new Rect2(k.V(X(i) - colW / 2, y - segment), k.V(colW, segment)), colors[s]);
                        y -= segment;
                    }
```

with:

```csharp
                if (stack > 0.5f)
                {
                    bool bottom = true;
                    foreach (int s in order)
                    {
                        float segment = Value(i, s) / total * stack;
                        if (segment <= 0) continue;
                        // Every segment above the bottom one leaves a gap under it, so two players of one colour don't merge.
                        float gap = bottom ? 0 : ChartMath.SegmentGap(segment, SegmentGap);
                        chart.DrawRect(new Rect2(k.V(X(i) - colW / 2, y - segment), k.V(colW, segment - gap)), colors[s]);
                        y -= segment;
                        bottom = false;
                    }
```

`y` still steps by the whole segment, so the stack's height, its outline (the `DrawRect` with `false` after the loop) and the hover highlight are unchanged. `Climb.cs` already has `using WhoCarried.Core;`. The exported image's climb (`SummaryCard.cs:56`) calls the same `Climb.Create`, and its legend already lists players in the stacks' order. Leave both alone.

- [ ] **Step 6: Build and run everything**

Expected: `Build succeeded`, `258/258 passed`.

- [ ] **Checkpoint:** report the task done. No commit.

---

### Task 4: Names at the ends of the Timeline's lines

**Files:**
- Create: `src/WhoCarried/Core/EndLabels.cs`
- Modify: `src/WhoCarried/UI/TimelineTab.cs`
- Test: `tests/WhoCarried.Tests/EndLabelsTests.cs`

**Interfaces:**
- Produces: `EndLabels.Spread(IReadOnlyList<float> ys, float gap, float top, float bottom) → float[]`, in the order given.

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/EndLabelsTests.cs`:

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>The Timeline's names at its lines' ends: apart, in order, inside the plot.</summary>
public static class EndLabelsTests
{
    [Test]
    public static void LabelsFarApartStayAtTheirLines()
    {
        float[] y = EndLabels.Spread(new[] { 100f, 40f, 200f }, 20, 0, 300);
        Check.Equal("100,40,200", string.Join(",", y), "unchanged, in the order given");
    }

    [Test]
    public static void CloseLabelsArePushedApartInTheLinesOrder()
    {
        float[] y = EndLabels.Spread(new[] { 100f, 105f, 98f }, 20, 0, 300);
        Check.Equal("118,138,98", string.Join(",", y), "the highest line's name stays; the others step down below it");
    }

    [Test]
    public static void LabelsPushedPastTheBottomMoveUpInstead()
    {
        float[] y = EndLabels.Spread(new[] { 295f, 290f, 298f }, 20, 0, 300);
        Check.Equal("280,260,300", string.Join(",", y), "stacked up from the bottom, still in order");
    }

    [Test]
    public static void TooManyLabelsForTheRoomAreSpacedEvenly()
    {
        float[] y = EndLabels.Spread(new[] { 50f, 50f, 50f, 50f, 50f }, 30, 0, 100);
        Check.Equal("0,25,50,75,100", string.Join(",", y), "evenly from top to bottom");
    }

    [Test]
    public static void LabelsStayInsideThePlot()
    {
        float[] y = EndLabels.Spread(new[] { -40f, 500f }, 20, 10, 300);
        Check.Equal("10,300", string.Join(",", y), "clamped to the plot");
    }

    [Test]
    public static void NoLabelsIsFine()
    {
        Check.Equal(0, EndLabels.Spread(Array.Empty<float>(), 20, 0, 100).Length, "empty");
        Check.Equal("50", string.Join(",", EndLabels.Spread(new[] { 50f }, 20, 0, 100)), "one label");
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests EndLabels`
Expected: build error, `The name 'EndLabels' does not exist`.

- [ ] **Step 3: Write `EndLabels`**

Create `src/WhoCarried/Core/EndLabels.cs`:

```csharp
namespace WhoCarried.Core;

/// <summary>Names at the ends of a chart's lines: pushed apart where lines end close together, kept inside the plot.</summary>
public static class EndLabels
{
    /// <summary>
    /// Where each label goes, in the order given: each as near its line's end as it can be, at least
    /// <paramref name="gap"/> from its neighbours, between <paramref name="top"/> and <paramref name="bottom"/>.
    /// When they can't all fit, they're spaced evenly from top to bottom, still in the lines' order.
    /// </summary>
    public static float[] Spread(IReadOnlyList<float> ys, float gap, float top, float bottom)
    {
        int n = ys.Count;
        var placed = new float[n];
        if (n == 0) return placed;
        // Ties keep the order given, so labels don't swap between frames.
        int[] order = Enumerable.Range(0, n).OrderBy(i => ys[i]).ThenBy(i => i).ToArray();
        var y = order.Select(i => Math.Clamp(ys[i], top, bottom)).ToArray();
        for (int i = 1; i < n; i++) y[i] = Math.Max(y[i], y[i - 1] + gap);
        if (y[n - 1] > bottom)
        {
            y[n - 1] = bottom;
            for (int i = n - 2; i >= 0; i--) y[i] = Math.Min(y[i], y[i + 1] - gap);
        }
        if (y[0] < top)
            for (int i = 0; i < n; i++) y[i] = n == 1 ? top : top + i * (bottom - top) / (n - 1);
        for (int i = 0; i < n; i++) placed[order[i]] = y[i];
        return placed;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests EndLabels`
Expected: `6/6 passed`.

- [ ] **Step 5: Give the names room**

In `src/WhoCarried/UI/TimelineTab.cs`:

1. Add `using System.Globalization;` as the first line of the file.
2. Under `private const float ChartW = 1488, ChartH = 548;`, add:

```csharp

    /// <summary>
    /// With two or more players each line ends in its player's name: the room kept for them on the right, their text
    /// size, and the least space between two of them (design pixels).
    /// </summary>
    private const float NameRoom = 150, NameSize = 18, NameGap = 20;

    /// <summary>The chart's right margin: room for the names when there's more than one line.</summary>
    private static float RightMargin(int lines) => lines > 1 ? 16 + NameRoom : 16;

    /// <summary>The text cut with "…" until it fits <paramref name="maxWidth"/> (screen pixels), whole letters at a time.</summary>
    private static string Shorten(Font font, string text, int size, float maxWidth)
    {
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= maxWidth) return text;
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        for (int n = starts.Length - 1; n > 0; n--)
        {
            string cut = text[..starts[n]].TrimEnd() + "…";
            if (font.GetStringSize(cut, HorizontalAlignment.Left, -1, size).X <= maxWidth) return cut;
        }
        return "…";
    }
```

3. At the top of `Chart`, replace:

```csharp
        const float left = 58, right = 16, top = 30, bottom = 30, icon = 28;
        float plotW = width - left - right, plotH = height - top - bottom, baseline = top + plotH;
```

with:

```csharp
        const float left = 58, top = 30, bottom = 30, icon = 28;
        // plotW changes with the number of lines (see Set): names at the ends need room.
        float plotW = width - left - RightMargin(view.Timeline.Count), plotH = height - top - bottom, baseline = top + plotH;
```

4. In `Set`, directly after `current = v;`, add:

```csharp
            plotW = width - left - RightMargin(v.Timeline.Count);
```

`X`, the gridlines, the room icons' `pitch` and the hover snapping all read `plotW`, so they follow.

- [ ] **Step 6: Draw the names**

In the `chart.Draw` handler, replace:

```csharp
                    chart.DrawCircle(points[i], k.U(r), colors[s]);
                }
            }
            float pitch = fights > 1 ? plotW / (fights - 1) : plotW;
```

with:

```csharp
                    chart.DrawCircle(points[i], k.U(r), colors[s]);
                }
            }
            if (colors.Length > 1 && bold != null)
            {
                // Each line ends in its player's name, so lines of close colours can still be told apart.
                int last = fights - 1;
                float[] ends = Enumerable.Range(0, colors.Length).Select(s => Y(Value(last, s))).ToArray();
                float[] placed = EndLabels.Spread(ends, NameGap, top, baseline);
                float x = X(last) + 14;
                for (int s = 0; s < colors.Length && s < current.Timeline.Count; s++)
                {
                    if (Math.Abs(placed[s] - ends[s]) > 2)
                        chart.DrawLine(k.V(X(last) + 8, ends[s]), k.V(x - 3, placed[s]), new Color(colors[s], 0.6f), k.U(1.5f));
                    string name = Shorten(bold, current.Timeline[s].Label, k.F(NameSize), k.U(NameRoom - 20));
                    Vector2 at = k.V(x, placed[s] + NameSize * 0.35f);
                    chart.DrawStringOutline(bold, at, name, HorizontalAlignment.Left, -1, k.F(NameSize), k.F(5), RecapTheme.Ink);
                    chart.DrawString(bold, at, name, HorizontalAlignment.Left, -1, k.F(NameSize), colors[s]);
                }
            }
            float pitch = fights > 1 ? plotW / (fights - 1) : plotW;
```

Notes for the implementer:
- `Value(last, s)` is the animated value, so names glide with their lines.
- `current.Timeline[s].Label` is the player's name (`RecapBuilder` sets it from `p.Name`), and `colors[s]` is in the same order.
- `bold` is `RecapTheme.Bold`, which follows the game's language font, so Chinese names draw.
- The `x` here is in a sibling block to the act-line loop's `x`, which C# allows.
- The legend above the chart stays as it is.

- [ ] **Step 7: Build and run everything**

Expected: `Build succeeded` with 0 warnings, `264/264 passed`.

- [ ] **Checkpoint:** report the task done. No commit.

---

### Task 5: Scoreboard portraits zoomed out partway

**Files:**
- Create: `src/WhoCarried/Core/PictureFit.cs`
- Modify: `src/WhoCarried/UI/Kit.cs` (`Cover`, around line 233)
- Modify: `src/WhoCarried/UI/GameArt.cs` (add `EdgeColours` above `Trim`)
- Modify: `src/WhoCarried/UI/CardFace.cs` (a constant under `Aspect`; the portrait line, around line 62)
- Test: `tests/WhoCarried.Tests/PictureFitTests.cs`

**Interfaces:**
- Produces:
  - `PictureFit.Rect(float X, float Y, float W, float H)`, `PictureFit.Placement(Rect Dest, Rect Source)`, and `PictureFit.Place(float boxW, float boxH, float picW, float picH, float zoom = 1, float focusY = 0.5f) → Placement?`.
  - `Kit.Cover(Texture2D?, float width, float height, float focusY = 0.5f, float zoom = 1, Func<Texture2D, (Color Left, Color Right)>? sides = null)`. Existing callers compile unchanged.
  - `GameArt.EdgeColours(Texture2D) → (Color Left, Color Right)?`.
  - `CardFace.PortraitZoom = 0.8f`.

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/PictureFitTests.cs`:

```csharp
using WhoCarried.Core;
using static WhoCarried.Core.PictureFit;

namespace WhoCarried.Tests;

/// <summary>A portrait in a card's picture window, from filling it (cover) to showing all of it (contain).</summary>
public static class PictureFitTests
{
    // A tall picture in a wide window, like a character portrait in a scoreboard card.
    private const float BoxW = 240, BoxH = 180, PicW = 400, PicH = 500;

    private static string Show(Rect r) => $"{r.X:0.#},{r.Y:0.#} {r.W:0.#}x{r.H:0.#}";

    [Test]
    public static void ZoomOneIsTheOldCover()
    {
        Placement p = Place(BoxW, BoxH, PicW, PicH, zoom: 1, focusY: 0.18f)!.Value;
        Check.Equal("0,0 240x180", Show(p.Dest), "fills the window");
        // Scale 0.6: the window shows 400 × 300 of the picture, 18% of the way down its spare 200.
        Check.Equal("0,36 400x300", Show(p.Source), "crops top and bottom");
    }

    [Test]
    public static void ZoomZeroShowsTheWholePicture()
    {
        Placement p = Place(BoxW, BoxH, PicW, PicH, zoom: 0)!.Value;
        // Scale 0.36: 144 wide, centred, leaving 48 either side.
        Check.Equal("48,0 144x180", Show(p.Dest), "full height, strips at the sides");
        Check.Equal("0,0 400x500", Show(p.Source), "nothing cropped");
    }

    [Test]
    public static void ZoomInBetweenShowsMoreThanCover()
    {
        Placement cover = Place(BoxW, BoxH, PicW, PicH, zoom: 1)!.Value, part = Place(BoxW, BoxH, PicW, PicH, zoom: 0.8f)!.Value;
        Check.True(part.Source.H > cover.Source.H, "more of the picture's height");
        Check.True(part.Dest.W < BoxW && part.Dest.X > 0, "narrower than the window, so strips appear");
        Check.True(Math.Abs(part.Dest.X * 2 + part.Dest.W - BoxW) < 0.01f, "centred");
        Check.Equal(BoxH, part.Dest.H, "still full height");
    }

    [Test]
    public static void ZoomIsClamped()
    {
        Check.Equal(Place(BoxW, BoxH, PicW, PicH, 1), Place(BoxW, BoxH, PicW, PicH, 3), "above 1 is cover");
        Check.Equal(Place(BoxW, BoxH, PicW, PicH, 0), Place(BoxW, BoxH, PicW, PicH, -1), "below 0 is contain");
    }

    [Test]
    public static void NothingToPlaceGivesNull()
    {
        Check.True(Place(0, 180, PicW, PicH) == null, "no box");
        Check.True(Place(BoxW, BoxH, 0, PicH) == null, "no picture");
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests PictureFit`
Expected: build error, `The type or namespace name 'PictureFit' could not be found`.

- [ ] **Step 3: Write `PictureFit`**

Create `src/WhoCarried/Core/PictureFit.cs`:

```csharp
namespace WhoCarried.Core;

/// <summary>
/// Fitting a picture into a box, from CSS's object-fit: cover (zoom 1: fills the box, cropping) to contain (zoom 0:
/// the whole picture, leaving strips). What's left of the box around <see cref="Placement.Dest"/> is the strips.
/// </summary>
public static class PictureFit
{
    public readonly record struct Rect(float X, float Y, float W, float H);

    /// <param name="Dest">Where the picture is drawn, in the box's own coordinates.</param>
    /// <param name="Source">The part of the picture drawn there, in the picture's pixels.</param>
    public readonly record struct Placement(Rect Dest, Rect Source);

    /// <param name="zoom">1 = cover, 0 = contain, in between scales between the two. Clamped to 0–1.</param>
    /// <param name="focusY">Which band is kept when the picture is cropped top and bottom: 0 the top, 1 the bottom.</param>
    /// <returns>Null when the box or the picture has no size.</returns>
    public static Placement? Place(float boxW, float boxH, float picW, float picH, float zoom = 1, float focusY = 0.5f)
    {
        if (boxW <= 0 || boxH <= 0 || picW <= 0 || picH <= 0) return null;
        zoom = Math.Clamp(zoom, 0, 1);
        float cover = Math.Max(boxW / picW, boxH / picH), contain = Math.Min(boxW / picW, boxH / picH);
        float scale = contain + (cover - contain) * zoom;
        float w = Math.Min(boxW, picW * scale), h = Math.Min(boxH, picH * scale);
        var dest = new Rect((boxW - w) / 2, (boxH - h) / 2, w, h);
        float srcW = w / scale, srcH = h / scale;
        var source = new Rect((picW - srcW) / 2, (picH - srcH) * focusY, srcW, srcH);
        return new Placement(dest, source);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests PictureFit`
Expected: `5/5 passed`.

- [ ] **Step 5: `Kit.Cover` takes a zoom and side colours**

In `src/WhoCarried/UI/Kit.cs`, replace the `Cover` doc comment and signature:

```csharp
    /// <summary>A texture cropped to cover a box, like CSS object-fit: cover; <paramref name="focusY"/> picks the band kept.</summary>
    public Control Cover(Texture2D? texture, float width, float height, float focusY = 0.5f)
    {
```

with:

```csharp
    /// <summary>
    /// A texture cropped to cover a box, like CSS object-fit: cover; <paramref name="focusY"/> picks the band kept.
    /// <paramref name="zoom"/> below 1 shows more of the picture (0: all of it, like contain); the strips that leaves
    /// either side are filled with the colours <paramref name="sides"/> gives for the picture.
    /// </summary>
    public Control Cover(Texture2D? texture, float width, float height, float focusY = 0.5f, float zoom = 1,
                         Func<Texture2D, (Color Left, Color Right)>? sides = null)
    {
```

In its `box.Draw` handler, replace:

```csharp
            if (source.X <= 0 || source.Y <= 0) return;
            float scale = Math.Max(size.X / source.X, size.Y / source.Y);
            Vector2 crop = size / scale;
            var origin = new Vector2((source.X - crop.X) * 0.5f, (source.Y - crop.Y) * focusY);
            box.DrawTextureRectRegion(picture, new Rect2(Vector2.Zero, size), new Rect2(origin, crop));
```

with:

```csharp
            if (PictureFit.Place(size.X, size.Y, source.X, source.Y, zoom, focusY) is not PictureFit.Placement place) return;
            if (sides != null && place.Dest.W < size.X - 0.5f)
            {
                (Color left, Color right) = sides(picture);
                box.DrawRect(new Rect2(0, 0, size.X / 2, size.Y), left);
                box.DrawRect(new Rect2(size.X / 2, 0, size.X - size.X / 2, size.Y), right);
            }
            box.DrawTextureRectRegion(picture, ToRect(place.Dest), ToRect(place.Source));
```

After `Cover`'s closing brace, add:

```csharp

    private static Rect2 ToRect(PictureFit.Rect r) => new(r.X, r.Y, r.W, r.H);
```

At zoom 1 the placement is exactly the old maths (`PictureFitTests.ZoomOneIsTheOldCover`), so `DefenseTab` (line 204) and `Kit.Thumb` (line 316) look the same as before. `Kit.cs` already has `using WhoCarried.Core;`.

- [ ] **Step 6: `GameArt.EdgeColours`**

In `src/WhoCarried/UI/GameArt.cs`, directly above the doc comment of `Trim` (`/// An atlas sprite without the transparent margin…`), add:

```csharp
    private static readonly Dictionary<ulong, (Color Left, Color Right)?> Edges = new();

    /// <summary>
    /// The average colour of a picture's outermost two pixel columns on each side, to fill the strips beside a
    /// zoomed-out portrait; read once per texture. Null when its pixels can't be read or its edges are transparent.
    /// </summary>
    public static (Color Left, Color Right)? EdgeColours(Texture2D texture)
    {
        ulong id = texture.GetInstanceId();
        if (Edges.TryGetValue(id, out (Color, Color)? cached)) return cached;
        (Color, Color)? edges = null;
        try
        {
            Image? image = texture.GetImage();
            if (image != null && !image.IsEmpty())
            {
                if (image.IsCompressed()) image.Decompress();
                int w = image.GetWidth(), h = image.GetHeight();
                if (w >= 4 && h > 0 && ColumnAverage(image, 0, h) is Color left && ColumnAverage(image, w - 2, h) is Color right)
                    edges = (left, right);
            }
        }
        catch (Exception)
        {
            // the strips fall back to the card's colour
        }
        Edges[id] = edges;
        return edges;
    }

    /// <summary>Two pixel columns from <paramref name="x0"/>, averaged by opacity; null if they're transparent.</summary>
    private static Color? ColumnAverage(Image image, int x0, int height)
    {
        float r = 0, g = 0, b = 0, weight = 0;
        for (int x = x0; x < x0 + 2; x++)
        for (int y = 0; y < height; y++)
        {
            Color p = image.GetPixel(x, y);
            r += p.R * p.A;
            g += p.G * p.A;
            b += p.B * p.A;
            weight += p.A;
        }
        return weight > height * 0.5f ? new Color(r / weight, g / weight, b / weight) : null;
    }
```

`GetImage` on an `AtlasTexture` gives just its region, and `Decompress` handles VRAM-compressed pictures. Anything else that fails lands in the `catch`, and the caller falls back. The cache is keyed by instance id: a texture the game disposed and reloaded is a new instance, so it gets read again.

- [ ] **Step 7: The card passes the zoom**

In `src/WhoCarried/UI/CardFace.cs`, under `public const float Aspect = HandLayout.Aspect;`, add:

```csharp

    /// <summary>How far the portrait is zoomed: 1 fills the picture window (cropping), 0 shows all of it.</summary>
    public const float PortraitZoom = 0.8f;
```

and replace:

```csharp
            Root.AddChild(k.At(k.Cover(spec.Portrait, pw, ph, 0.18f), px, py));
```

with:

```csharp
            // Zoomed out partway, so more of the portrait shows; the strips beside it take the portrait's own edge colours.
            Color strip = c.Darkened(0.6f);
            Root.AddChild(k.At(k.Cover(spec.Portrait, pw, ph, 0.18f, PortraitZoom,
                picture => GameArt.EdgeColours(picture) ?? (strip, strip)), px, py));
```

`c` is `spec.Color`, already declared at the top of the constructor. Only the scoreboard's `PlayerCard` passes a `Portrait`: award cards pass `Art`, so they don't change. The exported image draws `PlayerCard`, so it gets the zoom too.

- [ ] **Step 8: Build and run everything**

Expected: `Build succeeded` with 0 warnings, `269/269 passed`.

- [ ] **Checkpoint:** report the task done. No commit.

---

### Task 6: The dev preview takes a character list; docs

**Files:**
- Modify: `src/WhoCarried/UI/DevPreview.cs` (the flag comment and a new branch in `Run`, around lines 33–55; `names` in `BuildSample`, around line 305)
- Modify: `README.md` (Developer checks), `docs/README.md` (the spec/plan table), `CHANGELOG.md` (`[Unreleased]`)

**Interfaces:**
- Consumes: `GameReader.CharacterById(string? entry) → CharacterModel?` (exists).
- Produces: `preview.flag` accepts `IRONCLAD,IRONCLAD,IRONCLAD,REGENT`: comma-separated character ids, case-insensitive, up to five, and an optional language after a space as before.

- [ ] **Step 1: The flag**

In `DevPreview.Run`, replace the comment:

```csharp
        // The flag can hold a party size (1–4) to check the hand's other layouts, or "m1", "m2"… for the installed
        // modded characters four at a time (to check their art); four base characters otherwise.
```

with:

```csharp
        // The flag can hold a party size (1–4) to check the hand's other layouts, "m1", "m2"… for the installed
        // modded characters four at a time (to check their art), or a comma-separated list of up to five character
        // ids ("IRONCLAD,IRONCLAD,REGENT") to check players who share a character; four base characters otherwise.
```

and after the `m`-page branch's closing brace, before `else`, insert:

```csharp
        else if (wanted.Contains(','))
        {
            characters = wanted.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => GameReader.CharacterById(id.ToUpperInvariant()))
                .OfType<CharacterModel>()
                .Take(5)
                .ToArray();
            Tracker.Note($"preview: characters {string.Join(", ", characters.Select(c => c.Id.Entry))}");
        }
```

An id the game doesn't know is skipped. If none are known, the existing `if (characters.Length == 0)` line falls back to four base characters.

- [ ] **Step 2: A fifth sample name**

In `BuildSample`, replace `string[] names = { "Ash", "Mika", "Sam", "Jo" };` with:

```csharp
        string[] names = { "Ash", "Mika", "Sam", "Jo", "Wren" };
```

The sample's roles already wrap round the party (`P(i) => players[i % players.Count]`), and `HandLayout.Layout` already lays out five cards.

- [ ] **Step 3: Build and run everything**

Expected: `Build succeeded`, `269/269 passed`.

- [ ] **Step 4: Docs**

`README.md`, under **Developer checks**, replace the `preview.flag` bullet's last sentence, "The file can hold a party size (`1`–`4`).", with:

```markdown
The file can hold a party size (`1`–`4`), or a comma-separated list of up to five character ids, like `IRONCLAD,IRONCLAD,REGENT`, to check players who share a character.
```

`docs/README.md`: add this row at the end of the spec/plan table:

```markdown
| [Telling apart players on the same character](design/specs/2026-09-23-same-character-colours-design.md) | [plan](design/plans/2026-09-23-same-character-colours.md) | Shades of a shared colour, gaps in the climb, names on the Timeline's lines, and the scoreboard's portraits zoomed out |
```

`CHANGELOG.md`: under `## [Unreleased]` → `### Added`, add at the end:

```markdown
- Players on the same character can be told apart. Everyone after the first gets a lighter shade of the character's colour, on every tab, the cards and the exported image. The climb has a gap between each player's part of every bar, and the Timeline names each line at its end.
```

and under `### Changed`, add at the start:

```markdown
- The scoreboard cards show more of each character's portrait.
```

- [ ] **Checkpoint:** report the task done. No commit.

---

### Task 7: Check it in the game, and tune

This needs the owner. **Ask before deploying and before launching**: the game locks the DLL, and they may be playing.

**Files:**
- Possibly modify: the constants in `PlayerShades.cs`, `Climb.cs` (`SegmentGap`), `TimelineTab.cs` (`NameRoom`/`NameSize`/`NameGap`) and `CardFace.cs` (`PortraitZoom`), if the owner wants them changed.

- [ ] **Step 1: Deploy (with the owner's okay)**

With the game closed: `powershell -File tools/deploy.ps1`.

Two copies of the mod can be installed: the Workshop's and the local deploy. The game's mod settings decide which one loads. After launching, check `godot.log`'s `Loading assembly DLL` line: it must name `<game>\mods\WhoCarried\WhoCarried.dll`. Otherwise you're looking at the Workshop build, and the owner has to switch which copy is enabled.

- [ ] **Step 2: Three screenshot runs (with the owner's okay)**

For each flag below, write it to `%APPDATA%\SlayTheSpire2\WhoCarried\preview.flag`, and launch with `E:\Steam\steam.exe -applaunch 2868840`. Running the game's exe directly fails. About 10 s after the menu loads, the preview screenshots every view into that folder as `preview-*.png` and closes.

1. `IRONCLAD,IRONCLAD,IRONCLAD,REGENT`: the hard lobby from the design.
2. `NECROBINDER,NECROBINDER,NECROBINDER,NECROBINDER,NECROBINDER`: the tightest case.
3. `4`: four different characters, to confirm nothing changed for them except the portraits.

- [ ] **Step 3: Look, against the spec**

- `preview-1-scoreboard.png`:
  - the second and third Ironclad cards are lighter reds, with no yellow or purple;
  - the portraits show more of the character, and the side strips blend in (watch Necrobinder's);
  - the climb has a visible gap between every segment.
- `preview-5-timeline.png`:
  - every line ends in its name, and the names don't overlap;
  - the lines' right ends clear the names.
- `preview-9-export.png`: the export's cards and climb match the above.
- Run 3: the colours are exactly as before the change. Compare with `%APPDATA%\SlayTheSpire2\WhoCarried\previews-2026-09-17\` if it holds a scoreboard shot.

Show the owner the screenshots. Tune only what they ask for, then rerun the tests: `PlayerShadesTests` are the limits.

- [ ] **Step 4: Tidy up (with the owner's okay)**

Delete `preview.flag`, or it runs on every launch. Ask whether to add a line to the 1.2.0 change-note draft, `E:\Projects\Who-Carried-workshop\changenote-1.2.0-draft.txt`, which is outside the repo, in the draft's own style. Don't touch `workshop.json` or anything else in that folder.

- [ ] **Checkpoint:** report what the screenshots showed and what, if anything, was tuned. No commit unless the owner asks for one.
