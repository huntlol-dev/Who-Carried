using WhoCarried.Localization;

namespace WhoCarried.Core;

public readonly record struct AttributionResult(ulong? PlayerId, SourceRef Source);

/// <summary>
/// Decides who gets credit for a hit on an enemy, and under which source.
/// Source precedence: pet, then card, then the model on top of the choice-context stack, then the effect seen running
/// (hits with no dealer), then the fallback (poison, or what fired on its own a moment ago).
/// Player precedence: the dealer's player (pets resolve to their owner), then the chosen source's owner.
/// </summary>
public static class Attribution
{
    public static AttributionResult Resolve(DamageFacts facts)
    {
        if (facts.Pet != null)
        {
            // A pet's hit is listed under the pet, split by what made it attack: "Osty via Unleash".
            SourceCandidate? trigger = facts.Card ?? facts.StackTop;
            SourceRef pet = facts.Pet.Source;
            SourceRef source = trigger == null || trigger.Source.Kind is SourceKind.Pet or SourceKind.Monster || trigger.Source.Id == pet.Id
                ? pet
                : PetVia(pet, trigger.Source);
            return new AttributionResult(facts.DealerPlayerId ?? facts.Pet.OwnerId, source);
        }
        // What was seen running beats the fallback's guess (a debuff the target happens to have).
        SourceCandidate? chosen = facts.Card ?? facts.StackTop ?? facts.Effect ?? facts.Fallback;
        return new AttributionResult(facts.DealerPlayerId ?? chosen?.OwnerId, chosen?.Source ?? SourceRef.Unknown);
    }

    /// <summary>A hit on an enemy whose health bar shows infinite counts for nothing on the attacking side.</summary>
    public static bool Counts(DamageFacts facts) => !(facts.TargetIsEnemy && facts.TargetHpInfinite);
    /// <summary>
    /// Whether a hit on a Poison-style pile's holder can be that pile's tick: nothing explains it (no dealer, no card,
    /// nothing on the stack), and no other effect was seen running when its damage started.
    /// </summary>
    /// <param name="effectIsPile">Whether the effect seen running was the pile itself; null when none was seen.</param>
    public static bool IsPileTick(DamageFacts facts, bool hasDealer, bool? effectIsPile) =>
        !hasDealer && facts.Card == null && facts.StackTop == null && effectIsPile != false;

    /// <summary>Separator in a pet-via-trigger source id: "OSTY&gt;UNLEASH".</summary>
    public const char ViaSeparator = '>';

    public static SourceRef PetVia(SourceRef pet, SourceRef trigger) =>
        new(SourceKind.Pet, $"{pet.Id}{ViaSeparator}{trigger.Id}", Loc.Text("WHO_CARRIED.sources.pet_via", pet.Label, trigger.Label));

    /// <summary>
    /// Who put a debuff on an enemy. A player applier wins (pets already resolve to their owner). An applier that
    /// isn't a player (an enemy) means no player gets it. With no applier at all, the card's owner, then the owner
    /// of whatever started the action.
    /// </summary>
    public static ulong? ResolveApplier(ulong? applierPlayerId, bool applierIsNonPlayer, SourceCandidate? card,
                                        SourceCandidate? stackTop)
    {
        if (applierPlayerId.HasValue) return applierPlayerId;
        if (applierIsNonPlayer) return null;
        return card?.OwnerId ?? stackTop?.OwnerId;
    }
}
