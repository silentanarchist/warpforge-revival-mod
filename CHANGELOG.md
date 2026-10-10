# Changelog

Newest first. Each release page on GitHub carries its own section from this file.
Changes made on the server take effect for everyone at once; the ones listed here are those players notice.

## Unreleased

### New
- **Testers page:** the Testers button opens a page with a **Play** tab. It lists the Custom Test mode; picking it opens the mode's page as before.

### Changed
- **Custom Test** is no longer on the Play screen. It is for testers only and is reached from Testers > Play.
- **Testers button** has its own picture (a servo-tool medallion) instead of Social's helmets.
- **Play screen:** Skirmish and Practice share the first column, one above the other, followed by Classic and then Draft, now called **Classic Draft**.
- **PC fullscreen:** the game no longer minimizes itself when you alt-tab away; it stays on screen behind the window you switched to (should work; not yet confirmed).

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
