using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>
/// What a mod's armour ate on one hit, per creature. The keys stand in for game creatures, so they're compared by
/// reference: two creatures are never the same entry, however alike they look.
/// </summary>
public static class AbsorbLedgerTests
{
    private sealed class Creature(string name)
    {
        public override string ToString() => name;

        // Two creatures of the same kind must not share an entry.
        public override bool Equals(object? other) => ReferenceEquals(this, other);

        public override int GetHashCode() => 0;
    }

    [Test]
    public static void AddsUpWhatEachCreatureLostSeparately()
    {
        var ledger = new AbsorbLedger();
        var statue = new Creature("statue");
        var other = new Creature("statue");
        ledger.Add(statue, 5m);
        ledger.Add(statue, 3m);
        ledger.Add(other, 2m);
        Check.Equal(8, ledger.Take(statue).Amount, "both hits on the statue");
        Check.Equal(2, ledger.Take(other).Amount, "the other statue's own");
    }

    [Test]
    public static void TakingItCountsItOnce()
    {
        var ledger = new AbsorbLedger();
        var statue = new Creature("statue");
        ledger.Add(statue, 6m);
        Check.Equal(6, ledger.Take(statue).Amount, "the hit");
        Check.Equal(0, ledger.Take(statue).Amount, "nothing left for the next hit");
    }

    [Test]
    public static void NothingRecordedIsNothingTaken()
    {
        var ledger = new AbsorbLedger();
        Check.Equal(0, ledger.Take(new Creature("untouched")).Amount, "never touched");
    }

    [Test]
    public static void WholeHpOnly()
    {
        // HP comes off in whole points, so parts of a point that never became HP aren't counted.
        var ledger = new AbsorbLedger();
        var player = new Creature("player");
        ledger.Add(player, 3.6m);
        ledger.Add(player, 4.6m);
        Check.Equal(8, ledger.Take(player).Amount, "8.2 -> 8");
    }

    [Test]
    public static void OnlyReductionsCount()
    {
        // A layer that adds to a hit rather than eating it mustn't cancel out one that ate.
        var ledger = new AbsorbLedger();
        var player = new Creature("player");
        ledger.Add(player, 7m);
        ledger.Add(player, -4m);
        ledger.Add(player, 0m);
        Check.Equal(7, ledger.Take(player).Amount, "the reduction alone");
    }

    [Test]
    public static void AHitClearsWhatWasLeftForItsTarget()
    {
        var ledger = new AbsorbLedger();
        var player = new Creature("player");
        var enemy = new Creature("enemy");
        ledger.Add(player, 9m);
        ledger.Add(enemy, 4m);
        ledger.Clear(player);
        Check.Equal(0, ledger.Take(player).Amount, "cleared");
        Check.Equal(4, ledger.Take(enemy).Amount, "nobody else touched");
    }

    [Test]
    public static void AFightEndClearsEveryone()
    {
        var ledger = new AbsorbLedger();
        var player = new Creature("player");
        ledger.Add(player, 9m);
        ledger.Clear();
        Check.Equal(0, ledger.Take(player).Amount, "cleared");
    }

    [Test]
    public static void NamesTheLayersThatAte()
    {
        var ledger = new AbsorbLedger();
        var player = new Creature("player");
        ledger.Add(player, 5m, "MARBLED_POWER");
        ledger.Add(player, 2m, "GOLDEN_WISHMAKER");
        ledger.Add(player, 1m, "MARBLED_POWER");
        ledger.Add(player, 1m, null);
        ledger.Add(player, 1m, "");
        (int amount, IReadOnlyList<string> layers) = ledger.Take(player);
        Check.Equal(10, amount, "everything eaten");
        Check.Equal("MARBLED_POWER,GOLDEN_WISHMAKER", string.Join(",", layers), "each layer once, in the order it ate");
    }

    [Test]
    public static void ALayerThatKeepsItsNameToItselfStillCounts()
    {
        var ledger = new AbsorbLedger();
        var player = new Creature("player");
        ledger.Add(player, 8m);
        (int amount, IReadOnlyList<string> layers) = ledger.Take(player);
        Check.Equal(8, amount, "counted");
        Check.Equal(0, layers.Count, "no name to show");
    }
    [Test]
    public static void TheGamesOwnCapsOnAnEnemyArentArmour()
    {
        // Slippery (1 HP per hit) and Hardened Shell (HP per turn) take HP loss away in the same hook as a mod's armour.
        Check.True(!AbsorbLedger.CountsOn(enemy: true, gameContentActed: true), "a game cap on an enemy");
        Check.True(AbsorbLedger.CountsOn(enemy: true, gameContentActed: false), "a mod's armour on an enemy");
        Check.True(AbsorbLedger.CountsOn(enemy: false, gameContentActed: true), "Buffer or Tungsten Rod on a player");
        Check.True(AbsorbLedger.CountsOn(enemy: false, gameContentActed: false), "a mod's armour on a player");
    }
}
