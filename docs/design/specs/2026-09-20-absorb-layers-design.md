# Absorb layers — damage stopped between block and HP

Date: 2026-09-20. Branch: `codex/spike-generic-damage-attribution`.

Mods can add a layer that eats damage after block and before HP. Zone the Spire's **Marbled** is the case in hand; the design treats it as one instance of a general shape and names no mod in the code.

## Problem

Damage a layer eats is counted nowhere. The mod reads two fields of the game's `DamageResult`: `UnblockedDamage` (HP actually removed) and `BlockedDamage` (what block soaked). A layer that eats a hit after block reduces the HP loss before the result is written, so a fully eaten hit reports `UnblockedDamage` 0 and `BlockedDamage` of the real block only. Neither column moves, and the work the layer did is invisible in the recap.

## Decision

**Absorbed damage counts as block, on both sides.**

- A hit on an enemy: the amount its layer ate is added to the enemy block that hit knocked off, credited to the card (or other source) that dealt it.
- A hit on a player or their pet: the amount their layer ate is added to that player's damage blocked, a pet's going to its owner.

An absorb layer is consumed protection — granted, then spent by a hit — which is what block is. That is unlike a pet soaking a hit, where a second creature loses real HP it can die from, and which is why pets have their own number and armour does not.

Counting one side as block and the other as its own number would be the inconsistent choice, because both numbers carry an award: `Wall` ranks on damage blocked, `Siege breaker` on enemy block knocked off. Both now count armour.

Two consequences, accepted:

- A player's blocked total can exceed the block they actually gained. The Defense tab's note changes to say so: "The shield is damage your own block, and any armour a mod adds, soaked up."
- `Wall` and `Siege breaker` count armour as well as block.

The base game's own HP-loss caps on enemies (Slippery, Hardened Shell) are not armour and aren't counted; see [vanilla attribution fixes](2026-09-27-vanilla-attribution-fixes-design.md).

## How it's measured

Every absorb layer has to pass through the game's own `Hook.ModifyHpLost`, which is where HP loss is settled between block and the creature's HP. One postfix there measures every layer, whoever wrote it.

1. A postfix on `Hook.ModifyHpLost` records `amount - __result` for the call's target: what something took off this HP loss.
2. It runs at Harmony's lowest priority (`[HarmonyPriority(Priority.Last)]`), so it sees the value after other mods' postfixes have reduced it. A mod that reduces HP loss at an even lower priority would be missed; none is known to. The author of Zone the Spire sets no priority and asks us not to depend on that, which is what the lowest priority is for.
3. It records in **either** phase, as long as the call names a single one (`BeforeOsty` or `AfterOsty`). Which phase a layer acts in is the layer's own business and changes between versions of the same mod — see the table below — so the measurement never assumes one. The game's docs give `All` as what damage previews pass, and the game's own three callers (all in `CreatureCmd.Damage`) pass single phases, so a call naming both is not real damage and is ignored.
4. Amounts accumulate per creature, keyed by reference. A hit clears its target's total as it starts (`BeforeDamageReceived`), and each damage result takes its receiver's total when it arrives (`AfterDamageGiven`). Fight start and end clear everything.
5. Per damage result: an enemy receiver's total is added to the `blocked` figure passed to `RecordHit`, a friendly receiver's to `RecordBlocked` for its player or pet owner.

Nothing in this needs the layer's name, its id, or an API from the mod that owns it.

## Deliberately not doing

- **Naming the layer in the recap.** The chosen placement folds absorbed damage into block, which carries no source of its own on the defensive side and the attacking card's on the offensive one, so a name is not needed for either. The game's `modifiers` list from `ModifyHpLost` does name the models that changed the value, and Zone the Spire now appends Marbled to it, so the names are written to the event log for diagnosis. Note that the list says *which* models acted, never *how much* each took, so when two layers eat one hit the total cannot be split between them by name — which is another reason the recap counts the total rather than naming it.
- **A separate "absorbed" number.** Considered and rejected above.
- **Correcting the Weak and Strength-down counterfactuals.** Those are measured before block, where an absorb layer hasn't acted yet, so a hit that Weak shrank and armour then ate can credit prevention for HP that armour would have saved anyway. Both numbers describe real work; only their sum can overstate.

## What was verified

Against the installed `ZoneTheSpire.dll` and the game's own assemblies, read only:

| Claim | Finding |
|---|---|
| Marbled eats damage inside `Hook.ModifyHpLost` | Yes: `MarbledHpLossPatch` is a postfix that assigns `ref __result` |
| The phase | Not fixed. The build on the Workshop on 2026-09-19 gates both of its branches on `AfterOsty` (2), so it absorbs there; its author reports their newer source gating the absorb on `BeforeOsty` instead. This is why we measure either. |
| Unblockable damage (Poison) | Skipped by their check, so it is never absorbed |
| Marbled in the game's `modifiers` list | Not in the build inspected, where only `GoldenWishmaker` was added. Its author has since made Marbled append itself too. |
| Other HP-loss reductions in that mod | `GoldenWishmaker.ModifyFinalHpLoss`, in the same postfix; it will be counted the same way |
| `HpLossHookPhase` | `None = 0, BeforeOsty = 1, AfterOsty = 2, All = 3`; the game's docs give previews as what passes `All` |

The disagreement about the phase is the point of the design rather than a problem with it: a layer's phase is a detail of the mod that owns it, and it changed between two builds of the same mod within a day. Reading either phase means neither build needs anything from us.

## Tests

- **Core**: a ledger that accumulates per target, hands a total over once, and clears — the part with rules of its own. Nine checks, each first seen failing against a plausible wrong version (one shared total, no clearing on take, increases cancelling reductions, repeated layer names).
- **Against the real assemblies**: done. Applying Zone the Spire's `MarbledHpLossPatch` and ours to the game's `Hook.ModifyHpLost` gives the order `MarbledHpLossPatch -> ModifyHpLostPatch`, so ours reads what their absorb left, and ours is registered at the lowest priority as its author asked.
- **In a game**, once installed: an enemy statue chipped by a card, a player eating a hit with their own armour, a hit split between block and armour, a pet's armour crediting its owner, and a run with no such mod recording zero.

## Settled with the mod's author

Nothing was needed from their side, and nothing is now.

- They have made Marbled append itself to the `modifiers` list, and guarded the absorb against a call naming both phases. Neither is required here; both are good for any other mod reading the same hook.
- They report the game has three callers of `ModifyHpLost`, all in `CreatureCmd.Damage`, each passing a single phase, so nothing in the shipped game passes `All` today. Our filter stays as a guard against a future caller, ours or theirs.
- They prefer the defensive side to read as damage prevented rather than block. We count it as block, because the same damage on the offensive side is counted as block removed and each of those numbers carries an award; splitting them would make `Wall` and `Siege breaker` disagree about what armour is. Recorded here as a considered difference, not an oversight.
- They set no Harmony priority and ask us not to rely on that. We don't: naming is order-independent, but the *amount* is read from `__result`, so our postfix takes the lowest priority to run last.
