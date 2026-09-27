using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class AmplifierPartsTests
{
    [Test]
    public static void DebilitateDoublesVulnerableAndTakesWhatItAdded()
    {
        // 1.5 -> (no Phrog) 1.5 -> Debilitate 2.0: half the extra is Vulnerable's, half Debilitate's.
        Check.Equal(((decimal, decimal, decimal)?)(0.5m, 0m, 0.5m), AmplifierParts.Vulnerable(1.5m, 1.5m, 2.0m, 2.0m), "parts");
    }

    [Test]
    public static void PaperPhrogIsTheHittersOwnPart()
    {
        Check.Equal(((decimal, decimal, decimal)?)(0.5m, 0.25m, 0m), AmplifierParts.Vulnerable(1.5m, 1.75m, 1.75m, 1.75m), "Phrog only");
        Check.Equal(((decimal, decimal, decimal)?)(0.5m, 0.25m, 0.75m), AmplifierParts.Vulnerable(1.5m, 1.75m, 2.5m, 2.5m), "Phrog then Debilitate");
    }

    [Test]
    public static void PartsThatDontMatchTheGameAreNotUsed()
    {
        // A game version or mod changed Vulnerable: fall back to crediting it all to Vulnerable.
        Check.True(AmplifierParts.Vulnerable(1.5m, 1.5m, 2.0m, 2.2m) == null, "mismatch");
        Check.True(AmplifierParts.Weak(0.75m, 0.75m, 0.5m, 0.4m) == null, "mismatch");
    }

    [Test]
    public static void DebilitateAndPaperKraneShareWeaksReduction()
    {
        Check.Equal(((decimal, decimal, decimal)?)(0.25m, 0m, 0.25m), AmplifierParts.Weak(0.75m, 0.75m, 0.5m, 0.5m), "Debilitate");
        Check.Equal(((decimal, decimal, decimal)?)(0.25m, 0.15m, 0m), AmplifierParts.Weak(0.75m, 0.6m, 0.6m, 0.6m), "Krane");
        // Krane 0.6, then Debilitate: 0.6 - 0.4 = 0.2.
        Check.Equal(((decimal, decimal, decimal)?)(0.25m, 0.15m, 0.4m), AmplifierParts.Weak(0.75m, 0.6m, 0.2m, 0.2m), "both");
    }

    [Test]
    public static void AHitsBonusSplitsByPartsInWholePoints()
    {
        // 10 extra HP from 1.5 -> 2.5 with Phrog: 0.5 / 0.25 / 0.75 of it.
        int[] shares = DebuffBonus.SplitIndexed(10, new[] { 0.5m, 0.25m, 0.75m });
        Check.Equal(10, shares.Sum(), "adds up");
        Check.Equal(3, shares[0], "Vulnerable (3.33)");
        Check.Equal(2, shares[1], "hitter (1.67 rounds up)");
        Check.Equal(5, shares[2], "Debilitate");
    }
}
