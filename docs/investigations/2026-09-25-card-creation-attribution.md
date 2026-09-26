# Card creation attribution evidence

Investigated 2026-09-25; implementation checks 2026-09-26. No game launch or deployment.

## Installed game contracts

Inspected the installed Windows assemblies with ilspycmd. SHA-256:

- sts2.dll: `0861BFA1DF347538D932F22D580E75420F08082792EB914E53B4882764ACDBE9`
- 0Harmony.dll: `EF1898322C9F5C86DC1B0758B272A9C440823B4A41CA9A0B82A3AA6B3D206387`

`Hook.AfterCardGeneratedForCombat(ICombatState, CardModel, Player?)` iterates listeners and awaits each `AbstractModel.AfterCardGeneratedForCombat(CardModel, Player?)` override. The mod's global prefix observes the original card before any listener executes. `CardPileCmd.AddGeneratedCardsToCombat(IEnumerable<CardModel>, PileType, Player?, CardPilePosition)` calls that global hook once per output card. This is why only the global hook counts outputs while model hooks provide context.

Soulbound checks whether the supplied creator is its Applier and whether the card is a Soul, guards recursion with IsAddingSoul, and generates its actual Amount of Souls into the Owner's draw pile. It passes Owner.Player as creator. The observed reaction's Applier therefore provides contributor evidence the nested generated-card event lacks.

PowerCmd.Apply assigns Applier on a new power. Its generic overload calls ModifyAmount when stacking an existing instance, which changes Amount but does not reassign Applier. Soulbound uses the default PowerInstanceType.None. Normal stacking therefore retains the original applier. We attribute the executing instance to its retained applier, without a per-stack ledger. Removing and reapplying creates a new instance/applier. This corrects an intermediate investigation assumption that stacking replaces the applier.

Raw local decompilations are ignored under `runs/2026-09-26-card-creation/`; no game sources or binaries are distributed.

## Implementation checks

The standalone `tools/spikes/CardCreationAttribution` project references the real game, Harmony and production mod. It exercises production method discovery/patching against concrete AbstractModel overrides, deduplicates inherited methods, skips base no-ops, patches the real Soulbound override, and checks awaits, nested/concurrent contexts, fault/cancellation/throw cleanup, fight invalidation and independent effect scopes. Real uninitialized game objects test contributor extraction without running a combat or native UI.

The dispatch-order check simulates the inspected global hook's record-before-listeners order; it is not a full combat simulation. The harness does not invoke Soulbound gameplay, load a save, or prove that the in-game panel renders correctly.

Commands:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --project tools/spikes/CardCreationAttribution
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests
& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried/WhoCarried.csproj -c Release
```

## Historical evidence limits

The 16:07 snapshot on 2026-09-25 recorded Huntlol 93 Souls and Evening 9, with no aggregate card gifts. It proves creation was already recorded and the display was hard to find, not that all nine Evening Souls came from Soulbound. Existing history remains unchanged. Per-card gift details begin with the new events; missing past details are not inferred.

## Runtime validation still required

Automated results on 2026-09-26: Core 297/297; Harmony harness 20 checks passed; Release build 0 warnings/errors. The Core suite covers contributor selection, exactly-once gifts, old save preservation, duplicate labels/IDs, solo creation, thresholds and structured replay including malformed events and mixed historical/new logs. A clean archive of the feature commits also passed its build and the then-current 295 tests without the unrelated timer edits.

Independent review found one important issue: the existing exporter silently clamped images at 8192 pixels, which could omit creation rows and later sections. The correction renders 4096-pixel viewport slices and assembles the full-height image. ExportLayoutTests first failed for the absent full-page slicing API, then passed for boundaries through 20001 pixels, including 8193 and contiguous, non-overlapping row coverage. Rendering/allocation errors report failure instead of a cropped success. This verifies layout coverage; actual stitched pixels still require visual acceptance. No additional critical or minor findings were reported.

Preview flags prepared (not launched): `1 eng creation-only`, `2 eng creation-gifts`, `4 zhs creation-many`, `4 eng creation-tall`, `2 eng creation-empty`, `2 eng creation-live`. The tall case gives all players 128 generated types to exercise multiple export slices. Existing preview machinery captures Support, an empty-to-live update, and the exported image. Repeat representative flags with both languages and 1/2/4 players; visually inspect overflow, slice seams and the footer, and navigate to the final row.

After explicit user authorization, check actual Soulbound and Glimpse Beyond outputs, normal stacking and removal/reapplication, save/resume, live Support, controller scrolling, English/Chinese layouts for 1/2/4 players, and exported images. Do not equate the standalone Harmony checks with a live gameplay or visual pass.
