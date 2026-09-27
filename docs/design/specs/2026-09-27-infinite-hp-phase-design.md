# Infinite-HP phases don't count

Date: 2026-09-27. Branch: `bug/infinite-hp-phase`.

## Problem

Some enemies have a phase where they can't die. The Waterfall Giant is the vanilla case: when it's "killed" it sets its HP to 999,999,999, shows an infinite health bar, and takes one more turn to wind up a final eruption. Content mods add enemies that do the same thing.

Every hit in that phase removes real HP from the huge pool, so the mod counts it as damage. Players have noticed and pad their numbers by attacking into the phase. The damage changes nothing about the fight, so it shouldn't count.

## Decision

**A hit on an enemy whose health bar shows infinite counts for nothing on the attacking side.** No damage dealt, no enemy block knocked off, no bonus damage for Vulnerable-style debuffs, and no "damage lost to Weak" for a Weak attacker. Poison and Doom ticks on such an enemy aren't shared out either.

The signal is the game's own: `Creature.HpDisplay.IsInfinite()`, true for `InfiniteWithNumbers` and `InfiniteWithoutNumbers`. It's the flag the health bar reads to draw itself purple. The game also uses it in its own logic: Hellraiser checks whether every enemy shows infinite. It exists on both supported game versions (v0.107.1 and v0.111.0).

The flag isn't specific to any enemy or mod, so this names no content:

| Who sets it | When |
|---|---|
| Waterfall Giant (vanilla) | After it's "killed": HP 999,999,999, `InfiniteWithoutNumbers`, until its final attack |
| Hardened Shell (vanilla, Skulking Colony) | Once this turn's HP-loss cap is used up: `InfiniteWithNumbers`, until the next turn starts |
| Content mods the group plays | Both conventions: an undying phase with 999,999,999 HP, and a Hardened-Shell-style per-turn cap |

Hardened Shell's capped state is the same idea from the other direction: hits there remove no HP, and what the cap ate would otherwise show up as block knocked off.

## Timing: the killing blow still counts

These enemies switch to infinite inside their death handling (the death-prevention hook, or `AfterDeath`). The game runs that after the hit that brought them to 0 has been reported through `Hook.AfterDamageGiven`, where the mod counts it. The flag is still off at that moment, so the killing blow counts in full, overkill capped at the HP that was left, as today. Every later hit, including the rest of a multi-hit attack, lands on the infinite enemy and doesn't count.

## What still counts

- **The enemy's own attacks.** The final eruption is real damage to players. Block, damage taken, Weak and Strength-down prevention, and Vulnerable's cost on the victim are all measured as usual. Weakening the Giant during its wind-up is the right play, and it keeps its credit.
- **Debuffs applied to it.** Stacks are counted as applied, because Weak and Strength-down applied during the phase shrink the final attack. Vulnerable stacks on it are counted as applied too. It would take per-debuff rules to single them out, and a stack count isn't the damage number people pad.
- **Hits on it before the phase and after it ends.** The rule reads the flag at each hit.

## Kills

- A **Doom kill** or **direct kill** of a creature showing infinite HP credits nothing. Doom can't reach 999,999,999, but a modded judgement could kill such a creature outright, and crediting its HP would add hundreds of millions of damage.
- The ending of an undying phase (the enemy killing itself after its last attack) has no player behind it and credits nobody, as today.

## The event log and replays

A hit that doesn't count is still written to `events.log`, in a form the replay doesn't read as damage:

```
[F33 A2] Moth not counted 24 hp from Card:STRIKE_IRONCLAD (Strike) | target WATERFALL_GIANT, blocked 0, infinite HP
```

A replay of a new log therefore gives the same totals as the live recap. Logs written before this change record padded hits as ordinary hits, so the replay still counts them. The log never recorded whether a target showed infinite, so they can't be told apart. Saved stats keep their old totals.

## Deliberately not doing

- **Fixing Slippery or the capping hit of Hardened Shell.** The hit that reaches Hardened Shell's cap is taken before the flag is set, so what the cap ate is still counted as block knocked off. Slippery (Vantom, Inklets) never sets the flag. Both belong to the separate armour-layer fix from the vanilla audit.
- **Telling "can't die" apart from "still takes real HP loss".** The flag is the game's own statement that the HP number doesn't mean anything right now. Reading anything more would need per-enemy knowledge.

## Tests

- **Core, damage:** a hit on an enemy with infinite HP doesn't count; a hit on a normal enemy, or on a player, does.
- **Core, kills:** a direct kill of a creature showing infinite HP credits nothing, even with an effect acting and a player behind it.
- **Core, replay:** a "not counted" line stays out of a replay's totals, next to an ordinary hit that is counted.
- **Build** against both game versions.
- **In a game**, once installed: a Waterfall Giant fight. `events.log` should show the killing blow as an ordinary hit, then "not counted … infinite HP" lines for hits during the wind-up. The Giant's final eruption should still be counted against players.
