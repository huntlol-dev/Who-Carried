# Keeping every run

Date: 2026-10-03. Draft for review. Branch: `feat/multiple-runs`. Plan: to follow.

## Intent

The mod keeps one run at a time: `current_run.dat` (the counts) and `events.log`, in `%APPDATA%\SlayTheSpire2\WhoCarried\`. When a run starts that isn't the saved one, the old files are copied to `current_run.previous.dat` and `events.previous.log` and both start from zero. Players have asked for this to change. The case they hit: start a co-op run, leave it, start a singleplayer run, go back to co-op. The co-op counts went to `.previous`; on going back, the mod only looks at `current_run.dat`, finds the singleplayer run, and starts co-op from zero. A third run overwrites `.previous` too.

The game itself allows this. It keeps one singleplayer save and one co-op save per profile (`current_run.save`, `current_run_mp.save`), across three profiles, so several unfinished runs can exist at once.

**This change is about not losing data.** Every run gets its own folder that nothing else writes into. A run that's picked up again carries on in its folder, however many runs were played in between. It's done when, in the example above, finishing the co-op run shows a recap that includes its first half.

**Run history (browsing old runs in the game) is out of scope.** But every run written from this release on is what a history tab will later read, so each folder also records what the run was played with: mods and branch can change before anyone looks at an old run (see [run.json](#runjson)).

Nothing on screen changes, so there are no new strings and nothing for `zhs.json`.

## Decisions

The owner made these during brainstorming:

- **Keep every run, finished or not, up to 100.** Past that, the run played least recently is deleted, finished or not. An unfinished run is only lost after 100 newer runs, so a run that's really paused never is in practice, and runs that will never finish (a guest whose host never comes back) don't pile up. About 250 KB a run, so about 25 MB at the cap.
- **A folder per run**, under `runs\`. Once written, a run's files never move, so switching runs can't lose anything.
- **Lines written outside a run go to `mod.log`**, started fresh each launch.
- **Today's files are moved into `runs\` on the first start of the new version**, the `.previous` pair included, so a run in progress when Steam updates the mod carries on.
- **`replay.flag` rebuilds the newest run, or the run named in the flag.**
- **Each run records the mods, game build, language and players it was played with (`run.json`), but no art.** See [Why no art](#why-no-art).

## On disk

```
%APPDATA%\SlayTheSpire2\WhoCarried\
  settings.json                  unchanged
  mod.log                        lines written outside a run, this launch
  mod.previous.log               the same, last launch
  runs\
    2026-09-27_2140_TB20BJ6WUGJ2\
      stats.dat                  the counts (what current_run.dat was)
      events.log                 the run's log
      run.json                   what the run was played with
    2026-09-29_1918_CEKDYSK8A9AB\
      ...
```

The developer files (`preview.flag`, `replay.flag`, their screenshots, `images\`) stay where they are.

### Folder names

`yyyy-MM-dd_HHmm_SEED`: the local time the run started, then the seed. If the name is taken (the same seed started twice in a minute), `-2`, `-3` and so on are added.

The seed is in the name and the start time isn't, on purpose. A co-op guest's run key changes when the host hosts the run again (see [Picking a run up again](#picking-a-run-up-again)), so the start time can't identify the folder. The seed doesn't change, and having it in the name means finding a run opens only that seed's folders.

The time comes from the run key's start time (Unix seconds) when there is one, otherwise from the clock. Seeds are letters and digits; anything a file name can't hold becomes `_`.

### When a run was last played

The newest last-write time of any file in its folder. Pruning and `replay.flag` both use it, and it never needs a file to be read.

## Picking a run up again

`Tracker.OnRunStarted` asks `RunFolders.FindResumable(runKey, loadedFromSave)`:

1. Only folders named for this run's seed are opened (usually none or one).
2. Their `stats.dat` is read. Finished runs and files that can't be read are skipped.
3. **A folder whose stats have this exact run key wins.**
4. Otherwise, **if the game is loading a saved run, a folder with the same seed is enough.** This is today's rule (`RunStatsStore.LoadIfResumable`) from the [guest re-host investigation](../../investigations/2026-09-22-guest-stats-reset-on-rehost.md): a guest's new run is stamped with the guest's clock, and the save it loads later carries the host's.
5. If several match, the one played most recently wins.

On a match, counting carries on in that folder, the stats take on the new run key (as today), and the log gets the "resumed" line. Otherwise a new folder is made.

A new run with a seed played before (a custom seed) isn't loaded from a save, so it only matches on the exact key. Its start time differs, so it gets its own folder.

## run.json

Written when a run gets its folder, and **rewritten each time the run is picked up again**, so it describes the latest session. Before this version there was no `run.json`; a migrated run gets one the first time it's resumed, and a migrated run that's never resumed has none.

| Field | What | From |
|---|---|---|
| `format` | `1`. Raised whenever the folder's files change shape, so a later reader knows what it's reading. | constant |
| `runKey` | The run key, as in `stats.dat` | `GameReader.RunKey` |
| `whoCarried` | The mod's version | `ModEntry.Version` |
| `gameBuild` | The game's build, e.g. `v0.111.0`. Tells the public branch from the beta. | the game's release info |
| `language` | The game's language when this was written, e.g. `eng`, `zhs`. The text in `stats.dat` is in this language. | `Loc` |
| `localPlayer` | The player's own id | the run |
| `profile` | The game profile number (1–3) | the game's save path |
| `mods` | Every loaded mod: id and version | `ModManager.GetLoadedMods()` |
| `players` | Per player: id, name, character id, character name, colour (hex) | `GameReader.Players` |
| `written` | When this file was written (local time, ISO 8601) | clock |

Every field is read on its own and wrapped: if the game won't give it (a branch without the call, a mod without a version), that field is `"unknown"`. If the file can't be written, the run carries on without it. Neither ever affects counting.

Each start and resume also writes a `mods:` line to `events.log` with the same list, so a mod added, removed or updated mid-run shows in the log. It's useful for bug reports too.

Decks, relics, badges, the map path and what killed the party aren't copied: the game's own history file (`saves\history\<start time>.run`) already has them, for guests too, and a later history tab can find it by the start time in the run key. Replay already does.

### Why no art

The recap draws portraits, card art, buff icons and badges from the running game, and only while that run is loaded, so the mods it used are always there. A history tab would draw runs whose mods might be off, updated, or from another branch. It can do what the game's own run history screen does: an id it can't find becomes the game's placeholder (`SaveUtil.CardOrDeprecated`, `CharacterOrDeprecated`, `DeprecatedRelic`), and our stored text supplies the name. `run.json` lets it say which mods a run needs.

Saving the art was considered and turned down:
- It can only be captured when the mod sees a run end, so runs abandoned from outside, unfinished runs and every run already on disk would have none. History needs the text fallback anyway.
- A 4-player deck is about 120 cards at 100–200 KB each: 15–25 MB a run, up to 2.5 GB at the cap, unless art is shared between runs, which needs a cache of its own.
- Rendering it all would land on the main thread just as the game-over screen animates in.
- Frozen images have the language baked in, look soft at other resolutions, and show today's look instead of whatever the recap looks like then.

## The log outside a run

`mod.log` takes every line written before the first run starts in a launch: the start-up notes (`effect sources: watching …`), errors in the main menu, and the preview's and replay's lines. Each launch starts it fresh and keeps the last one as `mod.previous.log`, the way `EventLog.Reset` keeps `.previous` today.

Once a run starts, lines go to that run's `events.log`, including lines written after it ends (the recap's Save image and Copy notes), until another run starts. Run logs are only ever added to.

## Moving today's files

`RunFolders.MoveOldFiles(dataDir)` runs in `Tracker.Init`, before `mod.log` is opened:

1. If `current_run.dat` isn't in the root, it does nothing. On every launch after the first, that's all it costs: one file check.
2. Otherwise it moves two pairs, each into the folder of its run:
   - `current_run.dat` with `events.log`
   - `current_run.previous.dat` with `events.previous.log`
3. Each file's run comes from inside it: `RunKey` in the stats, the header line in the log (`LogReplay.HeaderLine`), or a later "resumed" line if there is one. A stats file and a log go in the same folder when their seeds match (a guest's log header and stats can have different start times). A file without its partner gets a folder on its own; a file whose run can't be read stays where it is.
4. The folder's name comes from the stats' run key (the log's, for a log on its own): its start time and seed.
5. **Nothing is ever overwritten.** If the folder already exists, the files stay in the root and a line goes to `mod.log`. That only happens if someone goes back to an older Who Carried mid-run, which in practice means local builds.
6. Moves are renames on the same drive, so a file is never in two places or in none. If one fails, the others are still tried, and whatever is left is tried again next launch.
7. It never throws, like `DataFolderMove`.

Other leftovers (`events.prev.log` and the like) are left alone. The code can come out a few releases on, once nearly everyone has been through it.

## Pruning

After a run gets its folder, `RunFolders.Prune(keep: 100, active)` deletes the folders played least recently until 100 are left. It never deletes the run being played. A folder that can't be deleted is skipped and logged.

## replay.flag

- **Empty:** rebuilds the run played most recently, as today.
- **Holds a folder name** (e.g. `2026-09-27_2140_TB20BJ6WUGJ2`): rebuilds that run. An old run or a player's bug-report folder can be replayed without moving files.
- **Names a folder that isn't there:** `mod.log` says so and nothing is replayed. It doesn't fall back to the newest run, because that would quietly show the wrong one.

The player's own block comes from that folder's `stats.dat`. Today it's only used when its run key equals the log's; in a folder the two are the same run by definition, so the guest case (different start times) gets its block back too.

## Code

**New**
- `Core/RunFolders.cs`: the `runs\` folder. `Create`, `FindResumable`, `Prune`, `Newest`, `Named`, `MoveOldFiles`. Plain C# with no game types, so it can be tested against a temporary folder.
- `Core/RunInfo.cs`: the `run.json` record, and saving and loading it (with `WhoCarriedJson`, like `RunStats`).

**Changed**
- `Game/Tracker.cs`
  - `Init` moves old files, then opens `mod.log` (reset with `mod.previous.log`).
  - `OnRunStarted` finds or creates the run's folder, points the stats path and the log at it, writes `run.json` and the `mods:` line, then prunes.
  - The `.previous` copying goes.
- `Core/EventLog.cs`: unchanged in behaviour. `Reset` with `.previous` now serves `mod.log`; run logs are opened without a reset.
- `Core/RunStatsStore.cs`: `Save` and `Load` stay. The resume rule moves to `RunFolders`, which reuses the seed comparison.
- `Game/GameReader.cs`: reads the game build, profile number and loaded mods, each wrapped.
- `UI/Replay.cs`: picks the run from `replay.flag`, and reads that folder's `events.log` and `stats.dat`.
- `README.md`: the data folder paragraph and the `replay.flag` line.
- `docs/README.md`: the "two later changes" note gets a third.
- `CHANGELOG.md`: an entry. The Workshop note stays short and friendly.

## What can go wrong

| Case | What happens |
|---|---|
| A run abandoned without the mod seeing it end (a save deleted outside the game, a guest whose host never comes back) | It stays unfinished. It's only opened again if a run with its seed loads, and the cap clears it out eventually. |
| A matching folder's `stats.dat` can't be read | Skipped. The run starts fresh in a new folder, as a corrupt `current_run.dat` does today. The bad folder is left for the cap. |
| The run's folder can't be made (permissions, full disk) | Counting carries on in memory and the recap still works. The error goes to `mod.log` and Godot's log. |
| `run.json` can't be written, or a field can't be read | The field is `"unknown"`, or the file is missing. Counting is never affected. |
| A mod updated, added or removed mid-run | If the game still loads the save, counting carries on in the same folder; `run.json` gets the new list and the log shows the change. If the game refuses the save, there's nothing to resume. |
| Switching branch (public ↔ beta) mid-run | The same: the game decides whether the save loads, and we follow. `run.json`'s build says which branch the last session was on. |
| Two Steam accounts or several game profiles on one PC | Their runs share `runs\`. Matching is by seed and run key, so they don't mix up; `run.json` records the profile and player so a history tab can filter. |
| The migration meets a folder that already exists | Nothing is overwritten; the root files stay and `mod.log` says so. |

## Testing

**Unit tests**, in the existing runner, with temporary folders as in `DataFolderMoveTests`:

- `RunFoldersTests`
  - `Create` names the folder `yyyy-MM-dd_HHmm_SEED` and adds `-2` when the name is taken.
  - `FindResumable` matches the exact key; matches on seed only when loading from a save; skips finished runs, other seeds and unreadable stats; takes the most recently played of several.
  - `Prune` keeps 100, deletes the least recently played first, never the active run, and does nothing below 100.
  - `Newest` and `Named` pick the right folder; an unknown name gives nothing.
  - `MoveOldFiles` moves both pairs into folders named from their own keys, pairs a guest's log and stats with different start times, never overwrites an existing folder, leaves a file it can't read, retries what's left, and does nothing when `current_run.dat` isn't there.
- **The players' case as one test:** a co-op run starts and saves, a singleplayer run starts and saves, the co-op run is loaded again and finishes. Every co-op count is there, and the singleplayer folder is untouched.
- `RunInfoTests`: `run.json` round-trips; missing fields read as `"unknown"`; a file that can't be read doesn't throw.
- `EventLogTests`: `mod.log` starts fresh each launch and keeps `mod.previous.log`.

**In the game**, after checking the owner isn't playing:

1. **Back up first.** The first launch of the new build moves the owner's real `current_run.dat` and `events.log`. Before deploying, copy the whole `WhoCarried` folder to a dated backup, and check afterwards that the runs of the 27th and 29th are in `runs\` intact.
2. Start a run; check its folder, `run.json` and the `mods:` line.
3. `replay.flag` empty, then naming a folder, then naming one that doesn't exist.
4. `mod.log` has the start-up lines.

**Optional, needs a second player:** the co-op guest case. Join a co-op run as a guest, let the host leave and host it again, rejoin. Afterwards there should be **one** folder for that run in `runs\`, not two. The rule is today's and the unit tests cover it.

## Out of scope

- Run history: any screen for browsing old runs.
- Saving art.
- Copying the game's history file into the run folder.
- Taking the migration code out again (a later release).
