using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>The recap's own button stone: a chamfered, nine-sliceable tile drawn in whole pixels.</summary>
public static class HewnStoneArtTests
{
    private static HewnStoneArt.Rgba At(HewnStoneArt.Result tile, int x, int y)
    {
        int i = (y * tile.Width + x) * 4;
        return new HewnStoneArt.Rgba(tile.Pixels[i], tile.Pixels[i + 1], tile.Pixels[i + 2], tile.Pixels[i + 3]);
    }

    private static HewnStoneArt.Result Tile()
    {
        HewnStoneArt.Result? drawn = HewnStoneArt.Draw(HewnStoneArt.Palette.Default);
        Check.True(drawn != null, "the default tile draws");
        return drawn!.Value;
    }

    [Test]
    public static void TheTileIsSquareAndLeavesAMiddleToStretch()
    {
        HewnStoneArt.Result tile = Tile();

        Check.Equal(tile.Width, tile.Height, "square");
        Check.True(tile.Margin * 2 < tile.Width, "the nine-slice keeps a middle to stretch");
        Check.Equal(tile.Width * tile.Height * 4, tile.Pixels.Length, "four bytes a pixel");
    }

    [Test]
    public static void TheCornersAreCutAway()
    {
        HewnStoneArt.Result tile = Tile();
        int last = tile.Width - 1;

        foreach ((int x, int y) in new[] { (0, 0), (last, 0), (0, last), (last, last) })
            Check.Equal(HewnStoneArt.Rgba.Clear, At(tile, x, y), $"the corner at {x},{y} is cut");
    }

    [Test]
    public static void TheRimIsLitOnTopAndShadedUnderneath()
    {
        HewnStoneArt.Result tile = Tile();
        int middle = tile.Width / 2, last = tile.Width - 1;

        Check.Equal(HewnStoneArt.Palette.Default.RimLit, At(tile, middle, 0), "the top edge catches the light");
        Check.Equal(HewnStoneArt.Palette.Default.RimShade, At(tile, middle, last), "the bottom edge is in shadow");
    }

    [Test]
    public static void TheFaceIsBevelledInsideTheRim()
    {
        HewnStoneArt.Result tile = Tile();
        int middle = tile.Width / 2, last = tile.Width - 1;

        Check.Equal(HewnStoneArt.Palette.Default.FaceLit, At(tile, middle, HewnStoneArt.Rim), "just under the top rim");
        Check.Equal(HewnStoneArt.Palette.Default.FaceShade, At(tile, middle, last - HewnStoneArt.Rim), "just above the bottom rim");
        Check.Equal(HewnStoneArt.Palette.Default.Face, At(tile, middle, middle), "the face itself");
    }

    [Test]
    public static void ATileTooSmallForItsCornersDrawsNothing()
    {
        Check.True(HewnStoneArt.Draw(HewnStoneArt.Palette.Default, 8) == null, "8px can't hold two 10px corners");
    }

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
}
