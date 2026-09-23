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
