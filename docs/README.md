# Design docs

Recent investigations: [Hextech Burn's missing attribution](investigations/2026-09-18-hextech-burn-attribution.md) and the [generic source-attribution spike](investigations/2026-09-18-generic-attribution-spike.md).

Comment verification: [Osty tanking and Strength-reduction prevention](investigations/2026-09-18-osty-strength-prevention.md) and [co-op guests losing their stats when the host re-hosts](investigations/2026-09-22-guest-stats-reset-on-rehost.md).

[Zone the Spire's Hallowed getting no credit for its kills](investigations/2026-09-23-hallowed-direct-kills.md), and what else crediting direct kills touches.

[Localization](localization.md): how the recap follows the game's language, and keeping Simplified Chinese complete.

Later design: [absorb layers](design/specs/2026-09-20-absorb-layers-design.md) — damage a mod's armour stops between block and HP.

The specs and plans the mod was built from, in the order they were written (September 2026). They're a record of how it got here, so they aren't updated as the code changes.

**The mod was called "Run Recap" when these were written.** It was renamed **Who Carried?** before release, because another Workshop mod already uses that name. Where the docs say `RunRecap`, the code now says `WhoCarried`: `src\RunRecap\` is `src\WhoCarried\`, `<game>\mods\RunRecap\` is `<game>\mods\WhoCarried\`, and so on. `<game>` is your Slay the Spire 2 folder.

**Two later changes the docs don't show:**
- The mod's files (`current_run.dat`, `events.log`, the dev preview and replay flags and their screenshots) moved from `<game>\mods\WhoCarried\data\` to a `WhoCarried` folder in the game's save folder: `%APPDATA%\SlayTheSpire2\WhoCarried` on Windows, `~/.local/share/SlayTheSpire2/WhoCarried` on Linux, `~/Library/Application Support/SlayTheSpire2/WhoCarried` on a Mac. The mod moves an old `data` folder over on its first start.
- **Save image** puts the card in the player's Steam screenshots instead of `Pictures\Who Carried`.

They were written with Claude Code, which is why they mention Claude building, deploying and reading the logs.

| Spec | Plan | What it covers |
|---|---|---|
| [Run Recap — design](design/specs/2026-09-10-run-recap-design.md) | [plan](design/plans/2026-09-10-run-recap.md) | The first version: what gets counted, who gets credit, how events are captured without touching gameplay |
| [Visual redesign + PNG export](design/specs/2026-09-10-run-recap-visual-design.md) | [plan](design/plans/2026-09-10-run-recap-visual.md) | The game's own fonts and art, and saving the recap as an image |
| [Decks](design/specs/2026-09-10-run-recap-decks-design.md) | [plan](design/plans/2026-09-10-run-recap-decks.md) | Each player's deck at the end of the run |
| [Debuffs](design/specs/2026-09-10-run-recap-debuffs-design.md) | | Debuffs each player applied and received, and the extra damage their Vulnerable set up for the team |
| ["Post-match broadcast" redesign](design/specs/2026-09-10-run-recap-broadcast-design.md) | | A sports-broadcast look, later replaced |
| ["Dealt" redesign](design/specs/2026-09-12-run-recap-dealt-design.md) | | The card-table look the mod ships with |
| [Fair Poison and Doom split](design/specs/2026-09-12-poison-doom-split-design.md) | [plan](design/plans/2026-09-12-poison-doom-split.md) | Sharing Poison ticks and Doom kills by each player's part of the pile, instead of crediting whoever started it |
| [Top-bar button](design/specs/2026-09-13-top-bar-button-design.md) | [plan](design/plans/2026-09-13-top-bar-button.md) | A podium button next to Map and Deck that opens the recap, drawn to match the game's icons |
| [Controller support](design/specs/2026-09-13-controller-support-design.md) | [plan](design/plans/2026-09-13-controller-support.md) | Reach the podium from the top bar, and use every tab with a controller |
| [Rebindable hotkey](design/specs/2026-09-16-rebindable-hotkey-design.md) | [plan](design/plans/2026-09-16-rebindable-hotkey.md) | Changing the key that opens the recap from inside the game, kept in the mod's own settings file |
| [The recap's controls](design/specs/2026-09-17-recap-controls-design.md) | [plan](design/plans/2026-09-17-recap-controls.md) | The reward screen's stone out, a stone the mod draws in, and the hotkey and Close as keys the bar names |
| [Telling apart players on the same character](design/specs/2026-09-23-same-character-colours-design.md) | [plan](design/plans/2026-09-23-same-character-colours.md) | Shades of a shared colour, gaps in the climb, names on the Timeline's lines, and the scoreboard's portraits zoomed out |
