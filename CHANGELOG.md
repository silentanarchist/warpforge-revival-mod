# Changelog

Newest first. Each release page on GitHub carries its own section from this file.
Changes made on the server take effect for everyone at once; the ones listed here are those players notice.

## Unreleased

### New
- **Social > Challenge:** a new tab on Social's ribbon, right below Friends, to challenge a friend with rules of your choosing. Pick a friend, pick a mode as the starting point (Classic or Skirmish), then change the match rules: warlord health multiplier (x1.0 to x3.0) and health change, starting hand (never more than the hand limit), extra cards for the second player, cards drawn per turn, hand limit, starting mana, mana per turn, overtime turn and turn timer. The menu lists the mode's deck rules for every rarity. Each player uses their deck for that mode. Your friend sees the rules on the challenge popup they accept with, and both games play by them.

- **Replays keep their rules:** each new replay saves the rules its match was played under (the mode's values, starting hands, warlord health, turn timer, and a challenge's custom rules) and plays back under them, so changing a mode's rules no longer breaks replays, and custom-challenge matches replay as they were played (not yet confirmed). A replay recorded with a different card set or mod rules version is refused, and the "Error loading match replay" popup says why. Replays saved before this play as before.

### Changed
- **Custom Test warlord health:** the multiplier now applies to the warlord's own health first, and any health change is added after it.

### Fixes
- **Testers button sometimes missing** (seen on a phone): when the server could not be reached while the game was loading, the mod never learned the player is a tester. It now asks again a few times (not yet confirmed).
- **Phones: smoother loading screen**: the mod no longer searches the whole screen for the Testers window four times a second while there is none (not yet confirmed).
- **Phones: fewer crashes and freezes while loading** (seen on a Pixel 6 Pro): the mod now sets itself up with the game's memory clean-up paused for those few seconds (not yet confirmed).
- **Phones: black screen once the menu loads** (seen on a Pixel 6 Pro, about 1 start in 5; music plays, picture stays black until the phone is locked and unlocked): the game reports drawing normally, the phone logs nothing, and only rebuilding the drawing surface brings the picture back, which the mod now does within a second or two of a black menu (0.12.58: the steps that never helped were dropped). Stopping the video before the scene change, a WebM copy of the clip and refreshing Unity's drawing state did not prevent it. Whether the loading screen's video is involved at all is being tested with the new **LoadingVideo** setting (Normal / Texture / Off); it is **Normal** (the game's own video) so that the loader change below is tested on its own first (not yet confirmed).
- **Alliances tab:** its ToFocus method is no longer patched (it only jumps into Refresh, which is), so no game function needs the 4-byte kind of hook any more.
- **Phones: the loader no longer places hook relays at addresses it picks itself** (needs the game patched again with "1 - patch.bat" and "2 - install on phone.bat"): one hook per start was given the 4-byte kind by mistake, and the hooking library maps that kind's relay at a spot it believes is free, replacing whatever was really there. Depending on where that landed, that could explain both the rare crash while loading (the runtime's memory) and the black menu (the graphics driver's memory) on the Pixel 6 Pro. The loader also now logs the exact address of any native crash and hands it to Android's crash report (not yet confirmed).
- **Phones: blocky, discoloured or stuttering opening video** (seen on a Pixel 6 Pro: its H.264 decoder plays the clip badly): the optional **"7 - fix loading video.bat"** in the Android patcher copies the clip out of your own game file, converts it to WebM (needs ffmpeg) and puts it on the phone, where the mod plays that copy instead. Looks only; option 2 of the script removes it.
- **Phones: start-up that could not load the mod properly** (seen on a Pixel 6 Pro, about one start in several): instead of a black loading screen that never ends, the game now says to close it fully and open it again.

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
