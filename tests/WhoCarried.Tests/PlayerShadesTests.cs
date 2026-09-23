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
