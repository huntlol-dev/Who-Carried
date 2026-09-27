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
