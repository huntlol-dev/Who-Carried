# Localization

Who Carried uses the game's selected language. The default catalogs are
`src/WhoCarried/Localization/eng.json` and `zhs.json`, with the same keys.
No language preference or language switcher is added to the mod.

**Simplified Chinese is kept complete.** A change that adds or rewords on-screen
English updates `zhs.json` in the same change, using the game's own Chinese terms
where they exist (格挡, 遗物, 充能球, 召唤物, 幕, 层). The tests fail while the two
catalogs' keys differ.

## Runtime and packaging

`Game/ModLocalization.cs` supplies the game adapter for `Localization/Loc.cs`.
On the first lookup it merges the embedded English catalog, then the selected
language catalog, into `LocManager.Instance.GetTable("main_menu_ui")` using the
public `LocTable.MergeWith` API. All entries use the `WHO_CARRIED.` namespace;
no base-game keys are overwritten. The registered table reference is checked
on every lookup, so replacing the game's tables also re-registers mod strings.

The loader normally merges mod localization from PCK paths into existing game
tables. This mod ships as a DLL only. Embedding the catalogs and merging them
through the public API preserves that distribution model without a Godot
project, a PCK build step, Harmony localization patches or additional libraries.
The csproj embeds every `Localization/*.json` catalog with a predictable resource
name. The existing deploy script copies the DLL and therefore all catalogs.
Do not copy these JSON files beside the DLL: the game scans loose JSON files as
mod manifests.

`Loc.Text(key, args)` retrieves the raw game-table template and applies .NET
composite formatting (`{0}`, `{1:0}`, etc.) with the game's culture. This is a
small formatting boundary, not a separate language manager. It tries the current
translation, embedded English, then a visible `[WHO_CARRIED.some.key]` marker.
Empty values, malformed composite formats, and temporarily unavailable game
tables also fall back. The pure Core test executable embeds the same catalogs
and uses English by default, without loading Godot.

Complete sentences and number labels are templates. Translators may reorder
arguments; do not split sentences into translated fragments. Singular/plural
English counts use separate keys. Award titles are stable localization keys;
award scoring, tie-breaking, source IDs and persisted log syntax do not change.
`RunFacts.Act` keeps the top-bar act independent of translated header punctuation.

## Native content and fonts

Existing card, relic, potion, orb, monster, character and badge lookups still use
the game's `LocString` or model title. Card types use
`gameplay_ui/CARD_TYPE.*`, Block uses `static_hover_tips/BLOCK.title`, and the
Relic legend uses `gameplay_ui/RELIC_RARITY.NONE`. Strength-loss wording wraps
the game's `powers/STRENGTH_POWER.title` in a mod template. The missing-model
Doom path also tries its native power title first.

`RecapTheme` asks `FontManager.GetSubstituteFont(language, Regular/Bold)` first,
then uses the original Kreon resources. The game maps Simplified Chinese to
Noto Sans Mono CJK SC (regular) and Source Han Serif SC (bold). The same theme
is used by all tabs and `SummaryCard`; no font files are bundled. Font selection
is invalidated when the language changes. Native names stored in an old run/log
remain the names captured when that run was played; this change does not migrate
historical data or invent translations for missing third-party models.

## Adding a language

1. Add `<game-language-code>.json` beside `eng.json`, e.g. `zht.json`, `jpn.json`
   or `kor.json`. Use an existing game language code.
2. Copy keys from English, translate complete templates and retain their argument
   indexes and numeric formats. A catalog other than Simplified Chinese may be
   partial: missing entries fall back to English.
3. Run the tests and build below.
4. Test via Steam and compare the in-game UI with the exported image.

## Verification commands

```sh
dotnet run --project tests/WhoCarried.Tests -c Release
dotnet build src/WhoCarried/WhoCarried.csproj -c Release -p:GameData="<directory containing sts2.dll>"
```

The console test runner (not `dotnet test`) runs the Core suite and the
localization tests. They check that every key the code uses is in `eng.json` and
none is left unused, that no catalog lists a key twice, eng/zhs parity and
placeholder parity, actual format parsing, missing/empty/broken translations,
unavailable lookup, visible last-resort fallback and Chinese argument reordering.

API inspection and compilation were performed against the locally installed
STS2 v0.111.0 (`41cef1ea`, arm64). It also builds against v0.107.1; the
manifest's existing minimum version is unchanged.

## Steam preview checklist

Close the game, install the built `WhoCarried.dll` and `WhoCarried.json` into its
WhoCarried mods folder, then start the game **from Steam**. On macOS this mod
folder is `SlayTheSpire2.app/Contents/MacOS/mods/WhoCarried/`.

Put `preview.flag` into the mod's user data folder:

- macOS: `~/Library/Application Support/SlayTheSpire2/WhoCarried/`
- Windows: `%APPDATA%/SlayTheSpire2/WhoCarried/`
- Linux: `~/.local/share/SlayTheSpire2/WhoCarried/`

Flag contents are `<party-size> <language>`, for example `4 zhs`, `1 zhs`,
`4 eng` or `4 jpn` (English mod fallback with native Japanese content/fonts).
The language override only calls `LocManager.SetLanguage`; it does not write the
game's saved language preference. Existing `m1`, `m2` and `steam` modes still work;
`steam zhs` exercises the real export status/Steam screenshot path.

After startup the preview captures seven tabs, the live update, the long image,
top-bar tooltip and controller navigation. Wait for `preview done` in
`mod.log`. Copy the screenshots somewhere else between runs: filenames are
reused. Delete `preview.flag` when finished.

| View | Check in English and Simplified Chinese |
| --- | --- |
| Scoreboard | Award plaques, damage, removed Block, bonus/biggest-hit label, solo story, no overlap |
| Awards | All earned title keys resolved, detail argument order, HP/count values, native badges |
| Sources | Type legend, created cards, Other count, uncredited source, native card/relic/power names |
| Debuffs | Official power names, stacks, bonus/prevented sentences, enemy-debuff cost labels |
| Timeline | Act headings, floor/encounter hover readout, team totals |
| Defense | Shield explanation, animated amounts, team strip, compact exported plates |
| Decks | Native card faces/type names, card counts, distinct count, damage labels, missing-deck state |
| Shared controls | Top-bar tooltip, close/export buttons, key rebinding prompts, controller glyph spacing |
| Summary image | Every section/story/footer in selected language; no missing glyphs or clipped descriptions |

Also start a fresh run and open the recap before dealing damage to check empty
states. Test a completed victory and defeat, an ongoing run at a new act before
the first fight, one and four players, and your normal display scale. Check real
Steam export success/failure status separately from the preview renderer.
Missing/malformed key behavior is covered by automatic tests; testing partial
catalogs in-game requires rebuilding a temporary catalog variant.

Automated startup outside Steam failed before mod initialization, so runtime
validation was handed to the user. The user subsequently confirmed complete
acceptance of the English and Simplified Chinese implementation. Their supplied
`4 jpn` summary image shows English mod fallback alongside native Japanese
content; this is expected without a Japanese catalog and was accepted by the user.
This is user-reported acceptance, not an automated visual test result. Individual
error paths, controller scenarios and the minimum supported game version were
not separately enumerated in that feedback.

## Final merge review

After the user's English/Chinese acceptance, code review found one remaining
number-plus-suffix construction in enemy debuff costs. These three entries now
own the numeric placeholder and use the existing animated-number formatter.
English and Chinese word order is preserved. A regression test covers both
catalogs and the ability to move the amount.

The official font lookup is now guarded like the original resource lookup. If
its cached font has been disposed, the official cache is cleared and the font
is requested again; loading errors fall back instead of escaping into the view.
These final changes have build/unit-test coverage, but have not been separately
checked in the game. A focused Debuffs cost-row check and reopening after scene
transitions suffice for the affected paths; previous broad acceptance is retained.

## Residual English audit

All runtime UI and summary-image wording identified in the audit is localized.
The retained English has these purposes:

- **B — diagnostics:** `Tracker`/`GameCompat`/`ModEntry` log messages, preview/replay
  diagnostics, internal PNG/Steam error reasons, settings and exception messages.
  UI export failures show the localized generic error, not these internal reasons.
- **C — identifiers/protocol:** source/card-type/rarity enum names (`Card`, `Power`,
  `Other`, `Attack`, `Common`), `Unattributed` and `Other` grouping sentinels (resolved
  at the UI boundary), source/award keys, filenames, Godot node/theme/shader/tween
  names, reflection member names, and `LogReplay`'s English log grammar. These
  cannot be translated without changing identity, parsing or game APIs.
- **D — native/proper names:** `Who Carried?`, Steam, Slay the Spire 2, keycap names
  such as F8/Esc, preview player names (Ash/Mika/Sam/Jo), native model identifiers
  or readable IDs used when third-party content is missing, and English emergency
  fallbacks `Block`, `Relic`, `Strength`, `Doom` after native lookup fails.
- The original distribution manifest name/author/description remain upstream
  metadata. The game's manifest uses literal strings, not `LocString` keys. This
  does not affect the recap or image language; the mod-manager description remains
  English. Documentation, test fixtures and build/deploy messages are developer
  material and are not translated.

The only changes in attribution/event code are source display-label expressions
(pet-via-trigger, unknown source, Strength loss and missing-model Doom). No damage
math, attribution decision, Harmony patch, event-capture order or log grammar is
changed.
