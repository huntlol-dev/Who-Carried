# Support stats — what players give each other

Date: 2026-09-23. Branch: `feature/support-stats`.

Damage and debuffs are already counted; help given straight to a teammate isn't. A co-op player who spends their turns handing out energy, block and cards shows up as dead weight. This adds five support stats, a Support tab, a Support section on the exported image, and one award per stat.

## What counts as support

**A player gives something, a different player receives it, during a fight.** Help to yourself never counts. Help landing on a teammate's pet (Osty) counts for that teammate. When the giver can't be worked out, the gift isn't counted and the log says so.

| Stat | Counted as | Caught at | Giver | Vanilla examples |
|---|---|---|---|---|
| Energy given | Energy that landed, after the game's modifiers | Prefix on `PlayerCombatState.GainEnergy(decimal)`, the one place `PlayerCmd.GainEnergy` hands over the final amount; the recipient is the state's private `_player` | See *Finding the giver* | Believe In You, Constellation, Energy Surge; Energy Potion, Cure All, Radiant Tincture thrown at a teammate |
| Cards given | Each generated card whose owner isn't its creator | The existing `AfterCardGeneratedForCombat` patch (`card.Owner` vs `creator`) | `creator` | Glimpse Beyond (Souls for each teammate), Largesse |
| Block given | Block gained, after modifiers | New postfix on `Hook.AfterBlockGained` (`creature`, `amount` is the final value, `cardSource`) | `cardSource.Owner`, else *Finding the giver* | Demonic Shield, Lift, Rally |
| Buffs given | Stacks of `PowerType.Buff` powers landing on a teammate, summed over all powers | The existing `AfterPowerAmountChanged` patch: positive amount, buff type, target a player (or their pet), applier a different player | `applier`'s player | Blaze (Strength), Fade, Coordinate, Soulbound, One For All |
| Cards drawn for teammates | Cards a teammate drew that weren't their normal hand draw | New prefix on `Hook.AfterCardDrawn`, skipping `fromHandDraw`; the recipient is `card.Owner` | The top of the `choiceContext`'s model stack, if it's a card; else *Finding the giver* | Huddle Up (draws through `DrawWithoutBlockingOnOtherPlayers`, which reaches `AfterCardDrawn`) |

The existing **cards created** count (Sources tab, Card factory award) doesn't change. It still counts every card a player makes, whoever gets it. **Cards given** is a separate count of the subset that went to a teammate.

### Finding the giver

Energy and draws don't come with a giver, and block only has one when a card gave it. In order:

1. **A turn hook is running** (`EffectSources.Running` is a `PowerModel`): the power's `Applier`'s player. This covers energy-next-turn powers and Radiance, which pay out at the start of the recipient's turn.
2. **Exactly one player's card or potion is taking effect.** The game counts this per player: `CombatManager.IsExecutingCardOrPotionEffect(Player)` is true from just before a card's `OnPlay` (or a potion's `OnUse`) until its effect is done, nested auto-plays included. If exactly one player in the run has one running, they're the giver. If two do (one player's card set off another's), nobody is, so the mod doesn't guess.
3. **Only if the game lacks that method** (it's looked up by name in `GameCompat`, like the other APIs that differ between branches): the running action's `OwnerId` (`RunManager.Instance.ActionExecutor.CurrentlyRunningAction`), which `PlayCardAction` and `UsePotionAction` set to their player.
4. **None of these:** no giver. Not counted; the log gets a `support: no giver` line naming the kind, amount and recipient.

The order is a pure rule in `Core/SupportCredit.cs`, so it's unit-tested. `Game/SupportGiver.cs` gathers the four inputs from the game.

Accepted limit: a card's effect bracket also covers the game's after-play hooks. So if Alice's card sets off Bob's own relic and that relic gives Bob energy, it's credited to Alice. Nobody has checked how often vanilla does this. The `gave` log lines name what was running, so the in-game check below can spot it.

## Stats (Core)

`PlayerTotals` gets five `int` counters: `EnergyGiven`, `CardsGiven`, `BlockGiven`, `BuffsGiven`, `CardsDrawnForTeam`. An older `current_run.dat` loads them as 0.

`RunStats.RecordSupport(ulong giver, ulong recipient, SupportKind kind, int amount)` adds to the giver's counter. It ignores `giver == recipient` and amounts ≤ 0, so the self-help rule is in one tested place. `SupportKind` is an enum: `Energy`, `Cards`, `Block`, `Buffs`, `Draws`.

## Recap view

`RecapBuilder` adds to `RecapView`:

- `IReadOnlyList<SupportRow> Support`: one row per player in scoreboard order, holding the name, colour, character icon key and the five totals. There's no unattributed row.
- `bool HasSupport`: true when any player's total is above zero.

Totals only: no breakdown by source or by recipient.

## Support tab

The eighth tab, placed after Debuffs: `Scoreboard, Awards, Sources, Debuffs, Support, Timeline, Defense, Decks`. `RecapPanel.Views`, the pad-tab array and `DevPreview.TabNames` all gain the entry.

- One card per kind of help, styled like the Debuffs tab's cards, three across. The cards sit under one heading, "Given to teammates", and each is titled with just its kind: "Energy", "Block"… (players found a bare "block" confusing, so the heading says *given* once rather than every card repeating it). Beside the title is the team's total, then the award that kind won and its winner, then one bar per player who gave any, in their colour, longest first.
- Kinds nobody gave are left out, so a sparse run shows a few full cards, not rows of zeros. (A first version had one plate per player with five numbers each. It looked empty, and on the image it repeated the Defense section's portraits.)
- Icons are the awards' pictures: energy, `GameArt.Cards`, `GameArt.Block`, Strength's power icon (`DebuffBuilder.IconPrefix + "STRENGTH_POWER"`), and a new `GameArt` entry for the combat draw pile, falling back to `GameArt.Deck` if its path isn't found in the game's resources. Energy wears the leading giver's own energy gem, since the colourless one is a grey orb.
- The tab is always there, so tab positions and bumper navigation don't depend on the run. When `HasSupport` is false it shows one line instead of the cards:
  - Co-op: "Help given to teammates shows here. Nothing yet."
  - Solo: "Support is what players give their teammates in co-op."
- Updates live from `Tracker.Changed`, like the other tabs.

## Exported image

`SummaryCard` gets a **Support** section after Debuffs: the same cards made small, in one row, as many across as there are kinds given, headed "Given to teammates" like the tab, and no award line. No portraits, so the image doesn't show each player's face twice. **It's left out entirely when `HasSupport` is false**: in solo runs, and in co-op runs where nobody gave anything. The new awards appear in the image's Awards section like any others, and only when won.

## Awards

Five new awards, **co-op only** (after the `if (!team) return awards;` line), each to the player with the most. Ties go to the higher-ranked player, and each needs a minimum:

| Key | Title | Stat | Minimum | Detail |
|---|---|---|---|---|
| `WHO_CARRIED.award.battery` | Battery | Energy given | 2 | energy given to teammates |
| `WHO_CARRIED.award.care_package` | Care package | Cards given | 3 | cards given to teammates |
| `WHO_CARRIED.award.bodyguard` | Bodyguard | Block given | 10 | block given to teammates |
| `WHO_CARRIED.award.coach` | Coach | Buffs given | 3 | buffs given to teammates |
| `WHO_CARRIED.award.playmaker` | Playmaker | Cards drawn for teammates | 3 | cards teammates drew |

- The minimums are constants beside `MinCardsCreated`, to tune after play.
- **Order:** straight after Protector, before Wall. A player's first award is their scoreboard headline, so a support player's headline is their support title rather than Wall or Siege breaker.
- **Art** (`RecapTexts.AwardArt`), the same five icons as the Support plates: energy for Battery, cards for Care package, block for Bodyguard, Strength for Coach, the draw pile for Playmaker.
- **Awards tab layout:** `AwardsTab.Grid` uses two rows at most, so sixteen awards would squeeze each card to about 120 px. It changes to one row up to 5 awards, two up to 10, three beyond, with the card width capped so the rows also fit the spread's 640 px height. Up to ten awards lay out exactly as today. The sizing moves to a pure `Core/AwardGrid.cs` so it's unit-tested.

## Log and replay

Each gift writes one line to `events.log`:

```
[F12 A2] Alice gave 2 energy to Bob | BELIEVE_IN_YOU
[F12 A2] Alice gave 8 block to Bob | RALLY
```

Kinds, as written: `energy`, `cards`, `block`, `buffs`, `draws`. After the `|` comes the id of the card, potion or power that gave it, or `?`. `LogReplay` reads these lines back into `RecordSupport`, so `replay.flag` rebuilds the Support tab. Logs from older versions have no such lines, and replay leaves the stats empty, as for other stats they never recorded. The line format lives in `LogReplay.SupportLine`, like `PetTookLine`.

## Localization

Every new string goes into both `eng.json` and `zhs.json`, in key order (see `docs/localization.md`):

- The tab name.
- The five stat labels.
- The two empty-state lines and the image section heading.
- Five award titles and five details.

The Chinese uses the game's own words: 能量 energy, 格挡 block, 抽牌 draw, 力量 Strength. Where the game has the word itself, it's fetched with `GameText.Native`.

## Changelog

`CHANGELOG.md` `[Unreleased]` → Added, and the same entry in the 1.2.0 draft change note (`Who-Carried-workshop\changenote-1.2.0-draft.txt`). Nothing is uploaded.

## Testing

Unit tests (`tests/WhoCarried.Tests`, Core only):

- `RecordSupport`: adds to the giver's counter for each kind; ignores self-gifts and zero or negative amounts.
- Awards: each goes to the leader; isn't given below its minimum; ties go to the higher-ranked player; none of the five are given in a solo run; they sit after Protector in the award order.
- `RecapBuilder`: one Support row per player in scoreboard order; `HasSupport` is false in solo and when every total is zero.
- `LogReplay`: `SupportLine` output parses back to the same `RecordSupport` calls; unknown player names are skipped.
- `LocalizationTests`: already fails if the catalogs' keys differ or a key goes unused.

In the game (the Game layer has no tests):

- Energy: Believe In You on a teammate, Energy Surge, an Energy Potion thrown at a teammate. Each credits the player of the card or potion, and the recipient's own gains don't count.
- Cards: Glimpse Beyond credits Souls to the caster as cards given; the teammate's own Soul generation doesn't count.
- Block: Rally or Demonic Shield credit the caster; a player's own Defend doesn't count.
- Buffs: Blaze on a teammate counts; the teammate's own Inflame doesn't.
- Draws: Huddle Up counts; start-of-turn hand draw doesn't.
- `events.log` has a `gave` line for each, and no `support: no giver` lines in a vanilla run.
- `preview.flag` screenshots for party size 1 and 4: in solo, the Support tab shows its hint and the image has no Support section; with four players, the plates show and the Awards tab fits.

## Deliberately not doing

- **A breakdown by source or by recipient.** Totals only, by choice. The log keeps both, for debugging.
- **Tutor.** It moves a card from a teammate's draw pile to their hand, which isn't a draw (`CardPileCmd.Add`, no `AfterCardDrawn`). Counting it would need a patch just for it.
- **Healing and potions given.** Rare in vanilla; left out.
- **How much of the block given actually stopped damage.** Block pools on the creature with no memory of who gave which point; splitting it would be a ledger of its own.
- **Stars, summons (Legion of Bone), Intercept-style redirects.** Not asked for; each could be one more `SupportKind` later.
