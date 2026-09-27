namespace WhoCarried.Core;

/// <summary>HP an effect took off a creature by setting it directly, not by a hit (Fur Coat starting enemies at 1).</summary>
public static class HpCut
{
    public static int Amount(int currentHp, decimal newHp) =>
        currentHp <= 0 ? 0 : Math.Max(0, currentHp - (int)Math.Max(0m, Math.Ceiling(newHp)));
}
