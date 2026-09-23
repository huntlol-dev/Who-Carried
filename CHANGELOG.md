# Changelog

[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format, [semver](https://semver.org/spec/v2.0.0.html) numbers.

## [Unreleased]

### Added

- Simplified Chinese: the recap, the exported image, the hotkey prompts and the top-bar button follow the game's language, in the game's Chinese font. In other languages the mod's own words stay English beside the game's own names for cards, relics and powers. Contributed by MerakW (#1).
- **Support**, a new tab headed *Given to teammates*: a card each for the energy, cards, block, buff stacks and draws players gave each other (Believe In You, Glimpse Beyond, Rally, Blaze, Huddle Up…), with a bar per player, most first, and the award it won. Help a player gives themselves isn't counted, and kinds nobody gave are left out. The exported image gets the same cards in one row when anyone gave a teammate anything. Five awards go with it: **Battery**, **Care package**, **Bodyguard**, **Coach** and **Playmaker**, and the Awards tab takes a third row when it needs one.
- Defense: HP a pet loses to enemies (Osty taking hits for the Necrobinder) shows as **tanked by pets**, on its owner's nameplate, in the team totals and on the exported image. It isn't part of the owner's damage taken.
- Experimental, off by default: `"experimentalEffectSources": true` in `settings.json` credits damage a modded effect deals with no dealer and no card (Hextech Runes' Burn, for one) to that effect and whoever applied it, instead of Unknown. Such a tick on a poisoned enemy is no longer counted as Poison. Read at start-up.
- Damage a mod's armour stops between block and HP (Zone the Spire's Marbled) is counted as block: chipped off an enemy it counts as enemy block knocked off, under the card that did it; soaked on a player or their pet it counts as that player's damage blocked. Measured at the game's own HP-loss hook, so any mod's layer counts, named or not.
- An enemy a modded effect kills outright at the start or end of a turn, the way Doom does (Zone the Spire's Hallowed), counts its remaining HP as that effect's damage, shared by who applied the stacks.
- A debuff that turns part of itself into another (Hallowed into Doom) hands the new stacks to whoever applied it, so a Doom kill credits the Hallowed's players in co-op. They don't count as Doom applied.
- Players on the same character can be told apart. Everyone after the first gets a lighter shade of the character's colour, on every tab, the cards and the exported image. The climb has a gap between each player's part of every bar, and the Timeline names each line at its end.

### Changed

- The scoreboard cards show more of each character's portrait.
- Behind the scenes, with no change to the recap: the rules for who gets credit for an outright kill or for converted stacks are covered by automated tests, and everything the mod remembers about a fight is wiped in one step as it starts and ends.

### Fixed

- Co-op guests no longer lose the recap's earlier fights when the host reloads the run. It hit guests on the game's public branch, the first time a run was reloaded.
- Strength-down (Enfeebling Touch, Piercing Wail, Malaise) gets credit for an enemy attack it takes all the way to 0: the attack's own size, not the Strength removed.
- Changing the hotkey keeps the rest of `settings.json`.
- `events.log` and the game's log give the mod's real version, not `v0.1.0`.

## [1.1.0] — 2026-09-17

### Added

- Rebindable hotkey: click the key on the recap's top bar and press the one you want. Esc cancels, Delete clears. Saved in `settings.json`; still F8 by default.

### Changed

- The recap's controls are drawn by the mod now, not borrowed from the reward screen.
- **Save image** is **Export as image**, and has moved from the top-right corner to beside the tabs.
- The hotkey is a key cap in the top bar; **Close** is a word wearing the game's cancel key.

### Fixed

- Opening the recap no longer risks clicking **Save image** underneath it.
- *Saved to your Steam screenshots* no longer runs under the Close button.
- Buttons keep their shape at every width, and an icon no longer stretches one taller.
- The controller's confirm glyph appears on the export button.

## [1.0.0] — 2026-09-15

First Workshop release: the co-op recap in seven views, live while you play; the podium button and F8; Save image to Steam screenshots; controller support; Poison and Doom split by share of the pile.
