# Changelog

Newest first. Each release page on GitHub carries its own section from this file.
Changes made on the server take effect for everyone at once; the ones listed here are those players notice.

## Unreleased

### New
- **Social > Challenge:** a new tab on Social's ribbon, right below Friends, to challenge a friend with rules of your choosing. Pick a friend, pick a mode as the starting point (Classic or Skirmish), then change the match rules: warlord health multiplier (x1.0 to x3.0) and health change, starting hand (never more than the hand limit), extra cards for the second player, starting mana, mana per turn, cards drawn per turn, hand limit, overtime turn and turn timer. The menu lists the mode's deck rules for every rarity. Each player uses their deck for that mode. Your friend sees the rules when the challenge arrives, and both games play by them (not yet confirmed).

### Changed
- **Custom Test warlord health:** the multiplier now applies to the warlord's own health first, and any health change is added after it.

## 0.12.34-w - 2026-10-10

### New
- **Testers window** (testers only): the Testers button in the menu bar opens a window laid out like Collection, with its own ribbon. **Play** shows the tester-only modes as Play-screen tiles (for now Custom Test, at full height). **Decks** goes straight to the Custom Test deck list; Back closes the window. **Cards** is the card list; for now Space Wolves are left out on purpose, to check it is separate from the normal Collection. While the window is open, the Testers button is lit in the menu bar, not Collection.

### Changed
- **Custom Test** is no longer on the Play screen or in Collection > Decks. It is for testers only, through Testers > Play and Testers > Decks.
- **Collection > Decks:** Classic and Skirmish are both full-size tiles again (not yet confirmed).
- **Play screen:** Skirmish and Practice share the first column, one above the other, followed by Classic, then Draft, now called **Classic Draft**.
- **Testers button** has its own picture (a servo-tool medallion) and no longer shows Social's helmets first.
- **Classic Draft:** the three warlords offered are always from three different armies (a server change, already live for everyone).

### Fixes
- **"Data update rate exceeded" and "Error connecting to server" popups** (mostly on the draft screen): the mod keeps fewer connections open to the server and tries a request once more when its connection fails or the server is too busy to take it, so these popups should show up far less often (not yet confirmed).

## 0.12.20-w - 2026-10-09

Changes since 0.12.3 (the first release with a changelog).

### New
- **Match replays.** Matches you play are recorded on the server, and Replay in the battle log plays them back, including other players' matches. The replay moves past the mulligan screen by itself.
- **Friend challenges (direct connect)** work again, between PC and phone too. For now a challenge can only be a Classic or Skirmish match.
- **Friends list** shows who is online or in a match, once you have added each other.
- **Profile:** the Warlord Mastery box is back (your warlord with the most wins).
- **Draft:** the server chooses the packs offered each round. The draft screen has its title, help text and picture again.
- **Testers button** in the main menu bar, shown only to in-game testers. Nothing behind it yet.

### Fixes
- **Draft:** tapping a pack twice quickly should no longer add it to your deck twice, and a pack you just picked should no longer be offered again straight away (not yet confirmed in play).
- **Draft:** the Free entry button is now centred under the screen's text.
- **Retries:** the server no longer carries out a request twice when the mod resends it after a dropped connection.
- **Support:** the Website button no longer opens the site twice.
- **Updates:** the "Mod updated" message now stays on screen for its whole countdown, and the "Close now" button that froze the game is removed.
- **Logging:** the log now notes screen events (focus lost or regained, no frames drawn for a while) to help track down black or frozen screens.

### Windows only
- Nothing beyond the changes above.
