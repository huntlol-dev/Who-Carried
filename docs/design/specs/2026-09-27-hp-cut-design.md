# HP cut by effects (Fur Coat), shown apart from damage

Date: 2026-09-27. Branch: `feature/hp-cut`.

## Problem

Fur Coat, an Ancient relic, marks up to 8 fights in an act. In those fights every enemy starts at 1 HP, including ones that join later. The game does this with `CreatureCmd.SetCurrentHp`, not damage, so the recap counts none of it. A marked elite with 150 HP shows 1 damage for the teammate who finished it and nothing for the player whose relic did the work.

## Decision

The owner chose to **show it, but not as damage**. It doesn't count toward damage dealt, share of the team's damage, the Timeline, the Heavy hitter award, or any other award.

- **What counts:** a player's content lowering a living enemy's HP by setting it directly, not by a hit. The amount is the HP it took away, `current − max(new, 0)`. Setting HP higher (an enemy restoring itself, an undying phase starting) isn't counted, and neither is anything done to an enemy showing infinite HP.
- **Whose it is:** the running effect's player when the game is running one of the hooks the mod already watches (Fur Coat's fight-start trigger is one). Otherwise, the nearest game content on the call stack, if exactly one player holds a relic of that kind. That's Fur Coat's trigger when an enemy joins mid-fight. An enemy setting its own HP names no player and isn't counted. None of this names Fur Coat, so a mod relic that sets enemy HP the same way shows up too.
- **Where it's kept:** a new per-player total, **HP cut**, by source, saved with the run.

## On screen

- **Sources tab:** under each player's banner, a quiet line when they have any: *"Fur Coat cut enemies' HP by 312. Not counted as damage."* With several sources they're joined ("Fur Coat and …"). The tab's damage bars and totals are unchanged. Solo runs show the same line in the player's column.
- Nothing on the scoreboard, the exported image or the copied picture.
- Log: `Ash cut 149 hp with Relic:FUR_COAT (Fur Coat) | target GREMLIN_LEADER`, read by the replay.

## Tests

- **Core:** the amount (lowered, raised, to 0, already 0); `RecordHpCut` keeps it apart from damage, fights and awards; the replay line reads back into HP cut and not damage.
- **Core:** the Sources view carries the line's parts; the text joins several sources.
- **Localization:** the new key in both files.
- **In a game:** a run with Fur Coat, through one marked fight.
