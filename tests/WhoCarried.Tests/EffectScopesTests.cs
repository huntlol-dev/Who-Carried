using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>
/// The scope rules the game patches rely on, with the patches' work done by hand: <see cref="Hook"/> is what the
/// prefix and finalizer do around a model's hook, <see cref="Damage"/> what they do around the damage command.
/// </summary>
public static class EffectScopesTests
{
    private sealed class Effect(string name)
    {
        public override string ToString() => name;
    }

    /// <summary>Enter as the hook is called; leave as it returns its Task (or throws), like a Harmony finalizer.</summary>
    private static Task Hook(EffectScopes s, object effect, Func<Task> body)
    {
        EffectScopes.Frame frame = s.EnterEffect(effect);
        Task? task = null;
        try { return task = body(); }
        finally { s.LeaveEffect(frame, task); }
    }

    /// <summary>A damage command: notes what its hit is credited to at the end, where AfterDamageGiven fires.</summary>
    private static Task Damage(EffectScopes s, List<object?> hits, Task? pause = null, Func<Task>? listener = null)
    {
        EffectScopes.Frame frame = s.EnterDamage();
        Task? task = null;
        try { return task = Deal(s, hits, pause, listener); }
        finally { s.LeaveDamage(frame, task); }
    }

    private static async Task Deal(EffectScopes s, List<object?> hits, Task? pause, Func<Task>? listener)
    {
        if (pause != null) await pause;
        if (listener != null) await listener();
        hits.Add(s.DamageSource);
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Run(Func<Task> test) => test().GetAwaiter().GetResult();

    private static void Same(object? expected, object? actual, string what) =>
        Check.True(ReferenceEquals(expected, actual), $"{what}: expected <{expected?.ToString() ?? "nothing"}> but got <{actual?.ToString() ?? "nothing"}>");

    [Test]
    public static void DamageAnEffectDealsIsCreditedToThatLiveEffect() => Run(async () =>
    {
        var s = new EffectScopes();
        var burn = new Effect("burn");
        var hits = new List<object?>();
        await Hook(s, burn, () => Damage(s, hits));
        Same(burn, hits.Single(), "the effect instance");
    });

    [Test]
    public static void TheEffectCarriesThroughARealAwait() => Run(async () =>
    {
        var s = new EffectScopes();
        var burn = new Effect("burn");
        var hits = new List<object?>();
        TaskCompletionSource gate = Gate();
        Task hook = Hook(s, burn, async () =>
        {
            await gate.Task;
            await Damage(s, hits);
        });
        Check.True(!hook.IsCompleted, "the hook is suspended");
        Same(null, s.Effect, "the caller is back to nothing while the hook waits");
        gate.SetResult();
        await hook;
        Same(burn, hits.Single(), "credited after resuming");
    });

    [Test]
    public static void TwoInstancesOfTheSameEffectKeepTheirOwnHits() => Run(async () =>
    {
        var s = new EffectScopes();
        var onA = new Effect("burn on A");
        var onB = new Effect("burn on B");
        var hitsA = new List<object?>();
        var hitsB = new List<object?>();
        TaskCompletionSource a = Gate(), b = Gate();
        Task first = Hook(s, onA, async () => { await a.Task; await Damage(s, hitsA); });
        Task second = Hook(s, onB, async () => { await b.Task; await Damage(s, hitsB); });
        b.SetResult();
        await second;
        a.SetResult();
        await first;
        Same(onA, hitsA.Single(), "A's");
        Same(onB, hitsB.Single(), "B's");
    });

    [Test]
    public static void AReactionInsideADamageCommandCantTakeTheOuterHit() => Run(async () =>
    {
        var s = new EffectScopes();
        var outer = new Effect("burn");
        var reaction = new Effect("thorns");
        var hits = new List<object?>();
        Func<Task> listener = () => Hook(s, reaction, async () => { await Task.Yield(); await Damage(s, hits); });
        await Hook(s, outer, () => Damage(s, hits, listener: listener));
        Same(reaction, hits[0], "the reaction's own hit");
        Same(outer, hits[1], "the outer hit");
    });

    [Test]
    public static void DamageNoEffectWasSeenStartingStaysUnknown() => Run(async () =>
    {
        var s = new EffectScopes();
        var hits = new List<object?>();
        await Damage(s, hits);
        Same(null, hits.Single(), "nothing running");
        Same(null, s.Effect, "no effect");
    });

    [Test]
    public static void DamageCapturesItsEffectBeforeItsOwnAwait() => Run(async () =>
    {
        var s = new EffectScopes();
        var burn = new Effect("burn");
        var hits = new List<object?>();
        TaskCompletionSource gate = Gate();
        Task hook = Hook(s, burn, () => Damage(s, hits, pause: gate.Task));
        gate.SetResult();
        await hook;
        Same(burn, hits.Single(), "pinned when the command started");
    });

    [Test]
    public static void AnEffectThatFailedDoesntColourTheNextHit() => Run(async () =>
    {
        var s = new EffectScopes();
        var error = new InvalidOperationException("sentinel");
        Exception? seen = null;
        try { await Hook(s, new Effect("async fault"), async () => { await Task.Yield(); throw error; }); }
        catch (Exception e) { seen = e; }
        Same(error, seen, "the effect's own exception");
        try { await Hook(s, new Effect("sync fault"), () => throw error); }
        catch (Exception e) { seen = e; }
        Same(error, seen, "a synchronous throw too");
        var hits = new List<object?>();
        await Damage(s, hits);
        Same(null, hits.Single(), "the next hit");
    });

    [Test]
    public static void WorkLeftRunningAfterAnEffectFinishedIsntCreditedToIt() => Run(async () =>
    {
        var s = new EffectScopes();
        var hits = new List<object?>();
        TaskCompletionSource gate = Gate();
        Task? detached = null;
        await Hook(s, new Effect("done"), () =>
        {
            detached = Task.Run(async () => { await gate.Task; await Damage(s, hits); });
            return Task.CompletedTask;
        });
        gate.SetResult();
        await detached!;
        Same(null, hits.Single(), "finished effect");
    });

    [Test]
    public static void AFinishedChildDoesntFallBackToItsStillRunningParent() => Run(async () =>
    {
        var s = new EffectScopes();
        var hits = new List<object?>();
        TaskCompletionSource gate = Gate();
        Task? detached = null;
        var child = new Effect("child");
        await Hook(s, new Effect("parent"), async () =>
        {
            await Hook(s, child, () =>
            {
                detached = Task.Run(async () => { await gate.Task; await Damage(s, hits); });
                return Task.CompletedTask;
            });
            gate.SetResult();
            await detached!;
        });
        Same(null, hits.Single(), "not the parent");
    });

    [Test]
    public static void WorkFromAnEarlierFightIsntCredited() => Run(async () =>
    {
        var s = new EffectScopes();
        var hits = new List<object?>();
        TaskCompletionSource gate = Gate();
        Task hook = Hook(s, new Effect("last fight"), async () => { await gate.Task; await Damage(s, hits); });
        s.NewFight();
        gate.SetResult();
        await hook;
        Same(null, hits.Single(), "a new fight started");
    });

    [Test]
    public static void ACancelledEffectClearsAndKeepsItsToken() => Run(async () =>
    {
        var s = new EffectScopes();
        using var cancel = new CancellationTokenSource();
        Task hook = Hook(s, new Effect("cancelled"), () => Task.Delay(Timeout.Infinite, cancel.Token));
        cancel.Cancel();
        CancellationToken seen = default;
        try { await hook; }
        catch (OperationCanceledException e) { seen = e.CancellationToken; }
        Check.True(seen == cancel.Token, "the token");
        var hits = new List<object?>();
        await Damage(s, hits);
        Same(null, hits.Single(), "the next hit");
    });

    [Test]
    public static void WorkStartedWithoutItsContextStaysUnknown() => Run(async () =>
    {
        var s = new EffectScopes();
        var hits = new List<object?>();
        await Hook(s, new Effect("no flow"), () =>
        {
            using (ExecutionContext.SuppressFlow()) return Task.Run(() => Damage(s, hits));
        });
        Same(null, hits.Single(), "context not carried");
    });

    [Test]
    public static void TheHookReturnsItsOwnTask() => Run(async () =>
    {
        var s = new EffectScopes();
        TaskCompletionSource gate = Gate();
        Task hook = Hook(s, new Effect("identity"), () => gate.Task);
        Same(gate.Task, hook, "not wrapped");
        gate.SetResult();
        await hook;
    });

    [Test]
    public static void LeavingWithoutEnteringDoesNothing()
    {
        // Another mod's prefix can skip the original before ours runs; the finalizer still does.
        var s = new EffectScopes();
        s.LeaveEffect(null, null);
        s.LeaveDamage(null, null);
        Same(null, s.Effect, "still nothing");
    }

    /// <summary>A direct kill: notes what it counts for as it starts, the way the kill command's prefix does.</summary>
    private static Task Kill(EffectScopes s, List<object?> kills, Task? pause = null, Func<Task>? inside = null)
    {
        EffectScopes.Frame frame = s.EnterKill(out object? effect);
        kills.Add(effect);
        Task? task = null;
        try { return task = Dying(pause, inside); }
        finally { s.LeaveKill(frame, task); }
    }

    private static async Task Dying(Task? pause, Func<Task>? inside)
    {
        if (pause != null) await pause;
        if (inside != null) await inside();
    }

    [Test]
    public static void AKillATurnHookStartsCountsForThatEffect() => Run(async () =>
    {
        var s = new EffectScopes();
        var hallowed = new Effect("hallowed");
        var kills = new List<object?>();
        await Hook(s, hallowed, () => Kill(s, kills));
        Same(hallowed, kills.Single(), "the judging effect");
    });

    [Test]
    public static void AKillWithNothingRunningCountsForNothing() => Run(async () =>
    {
        var s = new EffectScopes();
        var kills = new List<object?>();
        await Kill(s, kills);
        Same(null, kills.Single(), "a card's or a move's kill");
    });

    [Test]
    public static void MinionsDyingWithTheirLeaderCountForNothing() => Run(async () =>
    {
        var s = new EffectScopes();
        var hallowed = new Effect("hallowed");
        var kills = new List<object?>();
        TaskCompletionSource gate = Gate();
        Task hook = Hook(s, hallowed, () => Kill(s, kills, gate.Task, inside: () => Kill(s, kills)));
        gate.SetResult();
        await hook;
        Same(hallowed, kills[0], "the leader");
        Same(null, kills[1], "its minions, killed inside the leader's kill");
    });

    [Test]
    public static void KillsOneAfterAnotherEachCount() => Run(async () =>
    {
        // Doom kills its creatures one at a time, each waited for.
        var s = new EffectScopes();
        var doom = new Effect("doom");
        var kills = new List<object?>();
        TaskCompletionSource gate = Gate();
        Task hook = Hook(s, doom, async () =>
        {
            await Kill(s, kills, gate.Task);
            await Kill(s, kills);
        });
        gate.SetResult();
        await hook;
        Same(doom, kills[0], "the first");
        Same(doom, kills[1], "the second, after the first finished");
    });

    [Test]
    public static void OnlyTurnBoundaryHooksAreWatched()
    {
        foreach (string hook in new[]
                 {
                     "AfterSideTurnStart", "BeforeSideTurnEnd", "BeforeSideTurnEndVeryEarly", "AfterPlayerTurnStartLate",
                     "AfterSideTurnEndLate", "AfterEnergyReset", "AfterEnergyResetLate", "AfterBlockCleared", "BeforeHandDraw",
                     "BeforeHandDrawLate", "BeforeCombatStart", "BeforeCombatStartLate", "BeforeTurnEndOrbTrigger",
                     "AfterTurnStartOrbTrigger",
                 })
            Check.True(EffectScopes.IsTurnBoundaryHook(hook), hook);
        foreach (string hook in new[]
                 {
                     "AfterCardPlayed", "AfterDamageReceived", "AfterTakingExtraTurn", "AfterEnergySpent", "AfterModifyingEnergyGain",
                     "AfterBlockGained", "AfterBlockBroken", "AfterPreventingBlockClear", "AfterModifyingHandDraw", "AfterCardDrawn",
                     "AfterCombatEnd", "AfterCombatVictory",
                 })
            Check.True(!EffectScopes.IsTurnBoundaryHook(hook), hook);
    }
}
