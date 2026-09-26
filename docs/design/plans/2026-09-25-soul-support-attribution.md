# Card Creation Support and Soulbound Attribution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Make generated Souls visible in Support and credit reaction-generated cards and teammate gifts to the verified contributor.

**Architecture:** Preserve existing creation and aggregate gift totals, add per-card recorded gifts, and use one counting entry point. A dedicated generation-reaction observer reuses EffectScopes without changing damage or general support attribution. The recap and exported image display creation separately from teammate gifts.

**Tech Stack:** C# / net9.0, existing Harmony and Godot references, System.Text.Json source generation, repository console test runner.

**Spec:** [Card creation in Support and Soulbound attribution](../specs/2026-09-25-soul-support-attribution-design.md). Read both documents before execution.

**Status:** Implementation authorized 2026-09-26 and executed inline. Tasks 1–5 are implemented; automated checks pass. Task 6's live game, screenshot and controller acceptance checks remain pending explicit authorization. Existing timer edits are preserved outside feature commits. See the investigation for evidence and limits.

**Execution notes:** Scope lifecycle coverage uses the existing 19 EffectScopesTests plus the real Harmony harness instead of duplicating a new CardCreationScopeTests file. Generation replay tests live in the focused CardGenerationReplayTests class. The dispatch-order harness simulates the verified game loop; it does not execute a combat. Discovery failure diagnostics are implemented, but forced Harmony patch failure was not injected. Completed checkboxes refer to implementation with these substitutions; live/visual acceptance remains open below.

## Global Constraints

- Target net9.0; use the game's existing Harmony and Godot assemblies; add no NuGet dependencies.
- Keep attribution generic: no production Soulbound/Soul IDs or mod names in contributor-selection rules.
- Keep damage, creation, and teammate gifts separate; never add gift subsets to their parent totals.
- Update both eng.json and zhs.json; use stable player/card IDs for identity and localized labels for display.
- Do not rewrite historical ownership or infer missing historical gift details.
- Do not launch, control, or deploy to the game without explicit user authorization.

## Review Focus

1. Recipient passed as creator in an asynchronous reaction must not steal credit from its verified applier (Tasks 1 and 3).
2. An observer must not take credit for the original card that triggered it; nested and simultaneous reactions must stay isolated (Task 3).
3. Legacy aggregate gifts with no card details must stay intact and must not be presented as a complete per-card history (Tasks 1, 2 and 5).
4. Mixed new creation events and legacy gift events must replay without counting a new gift twice (Task 4).
5. Solo creation, identical labels, long translations and many generated types must remain visible and navigable in both recap and export (Tasks 2 and 5).

## File map and commands

New Core files: `CardCreationCredit.cs` (pure contributor rule), `CardGenerationEvent.cs` (versioned log DTO). New Game file: `CardCreationSources.cs` (narrow model reaction instrumentation). New UI file: `CreationPanels.cs` (shared creation display). Existing Core `RunStats.cs`, `RecapBuilder.cs`, `LogReplay.cs`, `WhoCarriedJson.cs` own persistence, projections and replay. Existing `Game/Tracker.cs` remains the game-to-Core boundary. Wire installation in `ModEntry.cs`; do not merge this observer into EffectSources.

Tests live under `tests/WhoCarried.Tests`; new files there are automatically included. The existing runner accepts a name filter as its first argument. Use PowerShell and the x64 runtime:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests
& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried/WhoCarried.csproj -c Release
```

Before editing, inspect `git status --short`, read applicable guidance and run the baseline tests. Record pre-existing failures instead of attributing them to this feature. Do not stage whole directories containing unrelated edits.

## Task 1: Contributor rule and atomic creation accounting

**Files:** Create `src/WhoCarried/Core/CardCreationCredit.cs`; modify `src/WhoCarried/Core/RunStats.cs`; create `tests/WhoCarried.Tests/CardCreationTests.cs`; extend `tests/WhoCarried.Tests/SupportTests.cs` and `AwardTests.cs`.

**Interfaces:**
- Consumes `SourceRef`, `RunStats.RecordCardCreated`, `RunStats.RecordSupport`, `RunStatsStore`.
- Produces `CardCreationCredit.Resolve(ulong? creator, ulong? recipient, bool observedReaction, ulong? reactionContributor)` returning `ulong?`; `PlayerTotals.CardGifts` as `Dictionary<string, SourceTotal>`; `RunStats.RecordCardGeneration(ulong contributor, ulong? recipient, SourceRef card, int count = 1)` returning void.

- [x] Add failing policy and counting tests, including this minimal regression:

```csharp
[Test]
public static void SoulboundStyleReactionCreditsTheApplier()
{
    Check.Equal((ulong?)1, CardCreationCredit.Resolve(2, 2, true, 1), "reaction applier");
    Check.Equal((ulong?)3, CardCreationCredit.Resolve(3, 2, true, 1), "explicit different creator");
    Check.Equal((ulong?)null, CardCreationCredit.Resolve(2, 2, true, null), "unknown reaction");
    Check.Equal((ulong?)2, CardCreationCredit.Resolve(2, 2, false, null), "ordinary self creation");
    Check.Equal((ulong?)null, CardCreationCredit.Resolve(null, 2, false, null), "enemy status");
    Check.Equal((ulong?)1, CardCreationCredit.Resolve(null, 2, true, 1), "verified reaction");
    var stats = new RunStats();
    var soul = new SourceRef(SourceKind.Card, "SOUL", "Soul");
    stats.RecordCardGeneration(1, 1, soul, 2);
    stats.RecordCardGeneration(1, 2, soul, 2);
    Check.Equal(4, stats.Get(1)!.CardsCreated[soul.Key].Amount, "all outputs");
    Check.Equal(2, stats.Get(1)!.CardGifts[soul.Key].Amount, "gift subset");
    Check.Equal(2, stats.Get(1)!.CardsGiven, "aggregate once");
    Check.True(stats.Get(2) == null, "recipient receives no creation credit");
}
```

- [x] Run the CardCreationTests filter and verify failure for the missing API. Add explicit cases for zero/negative count, null recipient, same-label/different-ID cards, an arbitrary modded card ID, and self gifts.
- [x] Implement the pure rule and recording method:

```csharp
public static ulong? Resolve(ulong? creator, ulong? recipient,
                             bool observedReaction, ulong? reactionContributor)
{
    if (creator.HasValue && creator != recipient) return creator;
    return observedReaction ? reactionContributor : creator;
}

// In PlayerTotals:
public Dictionary<string, SourceTotal> CardGifts { get; set; } = new();

// In RunStats:
public void RecordCardGeneration(ulong contributor, ulong? recipient, SourceRef card, int count = 1)
{
    if (count <= 0) return;
    RecordCardCreated(contributor, card, count);
    if (recipient is not ulong to || contributor == to) return;
    Entry(GetOrAdd(KeyFor(contributor)).CardGifts, card).Amount += count;
    RecordSupport(contributor, to, SupportKind.Cards, count);
}
```

- [x] Extend existing save/load tests with an old JSON fixture containing CardsCreated and CardsGiven but no CardGifts. Expect old totals unchanged and an empty detail dictionary. Add new gift, save and reload; expect only that gift in details and old+new in aggregate. Add Card factory/Care package tests using RecordCardGeneration: four created/two given in the example; thresholds retain their current constants; solo creation grants no teammate award.
- [x] Run CardCreationTests, SupportTests and AwardTests; commit only these Core/test changes with `feat: record generated card gift details`.

## Task 2: Project creation into Support without changing gift semantics

**Files:** Modify `src/WhoCarried/Core/RecapBuilder.cs`; extend `tests/WhoCarried.Tests/CardCreationTests.cs`; later remove the obsolete Sources creation assertion in `DamageDealtTests.cs` when Task 5 removes that UI.

**Interfaces:**
- Consumes CardsCreated and CardGifts keyed by stable SourceRef.Key.
- Produces the following additive view API. Keep existing SourcesView.CreatedCards temporarily until Task 5, so this commit remains buildable.

```csharp
public sealed record CreatedCardRow(string Key, string Label, int Created, int Given);
public sealed record CreationRow(ulong PlayerId, string Label, string ColorHex,
                                string? IconKey, IReadOnlyList<CreatedCardRow> Cards);
// Append optional parameter to RecapView's primary constructor:
// IReadOnlyList<CreationRow>? CreationRows = null
// Add properties:
public IReadOnlyList<CreationRow> Creation => CreationRows ?? Array.Empty<CreationRow>();
public bool HasCardCreation => Creation.Any(p => p.Cards.Count > 0);
public bool HasSupportContent => HasSupport || HasCardCreation;
```

- [x] Write failing projection tests. With Alice's five Souls for self and two for Bob, expect Alice's Soul row `(Created: 7, Given: 2)`, Bob's creation list empty, HasCardCreation true and HasSupport true. With only the five self Souls in a solo run, expect HasCardCreation/HasSupportContent true and HasSupport false. Include zero-event run and two card IDs sharing a label.
- [x] Run CardCreationTests to confirm failure. Implement player rows in scoreboard order, using `p.NetId` for identity. Use the existing source keys, not localized labels:

```csharp
static IReadOnlyList<CreatedCardRow> CreationCards(PlayerTotals? totals) => totals == null
    ? Array.Empty<CreatedCardRow>()
    : totals.CardsCreated.Where(kv => kv.Value.Amount > 0)
        .OrderByDescending(kv => kv.Value.Amount)
        .ThenBy(kv => kv.Value.Label, StringComparer.Ordinal)
        .ThenBy(kv => kv.Key, StringComparer.Ordinal)
        .Select(kv => new CreatedCardRow(kv.Key, kv.Value.Label, kv.Value.Amount,
            totals.CardGifts.TryGetValue(kv.Key, out var gift) ? gift.Amount : 0))
        .ToList();
```

- [x] Supply `CreationRows` from RecapBuilder.Build; preserve HasSupport's existing definition and all damage ranks/totals. Add an assertion that previously saved creation totals appear even when gift details are absent. Run CardCreationTests and SupportTests, then the full suite; commit `feat: expose creation rows for support recap`.

## Task 3: Observe real generation reactions and wire live attribution

**Files:** Create `src/WhoCarried/Game/CardCreationSources.cs`; modify `src/WhoCarried/ModEntry.cs`, `src/WhoCarried/Game/Tracker.cs`; create `tests/WhoCarried.Tests/CardCreationScopeTests.cs`; create a standalone integration harness under `tools/spikes/CardCreationAttribution/` using the existing GenericAttribution probe's project/reference pattern.

**Interfaces:**
- Consumes `Models.Types()`, `EffectScopes.EnterEffect/LeaveEffect/NewFight`, `FactsExtractor.PlayerIdOf`, and Task-returning game model hooks.
- Produces `CardCreationSources.InstallOnce()`, `NewFight()`, `Running` (`AbstractModel?`), and `ContributorOf(AbstractModel effect)` (`ulong?`). A separate scope instance is mandatory.
- Tracker consumes Running once per global generation callback, computes observedReaction from its presence, then calls CardCreationCredit.Resolve and RecordCardGeneration.

- [x] Inspect installed `Hook.AfterCardGeneratedForCombat`, `CardPileCmd.AddGeneratedCardsToCombat` overloads and `SoulboundPower` before implementing. Record assembly hashes, callback ordering, exact signatures, and stacking/applier behavior in a concise investigation file under `docs/investigations/2026-09-25-card-creation-attribution.md`. Keep full decompiled sources/binaries in ignored local scratch only. If the ordering differs from the spec, revise the design before coding the observer.
- [x] Add a failing integration regression against the installed Harmony/game assemblies: patch actual declaring methods, execute synthetic reaction models without launching the game, and capture original-card versus nested-card observations. Use two distinct player IDs and a TaskCompletionSource to force an await. Require original -> explicit creator; nested recipient-as-creator -> reaction applier. Do not accept hand-calling a helper as proof the Harmony hook works.
- [x] Add Core scope tests modeled on EffectScopesTests.Hook. Use a separate EffectScopes object from a simulated damage scope; assert no cross-channel state. Cover nested reactions, two concurrent instances, synchronous throw, faulted/cancelled Task, completion, and NewFight invalidation. Copy the existing scope test helper into the new test class (it is private in the old class).

```csharp
private static Task Hook(EffectScopes scopes, object effect, Func<Task> body)
{
    var frame = scopes.EnterEffect(effect);
    Task? result = null;
    try { return result = body(); }
    finally { scopes.LeaveEffect(frame, result); }
}
```

- [x] Implement discovery using EffectSources' declared-method/deduplication pattern, restricted to the exact base `AfterCardGeneratedForCombat` method and its signature. Do not match names alone or patch async MoveNext. Skip no-op base implementations. Instrument entry/exit with:

```csharp
private static readonly EffectScopes Scopes = new();
public static AbstractModel? Running => Scopes.Effect as AbstractModel;
public static void NewFight() => Scopes.NewFight();
private static void Enter(AbstractModel __instance, out EffectScopes.Frame? __state) =>
    __state = Scopes.EnterEffect(__instance);
private static void Leave(EffectScopes.Frame? __state, Task? __result) =>
    Scopes.LeaveEffect(__state, __result);
```

- [x] Implement ContributorOf by explicit model types. PowerModel uses only PlayerIdOf(power.Applier); CardModel/RelicModel/PotionModel/OrbModel use their Owner.NetId. Catch missing/incompatible ownership access and return null; never use PowerModel.Owner as a substitute for applier. Install once beside the existing ModEntry EffectSources call, log attempts/successes and bounded errors, and reset the dedicated scope in Tracker.OnRunStarted and OnCombatStart. Add reset in OnCombatEnd and OnRunEnded as well; all scope invalidation is independent of damage tracking.
- [x] Replace the body of Tracker.OnCardCreated around this flow, retaining error handling in the existing global patch:

```csharp
AbstractModel? effect = CardCreationSources.Running;
ulong? recipient = card.Owner?.NetId;
ulong? contributor = CardCreationCredit.Resolve(creator?.NetId, recipient,
    effect != null, effect == null ? null : CardCreationSources.ContributorOf(effect));
if (contributor is not ulong player)
{
    _log?.Write($"{Where} card-generation-unresolved creator={creator?.NetId} " +
        $"recipient={recipient} card={card.Id.Entry} effect={effect?.Id.Entry ?? "?"}");
    return;
}
string id = card.Id.Entry;
var source = new SourceRef(SourceKind.Card, id, GameText.Title(card.TitleLocString, id));
_stats.RecordCardGeneration(player, recipient, source);
Touch();
```

- [x] Keep the original global Hook patch as the sole counting point. Remove its old Support(...) call; Task 4 supplies the single replayable log event. Extend the integration harness with direct cross-player creator preservation, absent applier, enemy-generated statuses, inherited shared overrides patched once, discovery/patch failure diagnostics, and later-fight work. Run harness, Core scope tests, and Release build; commit `fix: attribute reaction-generated cards to their contributor`.

## Task 4: Log and replay creation without duplicate gifts

**Files:** Create `src/WhoCarried/Core/CardGenerationEvent.cs`; modify `Core/WhoCarriedJson.cs`, `Core/LogReplay.cs`, `Game/Tracker.cs`; extend `tests/WhoCarried.Tests/ReplayTests.cs`.

**Interfaces:**
- Produces `CardGenerationEvent` with properties Version (`int`, serialized `v`), Contributor (`ulong`), Recipient (`ulong?`), Id (`string`), Label (`string`), Count (`int`), Effect (`string?`), with the lowercase JSON names shown in the spec.
- Produces `LogReplay.CardGenerationLine(CardGenerationEvent value)` returning the `card-generation ` prefix plus serialized DTO, without floor/act prefix. Tracker adds its existing Where prefix.
- Consumes RecordCardGeneration; do not route new card events through legacy RecordSupport a second time.

- [x] Add failing round-trip tests using two player header lines and one new event. Assert created=1, gift detail=1 and CardsGiven=1. Add one legacy `gave 3 cards` line: expect aggregate=4, recorded detail=1. Include same player names with distinct IDs, Chinese/quotes/pipes in label, null recipient, self creation, malformed JSON, empty ID, unknown version, negative/zero count, unknown contributor/recipient and a log with no creation events.
- [x] Implement typed JSON using `[JsonPropertyName]` attributes and add `[JsonSerializable(typeof(CardGenerationEvent))]` to WhoCarriedJson. Provide the formatter:

```csharp
public static string CardGenerationLine(CardGenerationEvent value) =>
    "card-generation " + JsonSerializer.Serialize(value, CardEventJson);
```

`CardEventJson` is a private static JsonTypeInfo<CardGenerationEvent> from `new WhoCarriedJson(new JsonSerializerOptions { WriteIndented = false }).CardGenerationEvent`. Do not use the default indented serializer context, which would split a log event across lines. Add required System.Text.Json / Serialization.Metadata imports.

- [x] Match `Where + @"card-generation (.*)$"` before legacy gift processing, deserialize in a JsonException guard, validate a non-null DTO, Version==1, Count>0, non-empty Id and IDs against parsed players. Reject a specified unknown recipient; accept null. Then call RecordCardGeneration with SourceKind.Card and the logged label (fall back to Id if the label is null/empty). Update LogReplay's obsolete comment that creation is never recorded.
- [x] Emit one new event after successful live recording; emit no legacy gift line for that event. Unknown attribution uses `card-generation-unresolved` with diagnostic fields and is ignored by replay. Confirm a emitted event round-trips on one line, including escaped newlines in a label. Run ReplayTests and full suite; commit `feat: replay card creation and gifts from structured events`.

## Task 5: Move creation into Support and export it

**Files:** Create `src/WhoCarried/UI/CreationPanels.cs`; modify `UI/SupportTab.cs`, `UI/SourcesTab.cs`, `UI/SummaryCard.cs`, `UI/RecapPanel.cs`, `UI/DevPreview.cs`, `Core/RecapBuilder.cs`, `Localization/eng.json`, `Localization/zhs.json`; update `tests/WhoCarried.Tests/DamageDealtTests.cs` and `LocalizationTests.cs`.

**Interfaces:**
- `CreationPanels.Create(Kit k, RecapView view, float width, Live? live, bool compact = false)` returns Control.
- Change `SupportTab.Create` to accept optional `PadTab? pad = null`, passing the matching pad entry from RecapPanel exactly as SourcesTab does. Existing `SupportTab.Cards`, HasSupport and gift awards retain their semantics.
- CreationPanels consumes CreationRow/CreatedCardRow only; row identity uses PlayerId and Key. It does not update stats.

- [x] Add localization template tests using the following concrete copy, and projection assertions for creation-only co-op/solo, all-empty, and mixed content. Confirm the tests fail before adding keys. Add these to both catalogs:

| Key suffix under WHO_CARRIED.support | English | Simplified Chinese |
|---|---|---|
| created_heading | Cards created | 生成卡牌 |
| created_count | Created | 生成数量 |
| gifts_recorded | Recorded gifts | 已记录的赠予 |
| created_hint | Includes cards made for yourself and teammates. Gifts are included in Created. | 包括为自己和队友生成的卡牌。赠予数量已计入生成数量。 |
| gifts_history | Recorded gifts may exclude cards from before this update. | 已记录的赠予可能不包含本次更新前的卡牌。 |

Change WHO_CARRIED.empty.support to the spec's combined empty message and its translation `此处显示生成的卡牌及给予队友的帮助。暂无记录。`. Remove the obsolete support.solo key if no longer referenced; remove sources.created when Sources no longer uses it. Keep key sets identical and avoid unused entries.

- [x] Implement CreationPanels with one player panel per nonempty creation list, using an existing Kit VBox/Grid pattern, player color/icon/name and a table headed card name / Created / Recorded gifts. All cards remain visible; long labels wrap, counts align, and rows sort as the Core projection supplies. Rebuild only when stable row keys/order change; update numbers live otherwise. Compact mode reduces spacing/fonts using existing summary conventions, without truncating away card types.
- [x] Put both sections inside a Support scroll container matching Sources' viewport and PadTab.Scrolls pattern:

```csharp
creation.Visible = v.HasCardCreation;
gifts.Visible = v.HasSupport;
empty.Visible = !v.HasSupportContent;
// Attach live.On(Apply) so the first generated card reveals its section.
```

Give creation and gifts their own heading/hint; place creation first. Do not place self-creation under the heading Given to teammates. Ensure one compact player column per available width and wrap panels when needed, rather than squeezing four long names into unreadable widths.
- [x] Add an independent SummaryCard section guarded by HasCardCreation using CreationPanels.Create(..., compact: true), before the existing gift section guarded by HasSupport. Do not call SupportTab.Cards with zero columns when only creation exists. Let export height grow naturally.
- [x] Remove the Sources creation footer and its projection from SourcesView after migrating the old `CreatedCardsFlowIntoSources` test to the Support creation tests. Update positional SourcesView constructors (Kinds shifts into the removed Created argument position). Keep Card factory on PlayerTotals.CardsCreated. Search every CreatedCard/CreatedCards/SourcesView reference before removing obsolete types.
- [x] Extend DevPreview with deterministic self Souls, Soulbound-style contributor/gift counts via RecordCardGeneration, a generated non-Soul card, a player with creation but no damage, four players, duplicate labels/different keys and long labels. Run LocalizationTests, CardCreationTests, DamageDealtTests and the Release build. Stage only feature hunks in RecapPanel/DevPreview; their timer edits predate this work. Commit `feat: show card creation and recorded gifts in support`.

## Task 6: Validate complete behavior and document limits

**Files:** Update `README.md`, `CHANGELOG.md`, the focused investigation document, and this plan's checkboxes. Do not touch the Workshop publishing workspace.

- [ ] Run full Core suite, standalone Harmony generation harness and Release build once after integration; retain commands/results and game assembly hashes in the investigation. Re-run only checks affected by subsequent changes. Confirm damage, energy/block/draw attribution and existing award thresholds remain unchanged.
- [ ] Obtain explicit authorization before game deployment/interaction. After authorization, close the game before deployment using the normal project process. Exercise Alice/Bob: direct self-generation, Glimpse Beyond, one-stack Soulbound on teammate followed by two source Souls, recipient's own Soul generation, and stacked/reapplied Soulbound. Compare actual generated outputs, new event fields, saved totals, Support rows and export. Expect +4 created/+2 given for Alice in the stated one-stack example; do not assume all nine old Evening Souls came from Soulbound.
- [ ] Verify English/Chinese previews for 1/2/4 players, creation-only, gift-only, mixed, many types, long names and live zero-to-positive transition. Inspect screenshots and exported images; verify mouse/controller scroll reaches the last row. Passing Core tests cannot substitute for these visual checks.
- [ ] Reload an old save copy, never the user's live save: retain its old creation/aggregate gift totals, add a new known event, and confirm only the new event appears in per-card gifts. Replay a newly captured log and compare newly recorded creation/gift totals. Record that old historical ownership and missing per-card gifts remain unrecoverable.
- [ ] Add concise user-facing release text: generated cards now appear under Support; Soulbound-generated teammate cards credit the buff contributor; old history is not retroactively corrected. README explains Created versus Recorded gifts, including that gifts are already inside creation totals. Stage only these feature documentation hunks and commit `docs: explain generated card support and attribution limits`.

## Handoff and completion criteria

The user authorized implementation on 2026-09-26. Execution is inline with the Superpowers workflow and a final independent review. No deployment or game interaction has been authorized.

Completion requires the automated and integration checks above, visual evidence, and the authorized in-game Soulbound check. If runtime access is not authorized, report the implementation as awaiting runtime validation rather than claiming the Soulbound fix is proven. Do not merge, publish or deploy as part of planning.
