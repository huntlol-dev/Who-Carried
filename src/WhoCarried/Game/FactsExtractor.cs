using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>Reads hook arguments into a plain <see cref="DamageFacts"/>. Read-only.</summary>
internal static class FactsExtractor
{
    /// <param name="effect">The live content that started this damage (<see cref="EffectSources"/>); null if none was seen.</param>
    public static DamageFacts Extract(PlayerChoiceContext? context, Creature? dealer, DamageResult result,
                                      Creature target, CardModel? cardSource, AbstractModel? effect = null) =>
        new(
            HpRemoved: result.UnblockedDamage,
            Blocked: result.BlockedDamage,
            TargetIsEnemy: target.IsEnemy,
            TargetPlayerId: PlayerIdOf(target),
            DealerPlayerId: PlayerIdOf(dealer),
            Pet: dealer is { IsPet: true } ? new SourceCandidate(PetSource(dealer), dealer.PetOwner?.NetId) : null,
            Card: cardSource != null ? Candidate(cardSource) : null,
            StackTop: StackTop(context),
            // Only used when there's no card and nothing on the stack: poison ticks, and things firing on their own.
            Fallback: dealer == null ? PoisonFallback(target) : dealer.Player != null ? SelfFire.Recent(dealer.Player.NetId) : null,
            // A dealer already says whose hit it is; what was running only fills in for hits without one.
            Effect: dealer == null && effect != null ? Safe(() => Candidate(effect)) : null,
            TargetHpInfinite: HpInfinite(target));

    /// <summary>Player creature → its player; pet → its owner; anything else → null.</summary>
    public static ulong? PlayerIdOf(Creature? creature) => creature?.Player?.NetId ?? creature?.PetOwner?.NetId;

    /// <summary>The game's infinite health display marks a phase whose HP does not count.</summary>
    public static bool HpInfinite(Creature creature)
    {
        try { return creature.HpDisplay.IsInfinite(); }
        catch (Exception) { return false; }
    }
    public static SourceCandidate Candidate(AbstractModel model)
    {
        string id = model.Id.Entry;
        return model switch
        {
            CardModel card => new(new SourceRef(SourceKind.Card, id, GameText.Title(card.TitleLocString, id)),
                Safe(() => card.Owner?.NetId)),
            PowerModel power => new(new SourceRef(SourceKind.Power, id, GameText.Title(power.Title, id)),
                Safe(() => PlayerIdOf(power.Applier) ?? PlayerIdOf(power.Owner))),
            RelicModel relic => new(new SourceRef(SourceKind.Relic, id, GameText.Title(relic.Title, id)),
                Safe(() => relic.Owner?.NetId)),
            PotionModel potion => new(new SourceRef(SourceKind.Potion, id, GameText.Title(potion.Title, id)),
                Safe(() => potion.Owner?.NetId)),
            OrbModel orb => new(new SourceRef(SourceKind.Orb, id, GameText.Title(orb.Title, id)),
                Safe(() => orb.Owner?.NetId)),
            MonsterModel monster => new(new SourceRef(SourceKind.Monster, id, GameText.Title(monster.Title, id)), null),
            _ => Modded(model, id),
        };
    }

    /// <summary>
    /// A kind of content the game itself doesn't have (a mod's own): its name from a "Title" and its player from an
    /// "Owner", if it has them; otherwise a readable form of its id.
    /// </summary>
    private static SourceCandidate Modded(AbstractModel model, string id)
    {
        Type type = model.GetType();
        string readable = SourceArt.Readable(id, Models.KindWord(model));
        string label = Safe(() => type.GetProperty("Title")?.GetValue(model) is LocString title ? GameText.Title(title, readable) : null) ?? readable;
        ulong? owner = Safe(() => type.GetProperty("Owner")?.GetValue(model) switch
        {
            Player player => (ulong?)player.NetId,
            Creature creature => PlayerIdOf(creature),
            _ => null,
        });
        return new SourceCandidate(new SourceRef(SourceKind.Other, id, label), owner);
    }

    /// <summary>
    /// The model that started this damage: the top of the context's model stack (LastInvolvedModel on every game
    /// version). Some effects (things that fire when you're hit) run in a hook context that names its model without
    /// pushing it, so fall back to that.
    /// </summary>
    public static SourceCandidate? StackTop(PlayerChoiceContext? context)
    {
        AbstractModel? top = Safe(() => context?.LastInvolvedModel)
                             ?? (context as HookPlayerChoiceContext)?.Source;
        return top == null ? null : Candidate(top);
    }

    /// <summary>Poison deals damage with no dealer and a fresh context; credit its applier.</summary>
    private static SourceCandidate? PoisonFallback(Creature target)
    {
        PoisonPower? poison = target.GetPower<PoisonPower>();
        return poison == null ? null : Candidate(poison);
    }

    /// <summary>
    /// The Poison pile a hit came from: no dealer, no card and nothing on the stack (so the fallback credited Poison),
    /// the target has Poison, and nothing else was seen running (Burn ticking on a poisoned enemy isn't Poison). Null
    /// for any other hit.
    /// </summary>
    public static PoisonPower? PoisonTick(DamageFacts facts, Creature? dealer, Creature target, AbstractModel? effect)
    {
        PoisonPower? poison = target.GetPower<PoisonPower>();
        bool? effectIsPoison = effect == null ? null : ReferenceEquals(effect, poison);
        return poison != null && Attribution.IsPileTick(facts, dealer != null, effectIsPoison) ? poison : null;
    }

    private static SourceRef PetSource(Creature pet)
    {
        string name = Safe(() => pet.Name) ?? "";
        string id = pet.Monster?.Id.Entry ?? name;
        return new SourceRef(SourceKind.Pet, id, string.IsNullOrWhiteSpace(name) ? id : name);
    }

    private static T? Safe<T>(Func<T?> get)
    {
        try { return get(); }
        catch (Exception) { return default; }
    }
}
