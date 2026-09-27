using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>
/// Vulnerable's and Weak's multipliers taken apart into who they belong to, by calling the game's own amplifier methods
/// in the game's order. A part with no power is the hitter's or victim's own and isn't credited to anyone.
/// </summary>
internal static class VanillaAmplifiers
{
    public static IReadOnlyList<(PowerModel? Power, decimal Weight)>? Parts(PowerModel power, Creature target, decimal amount,
        ValueProp props, Creature? dealer, CardModel? cardSource, decimal gameMultiplier)
    {
        try
        {
            return power switch
            {
                VulnerablePower v => Vulnerable(v, target, props, dealer, cardSource, gameMultiplier),
                WeakPower w => Weak(w, target, props, dealer, cardSource, gameMultiplier),
                _ => null,
            };
        }
        catch (Exception) { return null; }
    }

    private static IReadOnlyList<(PowerModel?, decimal)>? Vulnerable(VulnerablePower power, Creature target, ValueProp props,
        Creature? dealer, CardModel? cardSource, decimal game)
    {
        decimal start = power.DynamicVars["DamageIncrease"].BaseValue, now = start;
        if (dealer != null)
        {
            if (dealer.Player?.GetRelic<PaperPhrog>() is { } phrog) now = phrog.ModifyVulnerableMultiplier(target, now, props, dealer, cardSource);
            if ((dealer.GetPower<CrueltyPower>() ?? dealer.PetOwner?.Creature.GetPower<CrueltyPower>()) is { } cruelty)
                now = cruelty.ModifyVulnerableMultiplier(target, now, props, dealer, cardSource);
        }
        decimal afterHitter = now;
        DebilitatePower? debilitate = target.GetPower<DebilitatePower>();
        if (debilitate != null) now = debilitate.ModifyVulnerableMultiplier(target, now, props, dealer, cardSource);
        if (AmplifierParts.Vulnerable(start, afterHitter, now, game) is not { } parts) return null;
        return new (PowerModel?, decimal)[] { (power, parts.Debuff), (null, parts.Hitter), (debilitate, parts.Debilitate) };
    }

    private static IReadOnlyList<(PowerModel?, decimal)>? Weak(WeakPower power, Creature target, ValueProp props,
        Creature? dealer, CardModel? cardSource, decimal game)
    {
        if (dealer == null) return null;
        decimal start = power.DynamicVars["DamageDecrease"].BaseValue, now = start;
        if (target.Player?.GetRelic<PaperKrane>() is { } krane) now = krane.ModifyWeakMultiplier(target, now, props, dealer, cardSource);
        decimal afterVictim = now;
        DebilitatePower? debilitate = dealer.GetPower<DebilitatePower>();
        if (debilitate != null) now = debilitate.ModifyWeakMultiplier(dealer, now, props, dealer, cardSource);
        if (AmplifierParts.Weak(start, afterVictim, now, game) is not { } parts) return null;
        return new (PowerModel?, decimal)[] { (power, parts.Debuff), (null, parts.Victim), (debilitate, parts.Debilitate) };
    }
}
