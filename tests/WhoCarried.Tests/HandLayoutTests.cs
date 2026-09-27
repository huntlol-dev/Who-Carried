using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>The scoreboard's hand of cards must never reach up into the panel's tabs, at rest or hovered.</summary>
public static class HandLayoutTests
{
    private static float Highest(int n, float top, bool hovered)
    {
        (float w, HandLayout.Slot[] slots) = HandLayout.Layout(n, 1200, top);
        return slots.Min(s => HandLayout.Reach(w, s, hovered));
    }

    private const float Limit = HandLayout.TabsBottom + HandLayout.TabGap;

    [Test]
    public static void ReachMatchesWhereTheCardsWereMeasuredOnScreen()
    {
        // Preview at 2560x1440 (1.6 screen pixels per design pixel), four players, cards at rest: the third card's
        // gem topped out at about 200 screen pixels, just under the line below the tabs (124 design = 198 screen).
        (float w, HandLayout.Slot[] slots) = HandLayout.Layout(4, 1200, HandLayout.PreferredTop);
        Check.Near(127.4, HandLayout.Reach(w, slots[2], hovered: false), "third card's gem", tolerance: 0.5);
    }

    [Test]
    public static void AtTheOldHeightHoveredCardsReachIntoTheTabs()
    {
        for (int n = 3; n <= 6; n++)
            Check.True(Highest(n, HandLayout.PreferredTop, hovered: true) < HandLayout.TabsBottom,
                $"{n} players: a hovered card reaches the tab buttons at the old height");
    }

    [Test]
    public static void EveryCardStaysBelowTheTabsEvenWhenHovered()
    {
        for (int n = 1; n <= 6; n++)
        {
            float top = HandLayout.TopClearOfTabs(n, 1200);
            Check.True(Highest(n, top, hovered: false) >= Limit - 0.01f, $"{n} players at rest: top {top}");
            Check.True(Highest(n, top, hovered: true) >= Limit - 0.01f, $"{n} players hovered: top {top}");
        }
    }

    [Test]
    public static void TheHandMovesNoLowerThanItHasTo()
    {
        for (int n = 1; n <= 6; n++)
        {
            float top = HandLayout.TopClearOfTabs(n, 1200);
            Check.True(top >= HandLayout.PreferredTop, $"{n} players: never higher than before");
            Check.Near(Limit, Highest(n, top, hovered: true), $"{n} players: the highest hovered card just clears the tabs",
                tolerance: 0.01);
        }
    }

    [Test]
    public static void OneAndTwoPlayerHandsBarelyMove()
    {
        // Their cards sit low already; the 2-player hand's badges end just above the note under the hand.
        Check.True(HandLayout.TopClearOfTabs(1, 1200) - HandLayout.PreferredTop < 3, "1 player");
        Check.True(HandLayout.TopClearOfTabs(2, 1200) - HandLayout.PreferredTop < 3, "2 players");
    }

    [Test]
    public static void TheCopiedPicturesHandSitsJustBelowItsBar()
    {
        const float line = ShareLayout.BarHeight + ShareLayout.HandGap;
        for (int n = 1; n <= 5; n++)
        {
            float top = HandLayout.TopBelow(line, n, 1200);
            Check.Near(line, Highest(n, top, hovered: false), $"{n} players: the highest card or gem is on the line", tolerance: 0.01);
            Check.True(top < HandLayout.TopClearOfTabs(n, 1200), $"{n} players: higher than in the recap, which has tabs to clear");
        }
    }

    [Test]
    public static void AHoveredCardRisesAndGrowsRoundItsMiddle()
    {
        (float w, HandLayout.Slot[] slots) = HandLayout.Layout(4, 1200, HandLayout.PreferredTop);
        float h = w * HandLayout.Aspect;
        foreach (HandLayout.Slot slot in slots)
        {
            (float X, float Y) rest = HandLayout.Point(w, slot, w / 2, h / 2);
            (float X, float Y) hovered = HandLayout.Point(w, HandLayout.Hovered(w, slot), w / 2, h / 2);
            Check.Near(rest.X, hovered.X, $"tilt {slot.Tilt}: the middle stays put across", tolerance: 0.001);
            Check.Near(rest.Y - HandLayout.HoverRise, hovered.Y, $"tilt {slot.Tilt}: the middle only rises", tolerance: 0.001);
        }
    }

    [Test]
    public static void TheFanItselfIsUnchanged()
    {
        (float w, HandLayout.Slot[] slots) = HandLayout.Layout(4, 1200, 148);
        Check.Equal(292f, w, "card width");
        // Three steps of 256 plus a card is 1060 wide, centred in 1200: the hand starts at 70.
        Check.Equal(new HandLayout.Slot(70f, 162f, -5f, 1.06f), slots[0], "the leader: tilted, raised, bigger");
        Check.Equal(new HandLayout.Slot(582f, 152f, 1.7f, 1f), slots[2], "third card");
    }
}
