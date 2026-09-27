# Releasing

How a build gets to players, and the traps that have actually bitten.

The Workshop workspace lives **outside this repo**, in a sibling folder beside it
(`<parent>\Who-Carried-workshop\`). It holds the uploader, the staged `content\`,
`workshop.json`, `description.txt` and the preview images. Nothing in it is tracked here.

## Next release: three features waiting

Specced and planned on 2026-09-27, held back from the release that shipped the HP cut, the
vanilla attribution fixes and infinite-HP phases. Each branch holds only its spec and plan, which
are already on `main`, so git lists them as merged: **don't delete them**. Start each one by
fast-forwarding it to `main`, implement it, then merge it the same way.

| Branch | Spec | Plan |
|---|---|---|
| `feature/protection-given` | [damage prevented for teammates](design/specs/2026-09-27-protection-given-design.md) | [plan](design/plans/2026-09-27-protection-given.md) |
| `feature/healing-given` | [healing given to teammates](design/specs/2026-09-27-healing-given-design.md) | [plan](design/plans/2026-09-27-healing-given.md) |
| `feature/strength-given` | [damage from Strength you gave](design/specs/2026-09-27-strength-given-design.md) | [plan](design/plans/2026-09-27-strength-given.md) |

Protection and healing both add Support kinds and awards, and strength adds to the damage maths,
so do them one after another rather than side by side. Delete this section once they've shipped.

## Local install

```
powershell -File tools/deploy.ps1
```

Builds Release and copies `WhoCarried.dll`, `.pdb` and `.json` into `<game>\mods\WhoCarried\`.
It refuses to run while the game is open, because the game locks the DLL.

**Launch the game through Steam**, not by running `SlayTheSpire2.exe`:

```
steam.exe -applaunch 2868840
```

Running the executable directly fails with *"Steam failed to initialize… No appID found"*.

To see the recap without playing a run, put `preview.flag` in the mod's data folder
(`%APPDATA%\SlayTheSpire2\WhoCarried`). About ten seconds after the game loads it opens the
recap with sample data, screenshots every view, exercises controller mode, and closes.
Put `steam` in the flag instead and it also presses Export and screenshots the result.
**Delete the flag afterwards** or it hijacks the next session.

## Workshop update

1. `git` is clean and merged; bump `"version"` in `src/WhoCarried/WhoCarried.json` and move
   the changelog's `[Unreleased]` heading to that version and today's date.
2. Copy the Release `WhoCarried.dll` and `WhoCarried.json` into the workspace's `content\`.
3. Put the change note in `workshop.json`'s `changeNote` — see the BBCode note below.
4. From the uploader folder: `ModUploader.exe upload -w ..\WhoCarried`
   (Steam must be running and signed in; the item ID comes from `mod_id.txt`.)

### Traps

**`visibility` is applied on every upload.** The uploader calls `SetItemVisibility` whenever
the field is present, so a stale `"private"` left over from setup will *delist a live mod*
on the next update. It must read `"public"`. Omitting the field entirely leaves whatever
Steam currently has, which is the safe option if you are unsure.

**The workspace is not the only source of previews.** Gallery updates have been done from
separate `gallery-update-<date>\` folders that blank `title`, `description`, `visibility` and
`tags` so the uploader touches only images. That means the main workspace's `previews\` can be
older than what is live, and uploading from it **silently reverts the gallery**. Before any
upload, check whether a `gallery-update-*` folder holds newer images and copy them into the
workspace first — same filenames, because the names map to gallery slots.

**`description.txt` is the source of truth for the description**, not the copy embedded in
`workshop.json`. The embedded one goes stale and uploading pushes it over the newer text.
Copy `description.txt` into the field before uploading.

**Every upload pushes the whole item** — content, description, title, tags, visibility and all
previews. There is no partial update. Whatever is in the workspace becomes what is live, so
anything stale in there is a regression waiting to happen.

### Editing `workshop.json` from PowerShell

Windows PowerShell 5.1's `ConvertTo-Json` writes a string that came from `Get-Content -Raw`
as `{"value": "…", "Length": n}` instead of a plain string, because the value is a wrapped
`PSObject`. That corrupts the field silently. Force it:

```powershell
$j.description = [string]("" + (Get-Content $descPath -Raw))
```

Then re-read the file and confirm each field's type is `String` before uploading.

### Change notes

Steam wraps a bare first line followed by a blank line in a grey panel and pulls the next
heading into it. Open with a real heading instead:

```
[h3]v1.1.0[/h3]

[b]Added[/b]
[list]
[*]…
[/list]
```

## After a release

Keep `CHANGELOG.md` and the manifest version in step — the changelog's newest heading and
`WhoCarried.json`'s `"version"` should always match what is live.

The store description mentions the hotkey and the export button by name. When either changes,
`description.txt` needs the same edit, or the page goes stale.
