# Changelog

[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format, [semver](https://semver.org/spec/v2.0.0.html) numbers.

## [Unreleased]

### Added

- Simplified Chinese, following the game's language. Contributed by MerakW (#1).
- **Support** tab: the energy, cards, block, buffs and draws players gave teammates, also on the exported image. Five awards go with it: **Battery**, **Care package**, **Bodyguard**, **Coach** and **Playmaker**.
- Defense: damage a pet takes for its owner shows as **tanked by pets**.
- Players on the same character get lighter shades of its colour, so they can be told apart.
- Fairer credit for modded effects: extra armour layers count as block, outright kills count as the effect's damage, and debuff stacks that turn into another debuff keep whoever applied them.
- Experimental, off by default: `"experimentalEffectSources": true` in `settings.json` credits modded damage with no dealer to the effect that dealt it, instead of Unknown.

### Changed

- The scoreboard cards show more of each character's portrait.

### Fixed

- Co-op guests no longer lose earlier fights when the host reloads the run.
- Strength-down gets credit for an enemy attack it takes to 0.
- Changing the hotkey no longer resets the rest of `settings.json`.
- Logs show the mod's real version, not `v0.1.0`.

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
