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
    [Test]
    public static void FractionalRemovalMatchesTheBlockActuallyLost()
    {
        Check.Equal(1, BlockStrip.Removed(0.5m, 10), "game truncates the remaining block to 9");
        Check.Equal(4, BlockStrip.Removed(3.2m, 10), "6.8 remaining becomes 6");
    }

    [Test]
    public static void LegacyCreditRequiresTheCallingContentToMatchALiveSource()
    {
        var card = new SourceCandidate(new(SourceKind.Card, "EXPOSE", "Expose"), 11);
        var relic = new SourceCandidate(new(SourceKind.Relic, "TEST", "Test relic"), 22);
        var enemy = new SourceCandidate(new(SourceKind.Power, "BURROWED_POWER", "Burrowed"), null);
        Check.Equal(card, BlockStrip.LegacySource(card.Source, null, card), "direct card call");
        Check.Equal(relic, BlockStrip.LegacySource(relic.Source, relic, card), "known turn effect");
        Check.True(BlockStrip.LegacySource(enemy.Source, null, card) == null, "enemy call during another player's card");
        Check.Equal(enemy, BlockStrip.LegacySource(enemy.Source, enemy, card), "known enemy effect remains ownerless");
        Check.True(BlockStrip.LegacySource(null, relic, card) == null, "no proven caller");
    }
}
