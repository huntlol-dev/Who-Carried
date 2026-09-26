using System.Runtime.CompilerServices;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Creatures;
using WhoCarried.Core;
using WhoCarried.Game;

var notes = new List<string>();
Type[] fixtures = { typeof(Reaction), typeof(InheritedReaction), typeof(OtherInheritedReaction), typeof(Quiet), typeof(SoulboundPower) };
int patched = CardCreationSources.Install(fixtures, notes.Add);
Expect(patched == 2, "inherited override patched once; base no-op skipped; real Soulbound patched");
Expect(notes.Single().Contains("2/2"), "installation summary");
var a = new Reaction();
var b = new InheritedReaction();
// These are real AbstractModel overrides, patched through production discovery.
var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
a.Body = async () =>
{
    Expect(ReferenceEquals(CardCreationSources.Running, a), "synchronous scope");
    await gate.Task;
    Expect(ReferenceEquals(CardCreationSources.Running, a), "scope after await");
    b.Body = () => { Expect(ReferenceEquals(CardCreationSources.Running, b), "nested scope"); return Task.CompletedTask; };
    await b.AfterCardGeneratedForCombat(null!, null);
    Expect(ReferenceEquals(CardCreationSources.Running, a), "outer restored");
};
Task pending = a.AfterCardGeneratedForCombat(null!, null);
Expect(CardCreationSources.Running == null, "caller restored while waiting");
gate.SetResult();
await pending;
Expect(CardCreationSources.Running == null, "completed scope gone");

var gateA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var gateB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
a.Body = async () => { await gateA.Task; Expect(ReferenceEquals(CardCreationSources.Running, a), "concurrent A"); };
b.Body = async () => { await gateB.Task; Expect(ReferenceEquals(CardCreationSources.Running, b), "concurrent B"); };
Task taskA = a.AfterCardGeneratedForCombat(null!, null);
Task taskB = b.AfterCardGeneratedForCombat(null!, null);
gateB.SetResult(); await taskB;
gateA.SetResult(); await taskA;
Expect(CardCreationSources.Running == null, "concurrent caller restored");

var unrelated = new EffectScopes();
var unrelatedFrame = unrelated.EnterEffect(new object());
a.Body = () => { Expect(unrelated.Effect != null && ReferenceEquals(CardCreationSources.Running, a), "separate effect scopes"); return Task.CompletedTask; };
await a.AfterCardGeneratedForCombat(null!, null);
unrelated.LeaveEffect(unrelatedFrame, Task.CompletedTask);

// Read production ownership using real game objects without constructing a combat or invoking native UI.
Player owner = Bare<Player>();
typeof(Player).GetField("<NetId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, 1UL);
Creature applier = Bare<Creature>();
typeof(Creature).GetField("<Player>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(applier, owner);
var power = Bare<SoulboundPower>();
Expect(CardCreationSources.ContributorOf(power) == null, "power without applier is unknown");
typeof(PowerModel).GetField("_applier", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(power, applier);
Expect(CardCreationSources.ContributorOf(power) == 1UL, "real power applier resolved");
Expect(CardCreationSources.ContributorOf(a) == null, "unsupported model remains unknown");

// Simulate the inspected global hook ordering: record before invoking listeners.
var seen = new List<ulong?>();
async Task Generated(ulong creator, ulong owner, Reaction? observer = null)
{
    var effect = CardCreationSources.Running;
    seen.Add(CardCreationCredit.Resolve(creator, owner, effect != null,
        ReferenceEquals(effect, a) ? 1UL : null));
    if (observer != null) await observer.AfterCardGeneratedForCombat(null!, null);
}
a.Body = async () => { await Task.Yield(); await Generated(2, 2); };
await Generated(1, 1, a);
Expect(seen.SequenceEqual(new ulong?[] { 1, 1 }), "original then reaction output");

foreach (bool cancel in new[] { false, true })
{
    a.Body = () => cancel ? Task.FromCanceled(new CancellationToken(true)) : Task.FromException(new InvalidOperationException("expected"));
    try { await a.AfterCardGeneratedForCombat(null!, null); } catch (Exception) { }
    Expect(CardCreationSources.Running == null, "fault/cancel cleaned");
}
a.Body = () => throw new InvalidOperationException("expected");
try { await a.AfterCardGeneratedForCombat(null!, null); } catch (InvalidOperationException) { }
Expect(CardCreationSources.Running == null, "synchronous exception cleaned");

var oldGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
a.Body = async () => { await oldGate.Task; Expect(CardCreationSources.Running == null, "old fight invalidated"); };
pending = a.AfterCardGeneratedForCombat(null!, null);
CardCreationSources.NewFight();
oldGate.SetResult();
await pending;
Console.WriteLine("All card creation Harmony checks passed.");

static void Expect(bool value, string message)
{
    if (!value) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}

static T Bare<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

public class Reaction : AbstractModel
{
    public override bool ShouldReceiveCombatHooks => true;
    public Func<Task> Body = () => Task.CompletedTask;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator) => Body();
}
public sealed class InheritedReaction : Reaction;
public sealed class OtherInheritedReaction : Reaction;
public sealed class Quiet : AbstractModel
{
    public override bool ShouldReceiveCombatHooks => true;
}
