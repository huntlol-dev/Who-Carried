# Deaths on the card, gold in the Decks tab

Date: 2026-09-26. Draft for review. Plan: [deaths and gold](../plans/2026-09-26-deaths-and-gold.md).

## Intent

Two new per-player numbers, both working in co-op:

- **Deaths:** how many times each player died, including the wipe that loses a run. It goes on their scoreboard card as its own line under the bonus damage, with the game's skull icon, so it's obvious at a glance (mockup option C, chosen by the owner).
- **Gold earned:** how much gold each player picked up over the run. It goes in the Decks tab's header row for that player, next to the card count and type chips.

No awards for either (the owner's call). Everything else on the recap stays as it is.

## What the game does

**In co-op, a death usually isn't the end.** When a co-op player reaches 0 HP, they are out for the rest of that fight. If a teammate is still standing when the fight ends, the game brings them back with 1 HP (`Player.ReviveBeforeCombatEnd`, run for every player from `CombatManager.EndCombatInternal`). When everyone is dead, the run is lost. In solo, a death ends the run. A death the game prevents, like Fairy in a Bottle's, isn't a death: the player never goes down.

Every death goes through `CreatureCmd.Kill`, which raises `Hook.AfterDeath(runState, combatState, creature, wasRemovalPrevented, deathAnimLength)`. `wasRemovalPrevented` is false for a real death and true when something like Fairy in a Bottle stopped it. After the last player dies, `Kill` sees that everyone is dead and ends the run as a defeat (`RunManager.OnEnded(isVictory: false)`). The wipe's deaths are raised before that. The game doesn't record deaths in its run history, so they can only be counted live.

**The game also kills everyone when a run is over.** Winning goes through `RunManager.WinRun`, reached from the Architect's final option (`TheArchitect.WinRun`). It first ends the run as a victory (`OnEnded(isVictory: true)`), then calls `GuaranteeKillAllPlayers`, which force-kills every player. Abandoning a run sets `RunManager.IsAbandoned` and calls the same routine ("When you Abandon a Run you are sentenced to death"). These forced kills raise `Hook.AfterDeath` like real deaths, so they have to be told apart. (They're also why a won run later reports a second, defeated ending, which the tracker already ignores.)

**Gold is already recorded per player.** `PlayerCmd.GainGold` adds every gain, after the game's modifiers, to that player's `GoldGained` on the current map point, and the run history saves it as `gold_gained` in each floor's `player_stats`. Starting gold isn't counted. Gold a player gets back from a thief is booked against `gold_stolen` instead, so it isn't counted either. On a real 4-player save (`1790416332.run`), summing `gold_gained` gives 1,072, 755, 715 and 651.

## Deaths

### What counts

Every time the game kills a player counts, the wipe that loses the run included. The only exceptions are the kills the game does to end a run that's already over, and deaths that never happened. If everyone died three times, including the wipe, every card says "Died 3 times" (the owner's rule).

| Situation | Counted |
|---|---|
| A co-op player dies, the others win the fight, they're revived | Yes, 1 |
| Two players die in the same fight | Yes, 1 each |
| A player dies twice in one fight (a mod revives them mid-fight) | Yes, 2 |
| The wipe: every player who dies in the fight that loses the run, the last one included | Yes, 1 each |
| A solo player dies and the run is lost | Yes, 1 |
| Winning: the game kills everyone after the Architect, once the run is already won | No |
| Abandoning: the game kills everyone to end the abandoned run | No |
| Fairy in a Bottle, or any other effect that stops the death | No |
| Pets (Osty) and enemies | No: players only |

### How it's counted

- **Capture:** a Harmony prefix on `Hook.AfterDeath`, taking `Creature creature` and `bool wasRemovalPrevented`. It passes on a death only when it wasn't prevented and the creature is a player (`creature.IsPlayer`, with `creature.Player` set). The tracker then counts it straight away with `RunStats.RecordDeath`, writes `"{where} {name} died"` to `events.log`, and refreshes an open recap.
- **Skipped once the run is over:** if the run has already ended (`RunStats.Finished`, set when the game reports the run's end), or the game is abandoning it (`RunManager.Instance.IsAbandoned`), the death isn't counted. It's logged as `"{where} {name} killed as the run ends, not counted"`. A win reports its end before the forced kills, so every one of them is skipped. The wipe's deaths come before the defeat is reported, so all of them count.
- **Every client counts for itself.** Each player's game runs every fight, so a host and guests with the mod all see the same deaths, as they already do for damage.
- **Storage:** `PlayerTotals.Deaths`, saved in `current_run.dat` with everything else: at the end of each fight and when the run ends. A death in a fight that's quit halfway is lost with that fight, like its damage, since the game replays the fight on reload. Saves from before this change load with 0.
- **Replay:** `LogReplay` counts the `died` lines, so `replay.flag` rebuilds deaths too. It ignores the `not counted` lines. Logs from before this change have none.
- **Timing:** a death shows on the card straight away, mid-fight included. Deaths from before the mod was installed, or before this update, can't be recovered.

### Game versions

`Hook.AfterDeath` with these parameter names is confirmed in the installed beta (v0.111). The public branch's DLL (v0.107) isn't on this machine, so it can't be checked here. `ModEntry` applies each patch class on its own and logs a failure without stopping the others. If the public branch's `AfterDeath` differs, deaths simply stay at 0 there, and nothing else is affected.

### On the card

- **Placement:** its own line, directly under the bonus damage line, in the card's text box. It shows the game's skull (`GameArt.Skull`, the icon the Punching bag award already uses) at the bonus line's icon size (1.2 em), then the text at the bonus line's size (0.92 em).
- **Wording:** "Died once" for 1 and "Died {0} times" otherwise. The whole line is one colour, the same off-white as the bonus line. The mockup showed the number in red, but the localization rules keep a sentence as one template and never split it into fragments. The bonus line already follows that rule and shows no coloured number either.
- **Hidden at 0.** In solo it only appears on a lost run, as "Died once".
- **Room:** the text box is about 9.8 em tall and today's lines use about 8.3 em; the new line needs about 1.6 em. The gap above the chips goes from 0.7 em to 0.4 em, the bonus line's from 0.45 em to 0.25 em, and the deaths line gets 0.25 em (tightened from a first try of 0.5, 0.3 and 0.3 after the in-game check). The damage number, caption, chips and all font sizes stay the same.
- **Chinese (open):** Chinese text is taller, and on a card with both a bonus line and a deaths line the damage number pokes above the text box (about 23 px on 5-player cards and 18 px on 4-player ones, at 2560×1440). Chinese bonus cards already sat about 5 px over before this change. Fixing it needs a call against the rules above, like a smaller number on those cards or tighter gaps in Chinese only.
- **Layout shift:** the text box centres its lines vertically. A card with a deaths line has its damage number about half a line higher than a card without one. The bonus line already behaves this way when a player has no bonus damage.
- **Export:** the saved image builds its player cards with the same `ScoreboardTab.PlayerCard`, so the deaths line appears there too.
- **Data:** `BarRow` gains `Deaths` (default 0), filled from `PlayerTotals.Deaths` for scoreboard rows.

## Gold earned

### What counts

The sum of `gold_gained` over every floor so far, per player: combat rewards, events, relics and cards that give gold, after the game's modifiers. Starting gold doesn't count, and nothing is subtracted for gold spent, lost or stolen. The label says "gold earned" to make that clear.

### Where it comes from

Both paths already read the game's per-floor history for the Defense tab:

- **Live** (mid-run and at the run's end): `GameReader.Defense` walks `run.MapPointHistory`; it also sums `PlayerStats.GoldGained`.
- **Saved runs** (replay): `RunHistory.Parse` also sums `gold_gained` into `PlayerRecord`.

The total travels on `DefenseTotals`, which becomes the record for everything the recap takes from the game's per-floor history. It gains `Gold` (default 0), and its doc comment says so. It isn't renamed, to keep the change small. `RecapBuilder` passes these totals to `DeckBuilder.Build`, which puts each player's total on `DeckView.Gold` (default 0).

Because it comes from the game's own history, gold is right after a reload. It's also right for co-op guests, and for runs started before the mod was installed.

### On screen

- **Decks tab:** in the header row, after the type chips and before the "14 different · …" note, add the game's gold coin (the one card text uses inline, `res://images/packed/sprite_fonts/gold_icon.png`, as `GameArt.Gold`) and "{0} gold earned". It's one template, in gold, at the card count's size (18, bold). Hidden when 0.
- **Export:** the saved image's Decks section shows each player's deck as a column under their name. Add a "{0} gold earned" line under the name, at the column's 14 px text size, so the export and the tab match. Hidden when 0.

## Localization

New keys in `eng.json` and `zhs.json`, in the same change:

| Key | English | 简体中文 |
|---|---|---|
| `WHO_CARRIED.stat.deaths_one` | Died once | 阵亡 1 次 |
| `WHO_CARRIED.stat.deaths` | Died {0} times | 阵亡 {0} 次 |
| `WHO_CARRIED.decks.gold` | {0} gold earned | 获得 {0} 金币 |

金币 is the game's own word for gold.

## Developer preview

`DevPreview.BuildSample` gives the sample run deaths (2 for the second player, 1 for the fourth, none for the others) and gold for every player. The preview then shows cards with and without the line side by side, plus the gold in the Decks tab and the export. The sample run is a won one, so the single-player preview (`preview.flag` = `1`) shows no deaths line.

## Testing

**Core tests** (console runner, no game):

- `RunStats.RecordDeath` adds up, and ignores counts of 0 or less.
- `RunStatsStore` keeps `Deaths` through a save and load, and an old save without the field loads as 0.
- `RunHistory.Parse` sums `gold_gained` per player across floors.
- `DeckBuilder.Build` puts each player's gold on their `DeckView`, and 0 when the history has none.
- `RecapBuilder` puts each player's deaths on their scoreboard `BarRow`.
- `LogReplay` turns `died` lines into deaths, and ignores `killed as the run ends, not counted` lines.
- `LocalizationTests` already fail if the two catalogs' keys differ.

**In the game** (preview flag, compared with the "before" screenshots taken on 2026-09-26 from the current build):

- 4 players, English: the deaths line sits under the bonus damage on the cards that have deaths. Nothing overflows the text box or runs into the badges, and the damage number is the same size as before.
- 5 players (`IRONCLAD,SILENT,REGENT,NECROBINDER,DEFECT`), English and Chinese (`… zhs`): the same checks on the narrowest cards.
- 1 player: no deaths line; the card looks as it did before.
- Decks tab and export: the coin and "gold earned" appear for each player, in both languages.

**Real runs (pending, needs the owner):**
- A co-op run where a teammate dies and is revived: `events.log` shows `died` and the card shows "Died once" straight away.
- A run lost to a wipe: every player gets a `died` line, and each card counts that death.
- A won run: after the Architect, `events.log` shows a `killed as the run ends, not counted` line for each player, and the cards' death counts don't change.

A solo run can check the last two without the group.

## Out of scope

- Awards for deaths or gold.
- Gold spent, lost or stolen.
- Recovering deaths for runs played before this update.
- Renaming `DefenseTotals`.
