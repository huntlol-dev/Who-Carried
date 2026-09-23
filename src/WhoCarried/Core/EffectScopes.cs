namespace WhoCarried.Core;

/// <summary>
/// Which piece of game content is running, carried through its asynchronous work, so that damage it deals with no
/// dealer and no card (a modded power ticking at the start of a turn), or a creature it kills outright (a mod's Doom),
/// can still be credited to it. The game layer enters an effect as a model's hook is called and leaves it as the hook
/// returns its Task; the damage and kill commands enter an operation, which pins the effect running when it started.
/// An effect is any object (the live power or relic instance); nothing here knows the game.
/// Rules: a frame counts only while its hook is still running or the Task it returned is unfinished, and only in the
/// fight it started in. When the innermost frame no longer counts there is no effect: looking past it to an outer one
/// would credit a finished child's leftover work to its parent.
/// </summary>
public sealed class EffectScopes
{
    private readonly AsyncLocal<Frame?> _effects = new(), _damages = new(), _kills = new();
    private int _fight;

    private static readonly string[] TurnBoundaries =
        { "TurnStart", "TurnEnd", "EnergyReset", "BlockCleared", "BeforeHandDraw", "BeforeCombatStart" };

    public sealed class Frame
    {
        private readonly int _fight;
        private Task? _task;
        private volatile bool _running = true;

        internal Frame(object? source, Frame? previous, int fight)
        {
            Source = source;
            Previous = previous;
            _fight = fight;
        }

        public object? Source { get; }

        internal Frame? Previous { get; }

        internal bool ActiveIn(int fight) => _fight == fight && (_running || _task is { IsCompleted: false });

        internal void Returned(Task? task)
        {
            _task = task;
            _running = false;
        }
    }

    /// <summary>The effect running here, or null.</summary>
    public object? Effect => Active(_effects.Value);

    /// <summary>The effect that started the damage operation running here, or null.</summary>
    public object? DamageSource => Active(_damages.Value);

    /// <summary>
    /// The hooks worth watching: the edges of a turn, where effects fire on their own with no card or dealer. Turn
    /// starts and ends (orbs' too), and the steps a player's turn sets up with (energy reset, block cleared, before the
    /// hand draw) or a fight starts with.
    /// </summary>
    public static bool IsTurnBoundaryHook(string name) => TurnBoundaries.Any(part => name.Contains(part, StringComparison.Ordinal));

    public Frame EnterEffect(object effect) => _effects.Value = new Frame(effect, _effects.Value, Fight);

    /// <param name="returned">The Task the hook returned; null if it threw (or wasn't entered).</param>
    public void LeaveEffect(Frame? frame, Task? returned) => Leave(_effects, frame, returned);

    public Frame EnterDamage() => _damages.Value = new Frame(Effect, _damages.Value, Fight);

    public void LeaveDamage(Frame? frame, Task? returned) => Leave(_damages, frame, returned);

    /// <summary>
    /// A direct kill starting (no damage: the creature's HP just goes). <paramref name="effect"/> is what it counts for:
    /// the effect running here, or null if none is, or if this kill happens inside another one (minions dying with
    /// their leader), which a card's kill wouldn't credit either.
    /// </summary>
    public Frame EnterKill(out object? effect)
    {
        bool nested = _kills.Value is { } outer && outer.ActiveIn(Fight);
        effect = nested ? null : Effect;
        return _kills.Value = new Frame(effect, _kills.Value, Fight);
    }

    public void LeaveKill(Frame? frame, Task? returned) => Leave(_kills, frame, returned);

    /// <summary>A fight started: nothing still running from an earlier one counts.</summary>
    public void NewFight() => Interlocked.Increment(ref _fight);

    private int Fight => Volatile.Read(ref _fight);

    private object? Active(Frame? top) => top != null && top.ActiveIn(Fight) ? top.Source : null;

    /// <summary>
    /// Back to the caller's frame straight away: work the hook left pending already carries its own frame, and keeps
    /// it only as long as the returned Task runs.
    /// </summary>
    private static void Leave(AsyncLocal<Frame?> scope, Frame? frame, Task? returned)
    {
        if (frame == null) return;
        frame.Returned(returned);
        scope.Value = frame.Previous;
    }
}
