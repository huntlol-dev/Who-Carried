# Who Carried?

A Slay the Spire 2 mod that settles the argument at the end of every co-op run: who carried, and who was dead weight.

When a run ends, the recap opens over the victory or defeat screen and deals every player out as a card, ranked by damage, with awards for the things damage doesn't show. It can be opened mid-run too, with **F8** (rebindable) or the podium button on the game's top bar.

**[Get it on the Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3802205096)** · pictures are on the Workshop page.

## What it shows

- **Scoreboard:** each player's damage, share of the team's damage, enemy block knocked off, and the bonus damage their Vulnerable set up for teammates. "The climb" stacks every fight of the run along the map.
- **Awards:** sixteen titles, one winner each (Heavy hitter, Enabler, Clutch, Protector, Battery, Care package, Bodyguard, Coach, Playmaker, Wall, Siege breaker, Fight leader, Jack of all trades, Card factory, Unscathed, Punching bag), plus the game's own end-of-run badges.
- **Sources:** every card, relic, power, potion and orb that dealt damage, per player, and the cards each player created.
- **Debuffs:** who stacked what, the damage Weak and Strength-down kept off the team, and what enemy debuffs cost each player.
- **Support:** the energy, cards, block, buffs and draws each player gave their teammates.
- **Timeline:** damage per fight across the run.
- **Defense:** damage taken, blocked and healed, and how close anyone came to dying.
- **Decks:** everyone's final deck, with the damage each card dealt.

**Export as image** renders the whole run as one tall summary card and puts it in the player's Steam screenshots.

## Good to know

- **Client side.** Only one player needs it. The manifest sets `affects_gameplay: false`, so the game doesn't compare it between co-op players, and the mod only reads game state: it never runs game commands, so it can't desync a run.
- **Modded content.** Nothing is hard-coded per character. Damage is read where the game applies it, so modded characters, cards, powers and summons are credited like vanilla ones. Tested with 10+ custom characters, 5-player lobbies and an extra-act mod.
- **Fair credit.** Shared Poison and Doom are split by each player's part of the pile, tick by tick. Vulnerable, Weak and Strength-down are credited to whoever applied them, and exact ties take turns.
- **Game versions.** One build runs on both the public branch (v0.107) and the beta (v0.111); `Game/GameCompat.cs` looks up the few game APIs that differ by name.
- **Controller** support: bumpers switch tabs, the d-pad moves around.
- **Hotkey:** open the recap and click the key on the top bar — the one reading **F8 toggles the recap** — then press the key you want. Esc cancels; Delete or Backspace clears it, leaving the podium button. The setting is saved in `settings.json` in the mod's data folder.
- **Experimental:** some modded effects deal damage with no dealer and no card (Hextech Runes' Burn), which shows as Unknown. `"experimentalEffectSources": true` in `settings.json` credits it to the effect that was running; restart the game after changing it.
- **Files** live in the game's save folder under `WhoCarried/` (`%APPDATA%\SlayTheSpire2\WhoCarried` on Windows).
- **Saves look gone?** They aren't deleted. The game keeps modded saves apart from vanilla ones, so progress seems to vanish the first time you play with any mod, not just this one. Vanilla saves are untouched and come back without mods. To bring them into your modded game, use [Import Vanilla Saves](https://steamcommunity.com/sharedfiles/filedetails/?id=3747503308).

### Steam Deck and Linux

The game's native Linux build doesn't let mods patch it yet, so run the Windows build through Proton:

1. Game Properties → Compatibility: force Proton 9.0.
2. Game Properties → General → Launch Options: `%command% --rendering-driver vulkan` (without it the screen stays black).

## Building from source

You need the .NET 9 SDK and a Slay the Spire 2 install; the build references the game's own DLLs, and there are no NuGet packages.

1. Copy `local.props.example` to `local.props` and set `GameDir` to your Slay the Spire 2 folder (in Steam: right-click the game → Manage → Browse local files).
2. Build: `dotnet build src/WhoCarried -c Release`
3. Test: `dotnet run --project tests/WhoCarried.Tests` (the tests cover `Core/`, which has no game dependencies).
4. Install into the game (close it first; it locks the DLL): `powershell -File tools/deploy.ps1`. This copies `WhoCarried.dll` and `WhoCarried.json` into `<game>/mods/WhoCarried/`.

To build against another game version's DLLs, pass `-p:GameData=<folder with sts2.dll>`.

## How it's put together

| Folder | What's in it |
|---|---|
| `src/WhoCarried/Core` | The stats, attribution rules, awards and view models. Plain C#, no game types, unit-tested. |
| `src/WhoCarried/Game` | Harmony patches on the game's hooks (damage, powers, block, combat start and end, run end) and the glue that turns game events into Core calls. |
| `src/WhoCarried/UI` | The recap panel, its eight views, the summary image, the top-bar button and controller input, built from the game's own fonts and art at runtime. |
| `tests/WhoCarried.Tests` | A small console test runner for Core. |
| `docs/design` | The specs and plans the mod was built from. |

### Developer checks

Two files in the mod's data folder switch on developer tools at start-up:

- `preview.flag` opens the recap with sample data about 10 s after the game loads, screenshots every view into the data folder, then closes it. The file can hold a party size (`1`–`4`), or a comma-separated list of up to five character ids, like `IRONCLAD,IRONCLAD,REGENT`, to check players who share a character.
- `replay.flag` rebuilds the recap of the last recorded run from `events.log` with the current rules.

## What's changed

[CHANGELOG.md](CHANGELOG.md) — what each release added, changed and fixed.
[docs/releasing.md](docs/releasing.md) — how a build reaches players, and what to check first.

## Credits

Made by huntlol-dev for a co-op group that couldn't stop arguing. Code written with help from AI (Claude Code); the design docs show how.

Simplified Chinese translation by [米拉克 (MerakW)](https://github.com/MerakW).

Not affiliated with or endorsed by Mega Crit. Slay the Spire is a trademark of Mega Crit.

## License

[MIT](LICENSE)
