using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>
/// Who gets credit for what an effect does outside a hit: an enemy it kills outright as it acts at the start or end of a
/// turn (Zone the Spire's Hallowed judging it), and stacks it hands on as it turns into another debuff (Hallowed into Doom).
/// </summary>
public static class EffectCreditTests
{
    private const ulong You = 1, Ash = 2, Jo = 3;

    private static EffectCredit.Kill Enemy(int hp = 25, bool acting = true, bool countedAsDoom = false, ulong? owner = null) =>
        new(acting, IsEnemy: true, IsAlive: true, hp, countedAsDoom, owner);

    private static string Show(IReadOnlyDictionary<ulong, int>? credits) =>
        credits == null ? "not counted" : string.Join(",", credits.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));

    /// <summary>Splits by a debuff's stacks the way the tracker does: each player's still on the enemy.</summary>
    private static Func<int, IReadOnlyDictionary<ulong, int>> StackedBy(params (ulong Player, int Stacks)[] stacks)
    {
        var ledger = new StackLedger();
        foreach ((ulong player, int n) in stacks) ledger.Add(player, n);
        return amount => DebuffBonus.Split(amount, ledger.Active());
    }

    private static IReadOnlyDictionary<ulong, int> NoOne(int _) => new Dictionary<ulong, int>();

    [Test]
    public static void AKillOfACreatureWithInfiniteHpIsNotCounted()
    {
        // A judgement killing an enemy mid-undying-phase would otherwise credit 999,999,999 HP.
        Check.Equal("not counted", Show(EffectCredit.ForKill(Enemy(hp: 999_999_999, owner: You) with { HpInfinite = true },
            StackedBy((You, 6)))), "infinite HP");
        Check.Equal("1:25", Show(EffectCredit.ForKill(Enemy(owner: You), null)), "a normal kill still counts");
    }

    [Test]
    public static void AKillNoEffectStartedIsNotCounted()
    {
        // A card's kill, an enemy's own move, or minions dying inside their leader's kill.
        Check.Equal("not counted", Show(EffectCredit.ForKill(Enemy(acting: false, owner: You), StackedBy((You, 6)))), "no effect acting");
    }

    [Test]
    public static void PlayersPetsAndTheAlreadyDeadAreNotCounted()
    {
        Check.Equal("not counted", Show(EffectCredit.ForKill(Enemy(owner: You) with { IsEnemy = false }, null)), "a player or pet");
        Check.Equal("not counted", Show(EffectCredit.ForKill(Enemy(owner: You) with { IsAlive = false }, null)), "already dead");
        Check.Equal("not counted", Show(EffectCredit.ForKill(Enemy(hp: 0, owner: You), null)), "no HP left to take");
    }

    [Test]
    public static void ADoomKillIsNotCountedTwice()
    {
        Check.Equal("not counted", Show(EffectCredit.ForKill(Enemy(countedAsDoom: true, owner: You), StackedBy((You, 30)))), "Doom's own");
    }

    [Test]
    public static void TheEnemysOwnCopySharesItsHpByWhoStackedIt()
    {
        Check.Equal("1:15,2:10", Show(EffectCredit.ForKill(Enemy(hp: 25), StackedBy((You, 6), (Ash, 4)))), "Hallowed: your 6, Ash's 4");
    }

    [Test]
    public static void WithNoCopyOfItsOwnTheEffectsPlayerGetsItAll()
    {
        // A player's power or relic removing an enemy as their turn ends.
        Check.Equal("1:25", Show(EffectCredit.ForKill(Enemy(hp: 25, owner: You), null)), "the effect's player");
    }

    [Test]
    public static void AnOwnCopyNoPlayerStackedCreditsNobody()
    {
        // An enemy's Fading ending it: its own debuff, which no player applied. Not handed to whoever else is named.
        Check.Equal("", Show(EffectCredit.ForKill(Enemy(owner: You), NoOne)), "counted, as nobody's");
    }

    [Test]
    public static void NoPlayerBehindAKillCountsItAsNobodys()
    {
        Check.Equal("", Show(EffectCredit.ForKill(Enemy(owner: null), null)), "counted, as nobody's");
    }

    [Test]
    public static void TheSplitIsOnlyWorkedOutForAKillThatCounts()
    {
        // Working a split out moves its tie turn along, so a kill that isn't counted mustn't.
        bool asked = false;
        Func<int, IReadOnlyDictionary<ulong, int>> split = hp => { asked = true; return new Dictionary<ulong, int> { [You] = hp }; };
        EffectCredit.ForKill(Enemy(countedAsDoom: true), split);
        EffectCredit.ForKill(Enemy(acting: false), split);
        Check.True(!asked, "not worked out");
    }

    private static EffectCredit.NewStacks Landing(bool player = false, bool card = false, bool anotherCreature = false, bool otherDebuffActing = true) =>
        new(player, card, anotherCreature, otherDebuffActing);

    [Test]
    public static void StacksAnotherDebuffHandsOnGoToItsOwnersByTheirShares()
    {
        // Your 6 and Jo's 3 Hallowed turn 5 of themselves into Doom, naming the enemy as its applier.
        Check.Equal("1:3,3:2", Show(EffectCredit.ForHandedOn(Landing(), 5, StackedBy((You, 6), (Jo, 3)))), "3.33 and 1.67");
    }

    [Test]
    public static void StacksSomeoneAppliedAreNotHandedOn()
    {
        var owners = StackedBy((You, 6));
        Check.Equal("not counted", Show(EffectCredit.ForHandedOn(Landing(player: true), 5, owners)), "a player's own");
        Check.Equal("not counted", Show(EffectCredit.ForHandedOn(Landing(card: true), 5, owners)), "a card's");
        Check.Equal("not counted", Show(EffectCredit.ForHandedOn(Landing(anotherCreature: true), 5, owners)), "another enemy's");
    }

    [Test]
    public static void OnlyAnotherDebuffActingOnTheSameEnemyHandsStacksOn()
    {
        Check.Equal("not counted", Show(EffectCredit.ForHandedOn(Landing(otherDebuffActing: false), 5, StackedBy((You, 6)))), "nothing acting");
    }

    [Test]
    public static void ADebuffNoPlayerOwnsHandsNothingOn()
    {
        Check.Equal("not counted", Show(EffectCredit.ForHandedOn(Landing(), 5, NoOne)), "the stacks stay nobody's");
    }
}
