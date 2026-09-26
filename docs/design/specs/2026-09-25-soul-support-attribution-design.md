# Card creation in Support and Soulbound attribution

Date: 2026-09-25. Branch: `codex/soul-support-attribution`.
Status: implemented 2026-09-26; automated validation passed. In-game and visual acceptance remain pending explicit authorization.

## Intent and success

Players should find Souls created in Support and see the portion they gave teammates. A Soul created by Alice's Soulbound on Bob should credit Alice as its contributor and Bob as its recipient. This fixes Who Carried's accounting; it does not change Soulbound gameplay.

The user approved separating all Souls created from Souls given to teammates and requested a spec and plan together. The design extends the existing generic card counters, so Shivs and modded generated cards remain visible too. This is a design choice, not a request for additional mechanics or a new carry score.

## Evidence and current behavior

The live investigation inspected the installed 1.2.0 assembly, game assembly, `events.log`, and `current_run.dat`. The saved snapshot at 16:07 on floor 25 contained Huntlol: 93 Souls, Evening: 9 Souls, and zero CardsGiven for both. The log later advanced beyond that snapshot. These numbers demonstrate that creation is recorded; they do not prove how each of the nine Souls originated.

- `Game/Patches.cs` prefixes `Hook.AfterCardGeneratedForCombat` and calls `Tracker.OnCardCreated`.
- `Tracker.OnCardCreated` records the supplied creator, ignores null creators, and counts a gift only when creator and card owner differ.
- The installed `SoulboundPower.AfterCardGeneratedForCombat` reacts to a Soul created by its applier and generates new Souls with both owner and creator set to the buff recipient. This explains the attribution defect.
- `Core/RecapBuilder.cs` places creation in `SourcesView.CreatedCards`. `UI/SourcesTab.cs` renders it after all damage rows in a scroll area.
- Support and the exported image currently mean only gifts to teammates. `RecapView.HasSupport` controls those gift sections.
- Creation events are not currently logged, so replay cannot reconstruct existing creation totals from old logs.

## Options and decision

1. **Move generic creation into Support and observe generation reactions (selected).** Reuses recorded card identities and the tested async scope lifecycle. Covers Soulbound without naming it in production attribution rules. Requires narrow new instrumentation and integration checks.
2. **Add a Soul-only counter and a Soulbound-specific patch.** Smaller initially, but duplicates existing totals and embeds one mechanic into compatibility code.
3. **Move the UI only.** Improves visibility but leaves contributor and gift credit wrong.

## Product behavior

Support contains two independent sections:

1. **Cards created**, first: one player panel with a row per generated card type, sorted by created count descending, then label and stable source key. Each row shows the card name, **Created**, and **Recorded gifts**. Thus Soul is explicitly visible as a named row, without a hard-coded Soul-only panel. Created includes cards for self and teammates; recorded gifts is a subset, never added to created.
2. **Given to teammates**: the existing five gift categories and awards. Its Cards total includes all generated-card gifts, including corrected Soulbound output. Soul gifts shown above are already part of this total.

Use separate explanations: "Includes cards made for yourself and teammates. Gifts are included in Created." and "Recorded gifts may exclude cards from before this update." The second note is deliberately honest for resumed and historical saves, because the old format has no per-card gift history. Keep the five-category gift explanation scoped to its own section.

Creation appears in solo runs too. Self-creation never enables Care package or any other teammate-support award. Zero creation hides that section; zero teammate gifts hides the gift section. Only when both are empty show "Cards created and help given to teammates appear here. Nothing yet." Do not show the current solo message when creation exists.

Move the creation list out of Sources. Keep Sources about damage. The exported image includes the same two sections independently, with all recorded card types and adaptive height. The Support tab must scroll when both sections or many card types exceed the viewport; controller scrolling must remain available. Both sections update live, including the first event after opening the panel.

Card factory continues using the existing CardsCreated totals, now with corrected contributors for new Soulbound events. Care package continues using CardsGiven and its existing threshold. No new award, damage contribution, draw credit, or numerical valuation of a Soul is introduced.

## Counting contract

Each actual generated card reaching the existing global hook produces at most one creation record. For a batch of three, the three global callbacks count three; do not also add the requested batch amount.

Persist a new generic `PlayerTotals.CardGifts` dictionary keyed like CardsCreated (`Card:SOUL`, etc.), containing SourceTotal values. Existing `CardsCreated` and `CardsGiven` remain the authoritative creation and aggregate-gift fields. Introduce one Core entry point:

```csharp
void RunStats.RecordCardGeneration(ulong contributor, ulong? recipient, SourceRef card, int count = 1)
```

For positive count: increment CardsCreated once; when recipient is known and different, increment CardGifts and CardsGiven once. Unknown recipients allow creation credit but no gift. Zero/negative counts do nothing. Retain RecordCardCreated for existing callers and fixtures; migrate the live generation path to the new entry point. Do not also call the mutating Support helper for this event.

No persistent Soul-specific total is needed. Stable card keys distinguish cards with identical localized names. Player identity is NetId throughout; names are display-only.

## Attribution contract

Observe concrete model overrides of `AfterCardGeneratedForCombat(CardModel, Player)` using a dedicated `CardCreationSources` component. Reuse a separate instance of Core `EffectScopes`; do not extend the shared damage/kill scope or change SupportCredit's precedence for energy, block, or draws.

The model callback scope identifies the reaction creating an additional card. The global Hook prefix records the incoming card before dispatching its observers. Therefore observers cannot take credit for the original triggering card. A nested generation callback sees the reaction that actually generated that additional card. Verify this ordering against the installed game before implementation.

Resolve contributor with a pure Core function:

```csharp
ulong? CardCreationCredit.Resolve(ulong? creator, ulong? recipient,
                                 bool observedReaction, ulong? reactionContributor)
```

| Facts | Contributor |
|---|---|
| Non-null explicit creator differs from recipient | Explicit creator; preserve direct gifts such as Glimpse Beyond |
| Reaction observed; creator equals recipient, or creator absent | Verified reaction contributor, possibly null |
| No reaction observed | Explicit creator, possibly null |

For an observed PowerModel reaction, use `FactsExtractor.PlayerIdOf(power.Applier)` only. Do not fall back to its recipient/owner and claim that proves who supplied the effect. For cards, relics, potions and orbs, use their actual player owner. Unsupported model ownership remains unknown. Do not infer a contributor from the current turn, action queue, card ID, deck owner, or merely a Soulbound power present on a player.

When the observed reaction has no provable contributor, skip attributed creation/gifts and log the unresolved event. When instrumentation is unavailable, retain existing explicit-creator accounting and log that reaction correction is unavailable; do not claim that a recipient-equals-creator case is corrected. Null-creator enemy status generation outside a verified player reaction remains unattributed.

Discovery uses registered Models.Types(), virtual Task-returning implementations with the exact base definition/signature, skips base no-ops, resolves the actual declaring MethodInfo, and patches each method once. Initialize alongside EffectSources after model registration. A failure must not abort startup or disable unrelated counters. Log patched/attempted counts and bounded errors.

Scope must survive awaits, restore the caller immediately when returning a Task, expire on completion/fault/cancellation, isolate overlapping players and nested callbacks, and invalidate at combat/run transitions. Reuse EffectScopes lifecycle behavior and prove integration with real Harmony wrappers; unit tests alone do not prove the hook target is correct. No broad scan or reflection per generated card.

## Soulbound example

Alice applies one stack of Soulbound to Bob, then creates two Souls for herself:

| New output | Alice created | Alice recorded gifts | Bob created from this output |
|---|---:|---:|---:|
| Two original Souls | +2 | +0 | +0 |
| Two Souls generated by the buff on Bob | +2 | +2 | +0 |
| Total | +4 | +2 | +0 |

Alice's CardsGiven increases by two. The original buff application still counts under Buffs given as it does today; that is a different kind of event, not a second card gift. Do not derive output from assumed Soulbound multipliers: count actual callbacks.

Multiple appliers on one stacked power use the applier currently represented by the executing game instance. Inspect and document the game's stacking behavior during integration; this feature does not introduce a per-stack ownership ledger or pretend to recover overwritten appliers.

## Persistence and replay

Old saves deserialize missing CardGifts as an empty dictionary. Keep every old creation and CardsGiven total as stored; do not transfer the observed nine Souls retroactively, reset the run, or infer historical gift types. New events accumulate correctly after a resume. Existing aggregate CardsGiven can exceed the sum of the newly recorded per-card gifts.

New events get a structured log line after the usual floor/act prefix:

```text
[F12 A2] card-generation {"v":1,"contributor":1,"recipient":2,"id":"SOUL","label":"Soul","count":1,"effect":"SOULBOUND_POWER"}
```

Use a typed DTO and System.Text.Json serialization, not interpolated user strings. IDs are player NetIds; recipient can be null. Replay validates version, known players, positive count and non-empty card ID, then calls RecordCardGeneration. Unknown versions/malformed payloads are skipped without crashing the replay. This single event records creation and gift together. Do not emit an additional legacy `gave ... cards` line for the same new event, which would double CardsGiven during replay. Continue reading old support lines for older logs.

Unknown contributor diagnostics use a different prefix and do not mutate replay totals. Old logs without creation events remain incomplete; snapshots retain their existing creation totals. Replay and save/load must preserve non-ASCII labels and delimiters inside names.

## Global constraints

- Target net9.0; use the game's existing Harmony and Godot assemblies; add no NuGet dependencies.
- Keep attribution generic: no production Soulbound/Soul IDs or mod names in contributor-selection rules.
- Keep damage, creation, and teammate gifts separate; never add gift subsets to their parent totals.
- Update both eng.json and zhs.json; use stable player/card IDs for identity and localized labels for display.
- Do not rewrite historical ownership or infer missing historical gift details.
- Do not launch, control, or deploy to the game without explicit user authorization.

## Acceptance and implementation limits

Automated checks cover direct/self/unknown/nested attribution, generic generated cards, exactly-once creation and gifts, legacy saves, new event round trips, mixed legacy/new logs, source-key identity, creation-only solo/co-op recap, and unchanged damage/award rules. Harmony integration must prove declaring-method discovery, original-versus-nested callback ordering, real awaits, failure cleanup and fight invalidation. Visual checks cover 1/2/4 players, English/Chinese, long labels, numerous card types, empty-to-live transitions, scrolling and saved images.

The implementation is not ready to ship until the user-authorized in-game Soulbound scenario confirms contributor, recipient, actual counts and UI placement. Current-run snapshots are evidence of the old behavior, not fixtures proving corrected attribution. The accompanying plan schedules those checks explicitly.
