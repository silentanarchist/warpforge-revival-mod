# Translations

Text that Warpforge Revival adds to the game (and later the website and custom cards), in every language the game
itself ships: English, Spanish, Russian, French, German, Portuguese (Brazil), Italian, Korean, Simplified Chinese,
Japanese, Polish, Czech and Turkish (`languages.json`). More languages can be added on request.

| Folder | What it holds |
|---|---|
| `mod/` | The mod's own texts: buttons, messages, windows. `en.json` is the source. |
| `site/` | The website's texts (the language picker is in the site's header). `en.json` is the source, made from the site page by `tools/localization/extract-site.py`. |
| `patcher/` | The Warpforge Patcher app's texts (the Android app that patches the game on the device). `en.json` is the source, made from the app's code (silentanarchist/MelonLoaderInstaller, `WarpforgePatcher/`) by `tools/localization/extract-patcher.py`. The app builds these files in and also fetches newer copies from the server. |
| `guides/` | The install and Card Creator guides, block by block. `en.json` is made from the server repo's `guides.html` by `tools/localization/extract-guides.py`; each key is the guide and a fingerprint of the block's English, so the guides need no marking and a changed block simply gets a new key (English until translated again). |
| `server/` | Messages the game server sends to players ("No player with that friend code", sign-in and ban notices...). `en.json` is made from the server repo's `playfab_emu.py` by `tools/localization/extract-server.py`; each key is a fingerprint of the English. Parts filled in by the server are `{p1}`, `{p2}`...: keep each one. The server itself answers in the player's language. |
| `index.json` | Made by `tools/localization/build-index.py`: which language files exist, with their checksums. |

## For translators

Each language is one file, `<code>.json` (for example `mod/es.json`), with the same keys as `en.json` and the text in
that language. Translations are done on Weblate; you do not need to edit these files by hand.

- Keep words in braces exactly as they are: in `"Friend request sent to {name}"`, `{name}` is replaced by a player's
  name. Move it wherever it fits your sentence.
- Keep the game's own words for game terms (Warlord, Stratagem, keywords...): use what the game shows in your language.
- A text without a translation is shown in English, so a half-done language is fine.
- First drafts made by machine are marked "needs editing" on Weblate until someone who speaks the language checks them.

Machine drafts not yet checked by a speaker of the language: **all of them** (every file in `mod/`, `site/` and `guides/` except `en.json`). They use the game's own words for its terms (its word for Energy, Warlord, Deck...) taken from the game's translations.

Game terms follow the game's own translations (its words for Troop, Stratagem, Warlord, Energy, Health, Armour,
rarities, races, keywords, game modes and menus). Keywords in running text are written as the game's word with the
English in brackets, e.g. "Refuerzo (Rally)", because the Card Creator lists them in English. Screens the mod has
translated (Friends, Social > Challenge, the Testers window, Settings > Account and Settings > Mod, the sign-in window)
are named by their exact translated label from `mod/<code>.json`, and the Patcher app's buttons (translated from patcher
0.4.0) by their label from `patcher/<code>.json`. Screens that exist only in English (the Card Creator's controls until
they are translated) are named by their exact
English label with a short translation in brackets.

## How it reaches players

The mod and the site download the language files from the revival server
(`/api/v1/content/files/localization/...`), so a new or corrected translation reaches players without an update.
For now the owner says when to copy the approved files to the server (later this will happen by itself).

## For developers

Every sentence the mod shows goes through `Loc.T("area.key", "English text", ("name", value))` (`WarpforgeRevival/Loc.cs`).
The English stays in the code; after adding or changing texts run

    python3 tools/localization/extract.py

to update `mod/en.json`.

The Patcher app (MelonLoaderInstaller fork, `WarpforgePatcher/`) writes its texts as `Loc.T("area.key", "English")`,
`new LocText("area.key", "English")` or `new UserError("area.key", "English")` (`Loc.cs`); its log stays in English.
After changing the app's texts run

    python3 tools/localization/extract-patcher.py [path/to/WarpforgePatcher]

to update `patcher/en.json`; the app's `build.sh` copies `patcher/*.json` into the app.

The website marks its texts as `data-t="key"` on elements (the element's text is the English; `data-t-title` /
`data-t-placeholder` for those attributes) and as `T("key", "English", { name: value })` in its scripts. After
changing the site, run

    python3 tools/localization/extract-site.py [path/to/creator.html]

(default: `../warpforge-revival-server/content/files/creator.html`) to update `site/en.json`.

The server's messages: every `err(code, "message")` in the server repo's `playfab_emu.py` is a text. After changing
them run `python3 tools/localization/extract-server.py`. The mod and the website send the player's language in the
`X-Revival-Lang` header, and the server replies with the translation from `server/<code>.json` (by the message's
fingerprint; values in `{p1}`, `{p2}`... are put back in). Without the header, or for a message not translated yet,
the reply stays English. Before copying to the server, run `tools/localization/build-index.py`.

The Card Creator (keys `creator.*`, `creatorHelp.*`, `home.*`) adds two things the extractor knows about:

- A call whose key is computed, `T("creator.kind." + id, label)`, is allowed when the literal part ends in a
  dot. Its keys are then listed as plain calls in a function next to it (`creatorKindKeys()`,
  `creatorTemplateKeys()`, `creatorTrigGroupKeys()`, `creatorWhoKeys()`, `creatorRarityKeys()`…), so the
  extractor still sees every key with its English.
- Texts the creator shows from its content files are collected from those files, which sit next to the page in
  the server repo: `creator-help.json` gives `creatorHelp.trigger.<id>.name` / `.text` (triggers the game
  never fires are left out) and `creatorHelp.keywordName.<id>` / `creatorHelp.keyword.<id>` for the keywords it names and describes; `keywords.json`
  gives `creatorHelp.keyword.<id>` for the others and `creatorHelp.rulebook.<id>` (the cheat sheet);
  `stratagem-types.json` gives `creatorHelp.stratagemType.<id>`. Changing a text in those files changes the
  English, so run the extractor afterwards.

What stays English on purpose: a card's own name and rules text, the generated "Suggest text" wording (it is
written onto the card), the creator rules (`creator-rules.txt`), the game's own identifiers (trigger ids,
`activeAbility`…) and faction and stratagem type names. Keyword names are shown as the game's word with the
English in brackets, taken live from the player's own export (`Card_Trait/<id>`), so no game text is shipped
here. The `creatorHelp.*` descriptions are a convenience: the English is the binding wording.

Before committing or copying translations, check them:

    python3 tools/localization/check.py site        # or mod, guides, patcher; add language codes to check only those

It lists missing or left-over keys, changed `{placeholders}`, HTML tags that differ from the English or are not
allowed, changed text inside `<code>`, and empty texts.
