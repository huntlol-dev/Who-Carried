# Vanilla attribution fixes: caps, debuff amplifiers, Expose

Date: 2026-09-27. Branch: `bug/vanilla-attribution`.

A read-only audit of the base game (v0.111.0, every item also on v0.107.1) found three places where vanilla content produces wrong numbers. This spec fixes all three. Stats players don't get credit for today (protection, healing, Strength given, Fur Coat) have their own specs.

## 1. Hit caps counted as enemy block knocked off

### Problem

The armour-layer measurement ([absorb layers](2026-09-20-absorb-layers-design.md)) counts whatever `Hook.ModifyHpLost` takes off a hit, and on an enemy it adds that to the enemy block the hit knocked off. Its spec assumed "a run without such a mod records zero absorbed". That isn't true. The base game reduces HP loss in the same hook with two enemy powers that are caps, not armour:

| Power | Enemy | What it does |
|---|---|---|
| Slippery | Vantom (Act 1 boss, 8–9 stacks per player), Inklets | Each hit takes at most 1 HP |
| Hardened Shell | Skulking Colony (elite, 20 per player) | HP lost per turn is capped |

A 25-damage hit on Vantom takes 1 HP and logs 24 as block knocked off. Over the fight that's hundreds of fake block, and the **Siege breaker** award goes to whoever hit Vantom most. Intangible on an enemy acts on the same hook, but its damage cap applies earlier, so it never leaves anything to measure.

### Decision

**On an enemy, what the game's own content takes off a hit isn't counted.** A reduction counts as armour only when no model from the game's own assembly is among the models the game says changed that HP loss. The game's content has no enemy armour, only caps; mods' layers (the case the absorb spec was written for) keep counting.

- The rule is read per `ModifyHpLost` call (one phase). Real damage settles each phase in its own call.
- If a game-owned cap and a mod's layer both act in the same call, that call's reduction is dropped. The `modifiers` list says which models acted, not how much each took, so it can't be split. This is rare, and it errs toward not inflating Siege breaker.
- **Players are unchanged.** Buffer, Tungsten Rod, Beating Remnant and Intangible on a player still count as that player's block, as the absorb spec decided.
- A dropped reduction is still written to the log (`caps on X ate N hp (SLIPPERY_POWER), not counted`) for diagnosis.
- The hit that uses up Hardened Shell's cap is covered here. Hits after it are covered by [infinite-HP phases](2026-09-27-infinite-hp-phase-design.md).

This doesn't name any power: "is this model the game's own" is `model.GetType().Assembly == typeof(AbstractModel).Assembly`.

## 2. Vulnerable and Weak amplifiers credited to the wrong player

### Problem

The base game strengthens Vulnerable and Weak from inside their own damage calculation:

| Amplifier | Whose | Changes |
|---|---|---|
| Debilitate (debuff on the enemy) | whoever applied Debilitate | Vulnerable ×1.5 → ×2.0; Weak ×0.75 → ×0.5 (doubles the effect) |
| Paper Phrog (relic) | the hitter | Vulnerable +0.25 |
| Cruelty (power) | the hitter (or a pet's owner) | Vulnerable + Cruelty% |
| Paper Krane (relic) | the player being hit | Weak −0.15 |

The mod asks each debuff for its multiplier, so it sees one bigger Vulnerable or Weak and gives all the extra to whoever applied it. Players reported this for Debilitate, and they're right.

### Decision

**Each amplifier's part of the multiplier goes to whoever it belongs to.** Vulnerable's excess over ×1 is split additively into parts:

- **Vulnerable's own:** its base increase (0.5), to the Vulnerable appliers (as today).
- **Hitter's own** (Paper Phrog, Cruelty): dropped. It's already in the hitter's damage, the same reason the hitter's own share of Vulnerable is dropped today.
- **Debilitate's:** what Debilitate added, which doubles everything before it, to Debilitate's appliers by their stacks still on the enemy.

Example: base 1.5, Paper Phrog → 1.75, Debilitate → 2.5. The parts are 0.5 (Vulnerable), 0.25 (hitter, dropped) and 0.75 (Debilitate).

Weak's reduction below ×1 splits the same way: Weak's base reduction (0.25) to the Weak appliers, Paper Krane's (0.15) dropped as the victim's own relic, and Debilitate's to Debilitate's appliers. Debilitate's credit is listed under Debilitate in the Debuffs tab. It counts toward bonus damage and **Enabler** for Vulnerable, and toward damage prevented and **Protector** for Weak.

The parts are worked out by calling the game's own public methods, in the game's order: `PaperPhrog.ModifyVulnerableMultiplier`, `CrueltyPower.ModifyVulnerableMultiplier`, `DebilitatePower.ModifyVulnerableMultiplier`, and `PaperKrane.ModifyWeakMultiplier`, `DebilitatePower.ModifyWeakMultiplier`. **If the result differs from the multiplier the game itself returned** (a game version with different rules, or a mod changing Vulnerable), the hit falls back to today's behaviour: all of it to the Vulnerable or Weak appliers. These are vanilla types; naming them is allowed, since the no-hard-coding rule is about other mods.

## 3. Expose's block strip

### Problem

Expose removes all of an enemy's block through `CreatureCmd.LoseBlock`, not through damage, so it never counts as block knocked off.

### Decision

**Block a player strips from an enemy without damage counts as block knocked off**, credited to the player named as the remover and listed under the card or effect on top of the action's model stack (Expose). Only the block the enemy actually had counts. On the beta, the game's `remover` argument says who did it, so this names no card. The public version has no remover or context argument: credit requires the nearest calling content to match a live turn effect or the current card's context. An unproven caller gets no credit. Enemies stripping their own block (Burrowed) aren't attributed to an unrelated player's action. Nothing is counted on an enemy showing infinite HP, or while combat is over or ending. The amount follows the game's rounding of the block remaining.

It's logged as an ordinary hit line with 0 HP, so replays read it:

```
[F12 A1] Moth <- Card:EXPOSE (Expose) 0 hp | target CULTIST, blocked 14, dealer player Moth, stack [EXPOSE]
```

## Replays and saves

The new log lines (Debilitate bonus and prevention, Expose block) use existing formats, so replays of new logs match the live recap. Old logs and saves keep their old numbers: they don't record which parts of a multiplier came from where, or which layer ate what.

## Tests

- **Core:** splitting a debuff's HP bonus between its parts (Vulnerable, dropped hitter part, Debilitate), with exact whole-number shares that add up; the same for Weak; a part of 0 gets nothing.
- **Core:** the absorb rule: a measured reduction with a game-owned model in its list isn't counted on an enemy, and is on a player; a quiet or mod-only reduction still counts on an enemy.
- **Core:** a 0-HP hit line with block replays as block removed (already covered by the existing replay test; kept).
- **Build** against both game versions.
- **In a game:** a Vantom or Inklets fight (no "block knocked off" growth beyond real block); a Necrobinder with Debilitate beside a Vulnerable teammate (`+N bonus via DEBILITATE_POWER` lines); Expose on a blocking enemy.
