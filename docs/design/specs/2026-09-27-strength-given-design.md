# Damage from Strength you gave

Date: 2026-09-27. Branch: `feature/strength-given`.

## Problem

Strength given to a teammate is counted only as buff stacks. The damage it adds to their attacks goes to them alone:

| Source | How it reaches a teammate |
|---|---|
| Blaze, Coordinate (cards) | Strength, or Strength for the turn, on a chosen teammate |
| Strength Potion, Flex Potion, Fysh Oil (thrown at a teammate) | Strength, or Strength for the turn |
| Mazaleth's Gift (thrown at a teammate) | Ritual: the teammate gains Strength every turn |

Vulnerable already has the right shape: the extra damage teammates deal because of your Vulnerable is your **bonus damage**, shown beside your own damage and counted for **Enabler**. Strength you gave should work the same way.

## Decision

**The extra HP a teammate's attack removed because of Strength you gave them is your bonus damage.** It's added to the same bonus number as Vulnerable's, and to Enabler.

### Whose Strength it is

Each player (and pet) keeps a ledger of Strength other players gave them:

- **Lasting Strength:** a positive Strength change whose applier is another player is theirs.
  - A player's own Strength gain while a teammate's buff on them is acting at a turn boundary (Ritual from Mazaleth's Gift) belongs to that teammate. The mod already tracks the running effect for this.
  - Strength that comes with a temporary Strength buff (Coordinate, Flex Potion) is taken back out of the lasting ledger, because that buff is read live instead. Its wear-off names the player themselves as applier, so it doesn't touch the ledger. This is the same arrangement the Strength-down tracking uses for temporary debuffs.
- **Temporary Strength:** read live at each hit from the buffs on the attacker whose applier is another player.
- **Limit:** gifts never count for more than the attacker's actual Strength. If Strength went down (a self-inflicted Strength loss, an enemy's Strength drain), gifted Strength is scaled down in proportion, and at 0 or below it counts for nothing.

### How much

Only powered attacks, since Strength only changes those. For the hit:

- **Gifted Strength** S, shared by giver as above.
- **Its multiplier:** every damage multiplier on the hit *except* the debuffs whose bonus is already credited (Vulnerable and the like). Those claim their share of the whole hit, Strength included. The two claims then add up exactly to their joint effect, and nothing is claimed twice.
- **Bonus:** HP the hit removed minus HP it would have removed with S × that multiplier less, after the same block, capped by the HP it actually removed. It's split between givers by their Strength, with exact ties taking turns.
- A hit on an enemy that doesn't count (an infinite-HP phase) gives no bonus.

## On screen

- Scoreboard: each player's **+N bonus damage** now includes Strength they gave. The README's line changes from "the bonus damage their Vulnerable set up" to "the bonus damage their Vulnerable and the Strength they gave set up".
- **Enabler:** the most bonus damage, Vulnerable-style debuffs and Strength together. Its detail names them ("damage teammates gained from their Vulnerable and Strength").
- The summary note under the scoreboard names Strength the same way.
- The Debuffs tab is unchanged: Strength isn't a debuff.
- Log: `Ash +4 buff bonus via STRENGTH_POWER (Strength) on Moth's hit (+3 Strength, x1, 13 hp)`, read by the replay.

## Not doing

- Other buffs that add damage. Vigor isn't given to teammates in the base game; mods' damage buffs could be added later the same way.
- Dexterity given to teammates and the block it adds. That's a separate, block-side version of this.
- Old logs and saves keep their numbers.

## Tests

- **Core:** the bonus for a hit, with block, and on a killing blow capped at HP removed; no bonus with S = 0.
- **Core:** scaling gifts to the attacker's real Strength, and splitting between givers.
- **Core:** no double count: a Vulnerable bonus plus a Strength bonus on one hit add up to the hit minus the hit without both.
- **Core:** `BuffBonus` counts in the scoreboard's bonus and in Enabler; its replay line reads back.
- **In a game:** Blaze or a Strength Potion on a teammate, then their attack, then a Vulnerable enemy.
