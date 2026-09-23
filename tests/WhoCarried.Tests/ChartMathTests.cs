using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class ChartMathTests
{
    [Test]
    public static void NiceCeilingRoundsUpToFriendlyAxisValues()
    {
        Check.Equal(1, ChartMath.NiceCeiling(0), "0");
        Check.Equal(1, ChartMath.NiceCeiling(1), "1");
        Check.Equal(5, ChartMath.NiceCeiling(3), "3");
        Check.Equal(10, ChartMath.NiceCeiling(7), "7");
        Check.Equal(100, ChartMath.NiceCeiling(95), "95");
        Check.Equal(200, ChartMath.NiceCeiling(120), "120");
        Check.Equal(2500, ChartMath.NiceCeiling(2400), "2400");
        Check.Equal(5000, ChartMath.NiceCeiling(2600), "2600");
    }

    [Test]
    public static void GridCeilingFitsTightlyAndSplitsIntoFourRoundSteps()
    {
        Check.Equal(4, ChartMath.GridCeiling(0), "0");
        Check.Equal(8, ChartMath.GridCeiling(7), "7");
        Check.Equal(16, ChartMath.GridCeiling(13), "13");
        Check.Equal(100, ChartMath.GridCeiling(95), "95");
        Check.Equal(120, ChartMath.GridCeiling(120), "120");
        Check.Equal(160, ChartMath.GridCeiling(130), "130");
        Check.Equal(1600, ChartMath.GridCeiling(1309), "1309");
        Check.Equal(2400, ChartMath.GridCeiling(2400), "2400");
        Check.Equal(3200, ChartMath.GridCeiling(2600), "2600");
        foreach (int value in new[] { 21, 55, 333, 777, 4321, 98765 })
        {
            int top = ChartMath.GridCeiling(value);
            Check.True(top >= value && top % 4 == 0 && top <= value * 1.34 + 4, $"{value} → {top}");
        }
    }

    [Test]
    public static void SegmentGapIsFullSizeOnTallSegmentsAndAThirdOnThinOnes()
    {
        Check.Near(2, ChartMath.SegmentGap(40, 2), "a tall segment gives the full gap");
        Check.Near(2, ChartMath.SegmentGap(6, 2), "exactly three gaps tall");
        Check.Near(1, ChartMath.SegmentGap(3, 2), "a thin one gives a third");
        Check.Near(0, ChartMath.SegmentGap(0, 2), "nothing gives nothing");
        Check.Near(0, ChartMath.SegmentGap(-5, 2), "never negative");
    }
}
