using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class AttributionTests
{
    private static readonly SourceRef Strike = new(SourceKind.Card, "STRIKE_IRONCLAD", "Strike");
    private static readonly SourceRef Osty = new(SourceKind.Pet, "OSTY", "Osty");
    private static readonly SourceRef Ashes = new(SourceKind.Relic, "CHARONS_ASHES", "Charon's Ashes");
    private static readonly SourceRef Poison = new(SourceKind.Power, "POISON_POWER", "Poison");

    private static readonly SourceRef Burn = new(SourceKind.Power, "HEXTECH_BURN_POWER", "Burn");

    private static DamageFacts Facts(ulong? dealer = null, SourceCandidate? pet = null, SourceCandidate? card = null,
                                     SourceCandidate? stack = null, SourceCandidate? fallback = null,
                                     SourceCandidate? effect = null, bool infinite = false) =>
        new(HpRemoved: 7, Blocked: 0, TargetIsEnemy: true, TargetPlayerId: null, DealerPlayerId: dealer,
            Pet: pet, Card: card, StackTop: stack, Fallback: fallback, Effect: effect, TargetHpInfinite: infinite);

    [Test]
    public static void AHitOnAnEnemyWithInfiniteHpDoesNotCount()
    {
        // The Waterfall Giant's wind-up after it's "killed": 999,999,999 HP and a purple bar.
        Check.True(!Attribution.Counts(Facts(dealer: 1, card: new(Strike, 1), infinite: true)), "infinite enemy");
        Check.True(Attribution.Counts(Facts(dealer: 1, card: new(Strike, 1))), "a normal enemy counts");
        Check.True(Attribution.Counts(Facts(dealer: 1, infinite: true) with { TargetIsEnemy = false, TargetPlayerId = 2 }),
            "only enemies are affected");
    }

    [Test]
    public static void AnOrbFiringOnItsOwnIsCreditedToTheOrb()
    {
        // Lightning's end-of-turn passive: the Defect hits with no card and nothing on the stack.
        var lightning = new SourceRef(SourceKind.Orb, "LIGHTNING_ORB", "Lightning");
        AttributionResult result = Attribution.Resolve(Facts(dealer: 2, fallback: new(lightning, 2)));
        Check.Equal(lightning, result.Source, "the orb");
        Check.Equal<ulong?>(2, result.PlayerId, "the Defect");
        AttributionResult played = Attribution.Resolve(Facts(dealer: 2, card: new(Strike, 2), fallback: new(lightning, 2)));
        Check.Equal(Strike, played.Source, "a card still wins over a recent orb");
    }

    [Test]
    public static void DebuffGoesToThePlayerWhoAppliedIt()
    {
        Check.Equal<ulong?>(2, Attribution.ResolveApplier(2, false, new(Strike, 1), new(Ashes, 1)), "applier wins");
    }

    [Test]
    public static void DebuffFromAnEnemyGoesToNobody()
    {
        Check.True(Attribution.ResolveApplier(null, true, null, new(Ashes, 1)) == null, "enemy applier, no fallback");
    }

    [Test]
    public static void DebuffWithNoApplierFallsBackToCardThenStack()
    {
        Check.Equal<ulong?>(1, Attribution.ResolveApplier(null, false, new(Strike, 1), new(Ashes, 3)), "card owner");
        Check.Equal<ulong?>(3, Attribution.ResolveApplier(null, false, null, new(Ashes, 3)), "stack owner");
        Check.True(Attribution.ResolveApplier(null, false, null, null) == null, "nothing to go on");
    }

    [Test]
    public static void CardDamageGoesToDealerUnderTheCard()
    {
        AttributionResult r = Attribution.Resolve(Facts(dealer: 1, card: new(Strike, 1), stack: new(Strike, 1)));
        Check.Equal<ulong?>(1, r.PlayerId, "player");
        Check.Equal(Strike, r.Source, "source");
    }

    [Test]
    public static void PetDamageIsListedUnderThePetSplitByTheCardThatCausedIt()
    {
        AttributionResult r = Attribution.Resolve(Facts(dealer: 2, pet: new(Osty, 2), card: new(Strike, 2)));
        Check.Equal<ulong?>(2, r.PlayerId, "player");
        Check.Equal("Pet:OSTY>STRIKE_IRONCLAD", r.Source.Key, "key");
        Check.Equal("Osty via Strike", r.Source.Label, "label");
    }

    [Test]
    public static void PetDamageFromTheStackTopIsSplitToo()
    {
        AttributionResult r = Attribution.Resolve(Facts(dealer: 2, pet: new(Osty, 2), stack: new(Ashes, 2)));
        Check.Equal("Osty via Charon's Ashes", r.Source.Label, "relic trigger");
    }

    [Test]
    public static void PetDamageWithNoTriggerIsJustThePet()
    {
        Check.Equal(Osty, Attribution.Resolve(Facts(dealer: 2, pet: new(Osty, 2))).Source, "no trigger");
        Check.Equal(Osty, Attribution.Resolve(Facts(dealer: 2, pet: new(Osty, 2), stack: new(Osty, 2))).Source, "its own turn");
    }

    [Test]
    public static void RelicDamageComesFromTheModelStack()
    {
        AttributionResult r = Attribution.Resolve(Facts(dealer: 1, stack: new(Ashes, 1)));
        Check.Equal<ulong?>(1, r.PlayerId, "player");
        Check.Equal(Ashes, r.Source, "source");
    }

    [Test]
    public static void PoisonFallbackCreditsTheApplier()
    {
        AttributionResult r = Attribution.Resolve(Facts(fallback: new(Poison, 3)));
        Check.Equal<ulong?>(3, r.PlayerId, "player");
        Check.Equal(Poison, r.Source, "source");
    }

    [Test]
    public static void DealerBeatsSourceOwnerWhenTheyDiffer()
    {
        AttributionResult r = Attribution.Resolve(Facts(dealer: 1, stack: new(Poison, 2)));
        Check.Equal<ulong?>(1, r.PlayerId, "player");
    }

    [Test]
    public static void KnownPlayerWithNoSourceGetsUnknownSource()
    {
        AttributionResult r = Attribution.Resolve(Facts(dealer: 1));
        Check.Equal<ulong?>(1, r.PlayerId, "player");
        Check.Equal(SourceRef.Unknown, r.Source, "source");
    }

    [Test]
    public static void NothingKnownIsUnattributedAndUnknown()
    {
        AttributionResult r = Attribution.Resolve(Facts());
        Check.Equal<ulong?>(null, r.PlayerId, "player");
        Check.Equal(SourceRef.Unknown, r.Source, "source");
    }

    [Test]
    public static void ARunningEffectCreditsAHitWithNothingElseToGoOn()
    {
        // Hextech Burn ticking at turn start: no dealer, no card, a fresh context. The live power was running.
        AttributionResult r = Attribution.Resolve(Facts(effect: new(Burn, 1)));
        Check.Equal(Burn, r.Source, "the effect");
        Check.Equal<ulong?>(1, r.PlayerId, "whoever applied it");
    }

    [Test]
    public static void ARunningEffectBeatsThePoisonGuess()
    {
        // Burn ticking on a poisoned enemy: the target's Poison is only a guess; the running Burn is what happened.
        AttributionResult r = Attribution.Resolve(Facts(effect: new(Burn, 1), fallback: new(Poison, 2)));
        Check.Equal(Burn, r.Source, "Burn, not Poison");
        Check.Equal<ulong?>(1, r.PlayerId, "Burn's player");
    }

    [Test]
    public static void ACardOrTheStackStillBeatsARunningEffect()
    {
        Check.Equal(Strike, Attribution.Resolve(Facts(card: new(Strike, 2), effect: new(Burn, 1))).Source, "card");
        Check.Equal(Ashes, Attribution.Resolve(Facts(stack: new(Ashes, 2), effect: new(Burn, 1))).Source, "stack");
    }

    [Test]
    public static void AnEnemysRunningEffectIsNamedButCreditsNobody()
    {
        AttributionResult r = Attribution.Resolve(Facts(effect: new(Burn, null)));
        Check.Equal(Burn, r.Source, "named");
        Check.Equal<ulong?>(null, r.PlayerId, "no player");
    }

    [Test]
    public static void OnlyAHitNothingExplainsCanBeAPoisonTick()
    {
        Check.True(Attribution.IsPileTick(Facts(), hasDealer: false, effectIsPile: null), "nothing seen: as before");
        Check.True(Attribution.IsPileTick(Facts(effect: new(Poison, 1)), hasDealer: false, effectIsPile: true), "Poison itself was running");
        Check.True(!Attribution.IsPileTick(Facts(effect: new(Burn, 1)), hasDealer: false, effectIsPile: false), "Burn on a poisoned enemy");
        Check.True(!Attribution.IsPileTick(Facts(card: new(Strike, 1)), hasDealer: false, effectIsPile: null), "a card");
        Check.True(!Attribution.IsPileTick(Facts(stack: new(Ashes, 1)), hasDealer: false, effectIsPile: null), "the stack");
        Check.True(!Attribution.IsPileTick(Facts(), hasDealer: true, effectIsPile: null), "a dealer");
    }
}
