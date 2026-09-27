using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class BlockStripTests
{
    [Test]
    public static void OnlyBlockTheEnemyHadCounts()
    {
        Check.Equal(14, BlockStrip.Removed(14m, 14), "Expose strips all of it");
        Check.Equal(6, BlockStrip.Removed(999_999_999m, 6), "asking for more than there is");
        Check.Equal(3, BlockStrip.Removed(3m, 10), "part of it");
        Check.Equal(0, BlockStrip.Removed(5m, 0), "no block");
        Check.Equal(0, BlockStrip.Removed(0m, 8), "nothing asked");
    }

    [Test]
    public static void AStripReplaysAsBlockRemoved()
    {
        string[] log =
        {
            "player 11 = Moth (The Silent) #5c350f",
            "[F12 A1] fight start: Cultist",
            "[F12 A1] Moth <- Card:EXPOSE (Expose) 0 hp | target CULTIST, blocked 14, dealer player Moth, stack [EXPOSE]",
        };
        PlayerTotals moth = LogReplay.Parse(log).Stats.Get(11)!;
        Check.Equal(14, moth.BlockRemoved, "block removed");
        Check.Equal(0, moth.DamageDealt, "no damage");
    }
}
