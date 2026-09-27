# Damage prevented for teammates

Date: 2026-09-27. Branch: `feature/protection-given`.

## Problem

Co-op has cards whose whole point is protecting someone else, and the recap gives them nothing:

| Card | What it puts on a teammate | Effect |
|---|---|---|
| Intercept | Covered (applied by the Intercept player) | The teammate takes **no** damage from attacks this turn; the Intercept player takes double |
| Tank | Guarded, on every teammate (applied by the Tank player) | Teammates take **50% less** from attacks; the Tank player takes 50% more |

The hit on the protected teammate arrives already shrunk (to 0 for Covered), so nothing counts.

## Decision

**Damage a teammate's buff kept off you is credited to that teammate, as support: a new kind, "Damage prevented".**

- **What counts:** an enemy hit on a player (or their pet) where the target has a **buff applied by another player** whose damage multiplier for this hit is below 1. That's the game's own `ModifyDamageMultiplicative`, read the same way Weak is read today. It covers Covered and Guarded without naming them, and any mod's protective buff that works the same way.
- **How much:** the HP the hit would have removed without those buffs, minus the HP it removes, after the target's block and capped at their HP. This is the same counterfactual Weak's "prevented" uses.
  - With every protective multiplier above 0 (Guarded), the hit without them is the final damage divided by their product.
  - A multiplier of 0 (Covered) leaves nothing to divide, so the hit is worked out again from the damage it started from. That's the game's calculation with the protective buffs left out: additive modifiers, then every other multiplier, then damage caps. If where it started wasn't seen, nothing is credited.
- **Sharing:** between several protective buffs on one hit, by how much each shrank it (by logarithm, like Weak and Shrink). A buff at ×0 takes all of it, shared evenly with any other ×0 buff.
- **Not counted:**
  - Buffs a player put on themselves (Colossus).
  - Protection that isn't a multiplier: Intangible from Ghost in a Jar is a damage cap, and Buffer is an HP-loss layer.
  - The Tank and Intercept players' own extra damage taken. That already shows as their damage taken.
- **Overlap with Weak:** Weak on the attacker still counts what it prevented on the hit as it landed, after the protection. The two numbers describe different parts of one reduction and don't double count.

## On screen

- **Support tab:** a new card, **Damage prevented**, with Covered's icon. It lists who kept how much damage off teammates, like the other kinds of help.
- **A new award, Guardian** ("damage kept off teammates"), with Intercept's icon. Its minimum is 10.
- The exported image and the copied picture show it wherever they show the other kinds of help.
- The log writes `Ash gave 18 protection to Moth | GUARDED_POWER`, which the replay reads like the other support lines.

## Tests

- **Core:** the prevented HP for one protective multiplier (×0.5), for ×0 given the hit without it, with block, and capped at HP; the split between two protectors, and the ×0 case.
- **Core:** `RunStats.RecordSupport` with the new kind; a support line with `protection` replays.
- **Core:** the Guardian award goes to the most protection given, at or above the minimum.
- **Localization:** new keys in both `eng.json` and `zhs.json`.
- **In a game:** Intercept on a teammate an enemy attacks, and Tank in a 2-player fight.
