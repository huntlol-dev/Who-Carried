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
