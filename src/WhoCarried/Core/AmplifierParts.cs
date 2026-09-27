namespace WhoCarried.Core;

/// <summary>
/// The base game strengthens Vulnerable and Weak from inside their own damage maths: Paper Phrog and Cruelty (the
/// hitter's), Paper Krane (the victim's) and Debilitate (a debuff with its own appliers). These split a debuff's
/// multiplier into additive parts, so each part's HP goes to whoever it belongs to. Null when the parts don't rebuild
/// the multiplier the game itself returned: the caller then credits it all to the debuff, as before.
/// </summary>
public static class AmplifierParts
{
    private const decimal Tolerance = 0.0001m;

    /// <param name="baseMultiplier">Vulnerable's own (1.5).</param>
    /// <param name="afterHitter">After the hitter's amplifiers (Paper Phrog, Cruelty).</param>
    /// <param name="afterDebilitate">After Debilitate on the target.</param>
    /// <param name="gameMultiplier">What the game's own Vulnerable returned for this hit.</param>
    public static (decimal Debuff, decimal Hitter, decimal Debilitate)? Vulnerable(decimal baseMultiplier, decimal afterHitter,
        decimal afterDebilitate, decimal gameMultiplier)
    {
        if (Math.Abs(afterDebilitate - gameMultiplier) > Tolerance || baseMultiplier <= 1m) return null;
        return (baseMultiplier - 1m, Math.Max(0m, afterHitter - baseMultiplier), Math.Max(0m, afterDebilitate - afterHitter));
    }

    /// <param name="baseMultiplier">Weak's own (0.75).</param>
    /// <param name="afterVictim">After the victim's amplifiers (Paper Krane).</param>
    /// <param name="afterDebilitate">After Debilitate on the attacker.</param>
    /// <param name="gameMultiplier">What the game's own Weak returned for this hit.</param>
    public static (decimal Debuff, decimal Victim, decimal Debilitate)? Weak(decimal baseMultiplier, decimal afterVictim,
        decimal afterDebilitate, decimal gameMultiplier)
    {
        if (Math.Abs(afterDebilitate - gameMultiplier) > Tolerance || baseMultiplier >= 1m) return null;
        return (1m - baseMultiplier, Math.Max(0m, baseMultiplier - afterVictim), Math.Max(0m, afterVictim - afterDebilitate));
    }
}
