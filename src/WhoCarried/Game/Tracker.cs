using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>Glue between game events and the pure core. All calls arrive on the game's main thread.</summary>
internal static class Tracker
{
    private static RunStats _stats = new();
    private static readonly RunOrigin _origin = new();
    private static IRunState? _run;
    private static EventLog? _log;
    private static string _dataDir = "";
    private static Dictionary<ulong, string> _names = new();

    public static RunStats Stats => _stats;

    /// <summary>Raised on the main thread after anything counted changes (the open recap refreshes from it).</summary>
    public static event Action? Changed;

    private static void Touch()
    {
        try { Changed?.Invoke(); }
        catch (Exception e) { LogError("Changed", e); }
    }

    public static IRunState? CurrentRun => _run;

    public static string DataDir => _dataDir;

    /// <summary>Appends a free-form line to events.log (exports, preview status).</summary>
    public static void Note(string line) => _log?.Write(line);

    // Not ".json": it used to live under mods/, where the game's mod loader reads every .json as a manifest.
    private static string StatsPath => Path.Combine(_dataDir, "current_run.dat");

    private static string Where => _run == null ? "[--]" : $"[F{_run.TotalFloor} A{_run.CurrentActIndex + 1}]";

    /// <summary>
    /// The mod's files live in the game's own save folder (user://: %APPDATA%\SlayTheSpire2 on Windows,
    /// ~/.local/share/SlayTheSpire2 on Linux, ~/Library/Application Support/SlayTheSpire2 on a Mac), not beside the DLL:
    /// Steam replaces a Workshop mod's folder when it updates, and on a Mac the mods folder is inside the game's app.
    /// Files an older version left in data/ beside the DLL are moved over.
    /// </summary>
    public static void Init(string modDir)
    {
        _dataDir = Path.GetFullPath(Godot.ProjectSettings.GlobalizePath("user://WhoCarried"));
        string oldDir = Path.Combine(modDir, "data");
        (int moved, string? error) = DataFolderMove.Run(oldDir, _dataDir);
        try { Directory.CreateDirectory(_dataDir); }
        catch (Exception e) { Log.Error($"[WhoCarried] can't make {_dataDir}: {e.Message}"); }
        _log = new EventLog(Path.Combine(_dataDir, "events.log"));
        if (moved > 0) _log.Write($"moved {moved} files here from {oldDir}");
        if (error != null) Log.Warn($"[WhoCarried] moving files from {oldDir}: {error}");
    }

    /// <summary>The game is setting up a run: a new one, or one <paramref name="saved"/> from a save.</summary>
    public static void OnRunSetUp(bool saved)
    {
        if (saved) _origin.SetUpSaved();
        else _origin.SetUpNew();
    }

    public static void OnRunStarted(IRunState run)
    {
        _run = run;
        string key = GameReader.RunKey(run);
        bool loaded = _origin.TakeLoadedFromSave(GameReader.ReloadCount());
        RunStats? resumed = RunStatsStore.LoadIfResumable(StatsPath, key, loaded);
        _stats = resumed ?? new RunStats { RunKey = key };
        if (resumed == null)
        {
            // The last run's stats and log are kept beside the new ones (".previous"), in case it's wanted later.
            try
            {
                if (File.Exists(StatsPath)) File.Copy(StatsPath, Path.ChangeExtension(StatsPath, ".previous.dat"), overwrite: true);
            }
            catch (Exception e) { LogError("keep previous stats", e); }
            _log?.Reset(LogReplay.HeaderLine(ModEntry.Version, key, DateTime.Now));
        }
        else
            _log?.Write(LogReplay.ResumedLine(key, _stats.Fights.Count));

        IReadOnlyList<PlayerInfo> players = GameReader.Players(run);
        _names = players.ToDictionary(p => p.NetId, p => p.Name);
        foreach (PlayerInfo p in players)
            _log?.Write($"player {p.NetId} = {p.Name} ({p.Character}) #{p.ColorHex}");
    }

    public static void OnCombatStart(IRunState run, ICombatState? combat)
    {
        _run ??= run;
        Fight.Reset();
        EffectSources.NewFight();
        string label = GameReader.EncounterLabel(combat);
        string room = GameReader.RoomType(run);
        _stats.BeginFight(run.CurrentActIndex + 1, run.TotalFloor, label, room);
        _log?.Write(room.Length > 0 ? $"{Where} fight start: {label} [{room}]" : $"{Where} fight start: {label}");
        Touch();
    }

    public static void OnDamage(PlayerChoiceContext? context, Creature? dealer, DamageResult result, Creature target,
                                CardModel? cardSource)
    {
        AbstractModel? effect = EffectSources.DamageSource;
        DamageFacts facts = FactsExtractor.Extract(context, dealer, result, target, cardSource, effect);
        DebuffBonusTracker.PendingHit? boosted = DebuffBonusTracker.Take(target);
        // A mod's armour between block and HP: protection this hit spent, counted as block on either side.
        (int absorbed, IReadOnlyList<string> layers) = AbsorbLayers.Take(target);
        if (absorbed > 0)
            _log?.Write($"{Where} armour on {Describe(target)} ate {absorbed} hp" +
                        (layers.Count > 0 ? $" ({string.Join(", ", layers)})" : ""));
        if (facts.TargetIsEnemy)
        {
            AttributionResult who = Attribution.Resolve(facts);
            if (effect != null && facts.Effect != null && facts.Card == null && facts.StackTop == null)
                _log?.Write($"{Where} effect source {EffectSources.InstanceId(effect)}{PowerSides(effect)} -> {NameOf(who.PlayerId)}");
            if (PoisonShares(facts, dealer, target, effect) is { } shares)
            {
                foreach ((ulong player, int hp) in shares) RecordHit(player, who.Source, hp, 0, target, dealer, context);
                // The pile's ticks are shared by who owns it; armour they chewed through isn't split, it's one number.
                if (absorbed > 0) _stats.RecordDamage(who.PlayerId, who.Source, 0, absorbed);
            }
            else
            {
                RecordHit(who.PlayerId, who.Source, facts.HpRemoved, facts.Blocked + absorbed, target, dealer, context);
            }
            if (boosted != null) CreditDebuffBonus(boosted, facts, who.PlayerId);
        }
        else if (facts.TargetPlayerId is ulong targetPlayer)
        {
            _stats.RecordBlocked(targetPlayer, facts.Blocked + absorbed);
            // A pet (Osty) taking an enemy's hit, often in its owner's place: its own result, HP it lost (block and
            // any overkill onto the owner come in the owner's result).
            if (target.Player == null && dealer is { IsEnemy: true } && facts.HpRemoved > 0)
            {
                _stats.RecordPetTanked(targetPlayer, facts.HpRemoved);
                _log?.Write($"{Where} " + LogReplay.PetTookLine(NameOf(targetPlayer), target.Monster?.Id.Entry ?? "?",
                    facts.HpRemoved, Describe(dealer)));
            }
        }
        if (target.Player is Player hurt) NoteHp(hurt.NetId, target.CurrentHp, target.MaxHp);
        Touch();
    }

    private static void RecordHit(ulong? player, SourceRef source, int hp, int blocked, Creature target, Creature? dealer,
                                  PlayerChoiceContext? context)
    {
        _stats.RecordDamage(player, source, hp, blocked);
        _log?.Write($"{Where} {NameOf(player)} <- {source.Kind}:{source.Id} ({source.Label}) " +
                    $"{hp} hp | target {Describe(target)}, blocked {blocked}, " +
                    $"dealer {Describe(dealer)}, stack [{StackIds(context)}]");
    }

    /// <summary>
    /// A Poison tick's HP shared by who owns the pile. Null for any other hit, or if the split can't be made (the hit
    /// then goes to the pile's starter, as before).
    /// </summary>
    private static IReadOnlyDictionary<ulong, int>? PoisonShares(DamageFacts facts, Creature? dealer, Creature target,
                                                                 AbstractModel? effect)
    {
        try
        {
            return facts.HpRemoved > 0 && FactsExtractor.PoisonTick(facts, dealer, target, effect) is PoisonPower poison
                ? DebuffBonusTracker.SplitPile(poison, facts.HpRemoved)
                : null;
        }
        catch (Exception e)
        {
            LogError("poison split", e);
            return null;
        }
    }

    /// <summary>Lasting Strength a player took off an enemy (Malaise) is listed as this debuff.</summary>
    private static SourceRef StrengthLoss => new(SourceKind.Power, "STRENGTH_LOSS", WhoCarried.Localization.Loc.Text("WHO_CARRIED.debuffs.strength_loss", GameText.Native("powers", "STRENGTH_POWER.title", "Strength")));

    /// <summary>
    /// Just before block, with the hit's final damage.
    /// Hits on enemies: remember Vulnerable-style boosts for after the hit; if the attacking player is Weak, count the
    /// damage they lost.
    /// Enemy hits on players or pets: credit the HP that Weak and Strength loss on the enemy kept off, and count the
    /// extra HP a Vulnerable-style debuff on the victim cost them. These can arrive as 0 (the game floors damage at
    /// zero), when Strength loss may be what took the whole attack away.
    /// </summary>
    public static void OnBeforeDamage(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        DebuffBonusTracker.BeforeDamage(target, amount, props, dealer, cardSource);
        AbsorbLayers.Starting(target); // this hit's armour is measured from here
        decimal? baseDamage = DebuffBonusTracker.TakeBaseDamage(target, dealer, amount, props);
        if (dealer == null) return;

        if (target.IsEnemy && FactsExtractor.PlayerIdOf(dealer) is ulong attacker)
        {
            if (amount <= 0m) return;
            foreach (DebuffBonusTracker.Amplifier weak in DebuffBonusTracker.DamageMultipliers(dealer, target, amount, props, dealer, cardSource, m => m > 0m && m < 1m))
            {
                int lost = (int)(amount / weak.Multiplier) - (int)amount;
                _stats.RecordDebuffCost(attacker, DebuffRef(weak.Power), RunStats.CostDealt, lost);
            }
            Touch();
            return;
        }

        if (!dealer.IsEnemy || FactsExtractor.PlayerIdOf(target) is not ulong victim) return;
        int block = (target.PetOwner?.Creature ?? target).Block; // pets use their owner's block
        int hp = target.CurrentHp;

        // Weak-style debuffs on the attacker: what they kept off together, shared by how much each shrank the hit.
        IReadOnlyList<DebuffBonusTracker.Amplifier> reducers = DebuffBonusTracker.Reducers(dealer, target, amount, props, cardSource);
        int[] kept = DebuffBonus.PreventedShares(amount, reducers.Select(r => r.Multiplier).ToList(), block, hp);
        for (int i = 0; i < reducers.Count; i++)
        {
            DebuffBonusTracker.Amplifier weak = reducers[i];
            int prevented = kept[i];
            if (prevented <= 0) continue;
            SourceRef debuff = DebuffRef(weak.Power);
            foreach ((ulong player, int share) in DebuffBonusTracker.Share(weak.Power, prevented))
            {
                if (share <= 0) continue;
                _stats.RecordDebuffPrevented(player, debuff, share);
                _log?.Write($"{Where} {NameOf(player)} prevented {share} via {debuff.Id} ({debuff.Label}) | " +
                            $"{Describe(dealer)} hit {NameOf(victim)} for {amount} (x{weak.Multiplier}, block {block})");
            }
        }

        if (props.IsPoweredAttack()) CreditStrengthLoss(target, amount, baseDamage, props, dealer, cardSource, victim, block, hp);

        foreach (DebuffBonusTracker.Amplifier vulnerable in DebuffBonusTracker.DamageMultipliers(target, target, amount, props, dealer, cardSource, m => m > 1m))
        {
            int extra = DebuffBonus.HpDifference(amount, amount / vulnerable.Multiplier, block, hp);
            _stats.RecordDebuffCost(victim, DebuffRef(vulnerable.Power), RunStats.CostTaken, extra);
        }
        Touch();
    }

    /// <summary>
    /// Strength an enemy lost to players (Piercing Wail, Dark Shackles for the turn; Malaise for good) made its
    /// attack weaker: the hit would have been bigger by that Strength times the hit's multipliers. The HP difference is
    /// shared between whoever took the Strength away, in proportion to how much each took; exact ties take turns.
    /// A hit the Strength loss took to nothing is worked out again from where it started (<paramref name="baseDamage"/>),
    /// with the Strength given back.
    /// </summary>
    private static void CreditStrengthLoss(Creature target, decimal amount, decimal? baseDamage, ValueProp props, Creature dealer,
                                           CardModel? cardSource, ulong victim, int block, int hp)
    {
        var parts = new List<(ulong Player, SourceRef Debuff, decimal Strength)>();
        foreach ((PowerModel power, int strength) in DebuffBonusTracker.TemporaryStrengthLoss(dealer))
        {
            IReadOnlyList<(ulong Player, int Weight)> weights = DebuffBonusTracker.Weights(power);
            int total = weights.Sum(w => w.Weight);
            if (total <= 0) continue;
            SourceRef debuff = DebuffRef(power);
            foreach ((ulong player, int weight) in weights) parts.Add((player, debuff, (decimal)strength * weight / total));
        }
        foreach ((ulong player, int strength) in DebuffBonusTracker.LastingStrengthLoss(dealer))
            parts.Add((player, StrengthLoss, strength));
        decimal removed = parts.Sum(p => p.Strength);
        if (removed <= 0m || _run == null) return;

        decimal multiplier = DebuffBonusTracker.DamageMultiplier(_run, target, dealer, props, cardSource);
        decimal? restored = amount <= 0m && baseDamage is decimal start
            ? DebuffBonusTracker.Recalculate(_run, target, dealer, start + removed, props, cardSource)
            : null;
        int prevented = DebuffBonus.StrengthPrevented(amount, removed, multiplier, restored, block, hp);
        int[] shares = DebuffBonusTracker.ShareStrengthLoss(dealer, prevented, parts.Select(p => ((p.Player, p.Debuff.Id), p.Strength)).ToList());
        string without = restored is decimal full ? $", {full} without it" : "";
        for (int i = 0; i < parts.Count; i++)
        {
            if (shares[i] <= 0) continue;
            _stats.RecordDebuffPrevented(parts[i].Player, parts[i].Debuff, shares[i]);
            _log?.Write($"{Where} {NameOf(parts[i].Player)} prevented {shares[i]} via {parts[i].Debuff.Id} ({parts[i].Debuff.Label}) | " +
                        $"{Describe(dealer)} hit {NameOf(victim)} for {amount}, {removed} Strength removed{without} (x{multiplier}, block {block})");
        }
    }

    /// <summary>Just before a creature gains block: count what a Frail-style debuff on a player took off it.</summary>
    public static void OnBeforeBlock(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
    {
        if (amount <= 0m || FactsExtractor.PlayerIdOf(creature) is not ulong player) return;
        IReadOnlyList<DebuffBonusTracker.Amplifier> reducers = DebuffBonusTracker.BlockReducers(creature, amount, props, cardSource);
        if (reducers.Count == 0) return;
        decimal final;
        try { final = Hook.ModifyBlock(creature.CombatState!, creature, amount, props, cardSource, null, out _); }
        catch (Exception) { return; }
        foreach (DebuffBonusTracker.Amplifier frail in reducers)
            _stats.RecordDebuffCost(player, DebuffRef(frail.Power), RunStats.CostBlock, (int)(final / frail.Multiplier) - (int)final);
        Touch();
    }

    /// <summary>A card created mid-fight (Souls, Shivs…), credited to the player who made it.</summary>
    public static void OnCardCreated(CardModel card, Player? creator)
    {
        if (creator == null) return; // enemies adding Dazed or Wounds pass no creator
        string id = card.Id.Entry;
        _stats.RecordCardCreated(creator.NetId, new SourceRef(SourceKind.Card, id, GameText.Title(card.TitleLocString, id)));
        // Made straight into a teammate's piles (Glimpse Beyond's Souls, Largesse): that's a gift too.
        if (card.Owner is Player owner && owner.NetId != creator.NetId)
            Support(creator.NetId, owner.NetId, SupportKind.Cards, 1, id);
        Touch();
    }

    /// <summary>
    /// Help one player gave another. A missing giver is logged, not guessed; gifts to yourself are dropped (see
    /// <see cref="RunStats.RecordSupport"/>).
    /// </summary>
    private static void Support(ulong? giver, ulong recipient, SupportKind kind, int amount, string source)
    {
        if (amount <= 0) return;
        if (giver is not ulong from)
        {
            _log?.Write($"{Where} support: no giver for {amount} {LogReplay.SupportWord(kind)} to {NameOf(recipient)} | {source}");
            return;
        }
        if (from == recipient) return;
        _stats.RecordSupport(from, recipient, kind, amount);
        _log?.Write($"{Where} " + LogReplay.SupportLine(NameOf(from), NameOf(recipient), kind, amount, source));
        Touch();
    }

    /// <summary>Energy landing on a player, after the game's modifiers.</summary>
    public static void OnEnergyGained(Player recipient, decimal amount) =>
        Support(SupportGiver.Find(_run), recipient.NetId, SupportKind.Energy, (int)amount, SupportGiver.Running());

    /// <summary>Block a player or their pet gained, after modifiers: a teammate's card that gave it names the giver.</summary>
    public static void OnBlockGained(Creature creature, decimal amount, CardModel? cardSource)
    {
        if (FactsExtractor.PlayerIdOf(creature) is not ulong recipient) return;
        Support(SupportGiver.Find(_run, cardSource?.Owner?.NetId), recipient, SupportKind.Block, (int)amount,
            cardSource?.Id.Entry ?? SupportGiver.Running());
    }

    /// <summary>
    /// A card drawn outside the normal hand draw, credited to whatever's behind it: a card, relic or power (whichever
    /// the choice context names, pushed or not).
    /// </summary>
    public static void OnCardDrawn(PlayerChoiceContext? context, CardModel card, bool fromHandDraw)
    {
        if (fromHandDraw || card.Owner is not Player recipient) return;
        SourceCandidate? by = FactsExtractor.StackTop(context);
        Support(SupportGiver.Find(_run, by?.OwnerId), recipient.NetId, SupportKind.Draws, 1,
            by?.Source.Id ?? SupportGiver.Running());
    }

    /// <summary>
    /// The extra HP debuff multipliers (Vulnerable, Flanking) added to this hit: shared between the debuffs by how much
    /// each multiplied, then each debuff's part between the players whose stacks of it are still on the enemy. The
    /// hitter's own share is dropped: it's already in their damage.
    /// </summary>
    private static void CreditDebuffBonus(DebuffBonusTracker.PendingHit hit, DamageFacts facts, ulong? hitter)
    {
        int[] bonuses = DebuffBonus.Bonuses(hit.Amount, hit.Amplifiers.Select(a => a.Multiplier).ToList(), facts.Blocked, facts.HpRemoved);
        for (int i = 0; i < hit.Amplifiers.Count; i++)
        {
            DebuffBonusTracker.Amplifier amp = hit.Amplifiers[i];
            int bonus = bonuses[i];
            if (bonus <= 0) continue;
            SourceRef debuff = DebuffRef(amp.Power);
            foreach ((ulong player, int share) in DebuffBonusTracker.Share(amp.Power, bonus))
            {
                if (share <= 0 || player == hitter) continue;
                _stats.RecordDebuffBonus(player, debuff, share);
                _log?.Write($"{Where} {NameOf(player)} +{share} bonus via {debuff.Id} ({debuff.Label}) " +
                            $"on {NameOf(hitter)}'s hit (x{amp.Multiplier}, {facts.HpRemoved} hp)");
            }
        }
    }

    private static SourceRef DebuffRef(PowerModel power)
    {
        string id = power.Id.Entry;
        string label;
        try { label = GameText.Title(power.Title, id); }
        catch (Exception) { label = id; } // some powers build their title from another model
        return new SourceRef(SourceKind.Power, id, label);
    }

    /// <summary>
    /// Doom kills bypass the damage hooks: the game removes the creature's remaining HP with a direct kill. Count that
    /// HP as removed by the Doom power, shared by how much Doom each player added (or to whoever applied it, if that
    /// can't be worked out).
    /// </summary>
    public static void OnDoomKill(IReadOnlyList<Creature> creatures)
    {
        foreach (Creature creature in creatures)
        {
            if (creature == null || !creature.IsEnemy) continue;
            int hp = creature.CurrentHp;
            if (hp <= 0) continue;
            DoomPower? doom = creature.GetPower<DoomPower>();
            SourceCandidate source = doom != null
                ? FactsExtractor.Candidate(doom)
                : new SourceCandidate(new SourceRef(SourceKind.Power, "DOOM_POWER", GameText.Native("powers", "DOOM_POWER.title", "Doom")), null);
            IReadOnlyDictionary<ulong, int>? shares = null;
            try
            {
                if (doom != null) shares = DebuffBonusTracker.SplitPile(doom, hp);
            }
            catch (Exception e)
            {
                LogError("doom split", e);
            }
            if (shares != null)
            {
                foreach ((ulong player, int part) in shares) RecordKill(player, source.Source, part, creature, "doom kill");
            }
            else
            {
                RecordKill(source.OwnerId, source.Source, hp, creature, "doom kill");
            }
            Fight.Now.DoomKilled.Add(creature);
        }
        Touch();
    }

    /// <summary>
    /// Creatures about to be killed outright, while <paramref name="effect"/>'s turn hook runs (Zone the Spire's
    /// Hallowed judging at the end of a turn). Like a Doom kill, the HP goes with no damage hooks, so it's counted as the
    /// effect's damage; <see cref="EffectCredit.ForKill"/> decides who gets it. The enemy's own copy of the effect is
    /// the one whose stacks share it out (each judged enemy has its own Hallowed).
    /// </summary>
    public static void OnDirectKill(IReadOnlyCollection<Creature> creatures, AbstractModel? effect)
    {
        foreach (Creature creature in creatures)
        {
            if (creature == null) continue;
            bool countedAsDoom = Fight.Now.DoomKilled.Remove(creature);
            PowerModel? own = effect is PowerModel power ? creature.Powers.FirstOrDefault(p => p.GetType() == power.GetType()) : null;
            SourceCandidate? source = effect != null ? FactsExtractor.Candidate(own ?? effect) : null;
            var kill = new EffectCredit.Kill(effect != null, creature.IsEnemy, creature.IsAlive, creature.CurrentHp,
                countedAsDoom, source?.OwnerId);
            IReadOnlyDictionary<ulong, int>? credits =
                EffectCredit.ForKill(kill, own != null ? hp => DebuffBonusTracker.ShareKill(own, hp) : null);
            if (credits == null || source == null) continue;
            if (credits.Count == 0)
                _log?.Write($"{Where} direct kill of {Describe(creature)} by {source.Source.Id}: no player behind it");
            foreach ((ulong player, int part) in credits) RecordKill(player, source.Source, part, creature, "direct kill");
        }
        Touch();
    }

    private static void RecordKill(ulong? player, SourceRef source, int hp, Creature creature, string kind)
    {
        _stats.RecordDamage(player, source, hp);
        _log?.Write($"{Where} {NameOf(player)} <- {source.Kind}:{source.Id} ({source.Label}) " +
                    $"{hp} hp | target {Describe(creature)}, {kind}");
    }

    /// <summary>
    /// Debuff stacks landing on a creature, after Artifact and other modifiers. Only debuff-type powers with positive
    /// changes count: buffs, reductions and duration ticks are ignored. Strength-down cards (Piercing Wail, Dark
    /// Shackles) apply their own debuff, which is what gets counted, so the Strength they remove isn't counted twice.
    /// </summary>
    public static void OnPowerChanged(PlayerChoiceContext? context, PowerModel power, decimal amount, Creature? applier,
                                      CardModel? cardSource)
    {
        TrackStrengthLoss(power, amount, applier);
        // A buff one player put on another (Blaze, Fade, Coordinate): only a player applier counts, so self-buffs and
        // relic buffs with no applier never reach the log.
        if (amount > 0 && power.Type == PowerType.Buff && FactsExtractor.PlayerIdOf(power.Owner) is ulong buffed &&
            FactsExtractor.PlayerIdOf(applier) is ulong buffer)
            Support(buffer, buffed, SupportKind.Buffs, (int)Math.Round(amount), power.Id.Entry);
        if (amount <= 0 || power.Type != PowerType.Debuff) return;
        Creature? target = power.Owner;
        int stacks = (int)Math.Round(amount);
        if (target == null || stacks <= 0) return;

        SourceRef debuff = DebuffRef(power);
        ulong? applierPlayer = FactsExtractor.PlayerIdOf(applier);

        if (target.IsEnemy && PassedOn(power, applier, applierPlayer, cardSource, stacks) is var (from, parts))
        {
            // Not counted as applied: its owners applied the debuff it came from, and that's counted already.
            DebuffBonusTracker.AddStacks(power, parts);
            _log?.Write($"{Where} enemy applied {stacks} {debuff.Id} ({debuff.Label}) | target {Describe(target)}, " +
                        $"applier {Describe(applier)}, stack [{StackIds(context)}], passed on from {from.Id.Entry}: " +
                        string.Join(", ", parts.Select(p => $"{NameOf(p.Key)} {p.Value}")));
        }
        else if (target.IsEnemy)
        {
            ulong? who = Attribution.ResolveApplier(applierPlayer, applier != null && applierPlayer == null,
                cardSource != null ? FactsExtractor.Candidate(cardSource) : null, FactsExtractor.StackTop(context));
            if (who is ulong player) _stats.RecordDebuffApplied(player, debuff, stacks);
            // Every stack goes in the queue, a player's or not, so they wear off in the order they went on.
            DebuffBonusTracker.AddStacks(power, who, stacks);
            string by = who != null ? NameOf(who) : applier != null ? "enemy" : "UNATTRIBUTED";
            _log?.Write($"{Where} {by} applied {stacks} {debuff.Id} ({debuff.Label}) | target {Describe(target)}, " +
                        $"applier {Describe(applier)}, stack [{StackIds(context)}]");
        }
        else if (target.Player is { } victim && applierPlayer == null)
        {
            _stats.RecordDebuffReceived(victim.NetId, debuff, stacks);
            _log?.Write($"{Where} {NameOf(victim.NetId)} received {stacks} {debuff.Id} ({debuff.Label}) | " +
                        $"applier {Describe(applier)}");
        }
        Touch();
    }

    /// <summary>
    /// Stacks another debuff on the same enemy hands on as it acts at the start or end of a turn (Zone the Spire's
    /// Hallowed turning half of itself into Doom, naming the enemy as the applier), with the owners they go to;
    /// <see cref="EffectCredit.ForHandedOn"/> decides. Null for any other stacks.
    /// </summary>
    private static (PowerModel From, IReadOnlyDictionary<ulong, int> Parts)? PassedOn(PowerModel power, Creature? applier,
        ulong? applierPlayer, CardModel? cardSource, int stacks)
    {
        PowerModel? from = EffectSources.Running as PowerModel;
        bool otherDebuffActing = from != null && !ReferenceEquals(from, power) && from.Owner == power.Owner &&
                                 from.Type == PowerType.Debuff;
        var landing = new EffectCredit.NewStacks(applierPlayer != null, cardSource != null,
            applier != null && applier != power.Owner, otherDebuffActing);
        IReadOnlyDictionary<ulong, int>? parts = EffectCredit.ForHandedOn(landing, stacks, n => DebuffBonusTracker.PassOn(from!, n));
        return parts != null ? (from!, parts) : null;
    }

    /// <summary>
    /// Keeps the lasting Strength players took off each enemy. Negative Strength from a player counts; a temporary
    /// Strength-down debuff (Piercing Wail) lowers Strength the same way but is counted as itself, so its own amount is
    /// taken back out. The order the two arrive in doesn't matter.
    /// </summary>
    private static void TrackStrengthLoss(PowerModel power, decimal amount, Creature? applier)
    {
        Creature? enemy = power.Owner;
        if (enemy == null || !enemy.IsEnemy || FactsExtractor.PlayerIdOf(applier) is not ulong player) return;
        int change = (int)Math.Round(amount);
        if (power is StrengthPower && change < 0)
        {
            DebuffBonusTracker.AdjustStrengthLoss(enemy, player, -change);
            _stats.AdjustDebuffApplied(player, StrengthLoss, -change);
        }
        else if (power is TemporaryStrengthPower && power.Type == PowerType.Debuff && change > 0)
        {
            DebuffBonusTracker.AdjustStrengthLoss(enemy, player, -change);
            _stats.AdjustDebuffApplied(player, StrengthLoss, -change);
        }
    }

    /// <summary>Close calls ("Clutch"): a player's own HP right after a hit, pets excluded.</summary>
    private static void NoteHp(ulong player, int hp, int max)
    {
        if (hp <= 0)
        {
            Fight.Now.Fallen.Add(player);
            return;
        }
        if (max <= 0) return;
        Dictionary<ulong, (int Hp, int Max)> lows = Fight.Now.Lows;
        if (!lows.TryGetValue(player, out (int Hp, int Max) low) || (long)hp * low.Max < (long)low.Hp * max)
            lows[player] = (hp, max);
    }

    /// <summary>
    /// The fight is over: the lows of everyone still standing count. Used up once counted, since a won run commits the
    /// last fight's lows before that fight's own end comes.
    /// </summary>
    private static void CommitFightLows()
    {
        Fight fight = Fight.Now;
        foreach ((ulong player, (int hp, int max)) in fight.Lows)
            if (!fight.Fallen.Contains(player) && _stats.RecordHp(player, hp, max))
                _log?.Write($"{Where} {NameOf(player)} hp low {hp}/{max}");
        fight.Lows.Clear();
        fight.Fallen.Clear();
    }

    public static void OnCombatEnd(IRunState run)
    {
        CommitFightLows();
        Fight.Reset();
        _stats.EndFight();
        Save();
        _log?.Write($"{Where} fight end, saved");
        Touch();
    }

    /// <param name="saved">The run as the game just saved it; the game works out everyone's badges from it.</param>
    public static void OnRunEnded(bool isVictory, SerializableRun? saved)
    {
        // Heart of the Spire ends a won run a second time as a defeat; the game's own history keeps the win.
        if (_stats.Finished && _stats.Victory == true && !isVictory)
        {
            _log?.Write($"{Where} ignored a later \"defeat\" for a run already won");
            return;
        }
        if (isVictory) CommitFightLows(); // the last fight may end the run before its own end-of-fight
        _stats.EndFight();
        _stats.Finished = true;
        _stats.Victory = isVictory;
        _log?.Write($"{Where} run ended: {(isVictory ? "victory" : "defeat")}");
        RecordBadges(isVictory, saved);
        Save();
        Touch();
    }

    /// <summary>
    /// The same badges the game shows on its end screen and writes to its run history, for every player. Asked of the
    /// game directly, so they're there for guests in co-op too. Abandoned runs get none, as in the game.
    /// </summary>
    private static void RecordBadges(bool isVictory, SerializableRun? saved)
    {
        if (saved == null || RunManager.Instance.IsAbandoned) return;
        foreach (SerializablePlayer player in saved.Players)
        {
            try
            {
                List<EarnedBadge> badges = ScoreUtility.GetBadges(saved, player.NetId, isVictory)
                    .Select(b => new EarnedBadge { Id = b.Id, Rarity = b.Rarity.ToString().ToLowerInvariant() })
                    .ToList();
                _stats.SetBadges(player.NetId, badges);
                foreach (EarnedBadge b in badges) _log?.Write($"{Where} {NameOf(player.NetId)} badge {b.Id} ({b.Rarity})");
            }
            catch (Exception e)
            {
                LogError($"badges for {NameOf(player.NetId)}", e);
            }
        }
    }

    public static void LogError(string where, Exception e)
    {
        _log?.Write($"ERROR in {where}: {e}");
        Log.Error($"[WhoCarried] {where}: {e.Message}");
    }

    private static void Save()
    {
        try { RunStatsStore.Save(_stats, StatsPath); }
        catch (Exception e) { LogError("save", e); }
    }

    private static string NameOf(ulong? playerId) =>
        playerId is ulong id ? _names.GetValueOrDefault(id, id.ToString()) : "UNATTRIBUTED";

    private static string Describe(Creature? c)
    {
        if (c == null) return "null";
        if (c.Player != null) return $"player {NameOf(c.Player.NetId)}";
        if (c.PetOwner != null) return $"pet {c.Monster?.Id.Entry ?? "?"} of {NameOf(c.PetOwner.NetId)}";
        return c.Monster?.Id.Entry ?? "?";
    }

    /// <summary>For a power seen running: which creature it's on and who applied it (", on X, applied by Y").</summary>
    private static string PowerSides(AbstractModel effect) =>
        effect is PowerModel power ? $", on {Describe(power.Owner)}, applied by {Describe(power.Applier)}" : "";

    private static string StackIds(PlayerChoiceContext? context) =>
        string.Join(",", GameCompat.ModelStack(context).Select(m => m.Id.Entry));
}
