namespace WhoCarried.Core;

public static class ChartMath
{
    /// <summary>The smallest "nice" axis maximum (1, 2, 2.5 or 5 × 10ⁿ) that is at least <paramref name="value"/>.</summary>
    public static int NiceCeiling(int value)
    {
        if (value <= 0) return 1;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (double step in new[] { 1, 2, 2.5, 5, 10 })
        {
            double candidate = step * magnitude;
            if (candidate >= value) return (int)Math.Ceiling(candidate);
        }
        return (int)Math.Ceiling(10 * magnitude);
    }

    /// <summary>
    /// A snug axis maximum for a chart with <paramref name="lines"/> gridlines: at most about a third above
    /// <paramref name="value"/>, and each gridline a round number (1,309 → 1,600: 400, 800, 1,200, 1,600).
    /// </summary>
    public static int GridCeiling(int value, int lines = 4)
    {
        if (value <= lines) return lines;
        if (value < 20) return (value + lines - 1) / lines * lines;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (double step in new[] { 1, 1.2, 1.6, 2, 2.4, 3.2, 4, 5, 6, 8, 10 })
        {
            int candidate = (int)Math.Round(step * magnitude);
            if (candidate >= value && candidate % lines == 0) return candidate;
        }
        return (int)Math.Round(10 * magnitude);
    }

    /// <summary>
    /// The gap a stacked segment gives up at its bottom edge, so two segments of one colour don't merge: the full
    /// <paramref name="gap"/>, or a third of a thin segment, so a small share doesn't vanish.
    /// </summary>
    public static float SegmentGap(float segment, float gap) => Math.Max(0, Math.Min(gap, segment / 3));
}
