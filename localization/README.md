# Translations

Text that Warpforge Revival adds to the game (and later the website and custom cards), in every language the game
itself ships: English, Spanish, Russian, French, German, Portuguese (Brazil), Italian, Korean, Simplified Chinese,
Japanese, Polish, Czech and Turkish (`languages.json`). More languages can be added on request.

| Folder | What it holds |
|---|---|
| `mod/` | The mod's own texts: buttons, messages, windows. `en.json` is the source. |
| `index.json` | Made by `tools/localization/build-index.py`: which language files exist, with their checksums. |

## For translators

Each language is one file, `<code>.json` (for example `mod/es.json`), with the same keys as `en.json` and the text in
that language. Translations are done on Weblate; you do not need to edit these files by hand.

- Keep words in braces exactly as they are: in `"Friend request sent to {name}"`, `{name}` is replaced by a player's
  name. Move it wherever it fits your sentence.
- Keep the game's own words for game terms (Warlord, Stratagem, keywords...): use what the game shows in your language.
- A text without a translation is shown in English, so a half-done language is fine.
- First drafts made by machine are marked "needs editing" on Weblate until someone who speaks the language checks them.

## How it reaches players

The mod and the site download the language files from the revival server
(`/api/v1/content/files/localization/...`), so a new or corrected translation reaches players without an update.
For now the owner says when to copy the approved files to the server (later this will happen by itself).

## For developers

Every sentence the mod shows goes through `Loc.T("area.key", "English text", ("name", value))` (`WarpforgeRevival/Loc.cs`).
The English stays in the code; after adding or changing texts run

    python3 tools/localization/extract.py

to update `mod/en.json`. Before copying to the server, run `tools/localization/build-index.py`.
