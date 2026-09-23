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
