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
