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
