# Changelog: Android mod (up to 0.13.26)

Android releases before 0.14.0. From 0.14.0 on, both builds share one release and one changelog: [CHANGELOG.md](CHANGELOG.md).


## 0.13.26-a - 2026-10-14

### New
- **Offence cards per game mode:** each game mode has its own Offence card switch, set by the server owner on the website's Server page. All current modes have it on; a new mode starts with it off. Replays remember whether the match had the pick.
- **Friend challenges (Social > Challenge):** two more rules. **Offence cards** on or off (starts at the chosen mode's setting), and **Goes first**: Random (the game decides as usual), You, or Opponent. The friend sees both in the challenge popup. Both players need this version: a challenge from an older mod can't be accepted, and the other way round. Not yet confirmed in play.
- **Settings > Mod > Export card art (needed for the website):** saves the card pictures and card text from your own copy of the game into one zip, `UserData/WarpforgeRevival/card-art-export.zip` in the game's folder (on PC the folder opens when it is done). It holds every card's art at 512 x 512, the pop-out masks of troops and warlords, the faction frames, and every card's name, rules text and lore in all of the game's languages. The button shows the progress; it takes a minute or two. Load the zip on the website's Profile page: the website no longer serves official card art or text itself, so the Card viewer, Examples and the card creator show official cards (in the language chosen there) only from your export.
- **Bug reports (testers):** press **F8** (PC), **Settings > Mod > Report a bug**, or in a match **Report bug** in the gear menu (next to Resign). The game takes a screenshot, asks for a title, the type of problem, how bad it is and what happened, and sends it with the game's recent log and details of your device and what you were doing. You get a number (B-0042) and the report on the website's new **Bugs** page.
- **Crash and freeze reports (testers):** if the game crashes, freezes, or is closed while frozen, the next start offers to send a report with what was saved at the time. On PC a frozen game also shows a short message after 30 seconds. Reports that can't be sent are kept and sent at the next start. Known issue: the offer sometimes pops up even though the game did not crash (mostly on Android); it can be ignored. It should no longer appear after the game was put in the background (phone screen off, home button, another app) and then closed or ended by the phone, after it was swiped away or closed from the taskbar, or after an update replaced the mod or the game, but this still needs testing.

### Changed
- **Card versions:** the old card pack is no longer downloaded (nothing used its cards). Replays record the server's card bundle version instead, the same on PC and Android, so a replay only plays with the cards it was recorded with; replays recorded before still play.
- **Sign-in window:** the first box is now called **Username**, the same as on the website (it said Name), and the server's messages there say username too.
- **Android: Settings > General > Quit** now closes the game completely (it also leaves the recent apps), the same way the game closes after an update. Before, it could leave the game half-open.
- **Settings > Mod:** the website button is now called **Open** and opens the website's home page (it opened the Card Creator).

### Fixes
- **Social > Challenge:** Classic's starting hand in the challenge menu now starts at the game's own 3 cards (+1 for the second player) instead of 4.
- **Mulligan:** the mulligan closes before the Offence / Defence card step. After Continue, the mulligan cards no longer stay on screen under the pick, nor in front of the opponent's Offence card when it is shown to the player going second; they appear in your hand afterwards.
- **Draft:** after picking a pack, the next packs appear once the server has answered, instead of the packs you just chose from coming back for a moment (and refusing a tap). Should fix it on slower connections, phones especially; not yet confirmed in play.
- **Draft:** when a Draft search ends against the AI, the AI now plays a deck that fits the Draft's rules: a Classic deck in Classic Draft (it was given a Skirmish bot deck), and in a Draft with other deck rules the bot deck nearest its deck size. Confirmed in Classic Draft; other Draft rules not yet confirmed in play.
- **Chat > Report message** now reaches the server admins (the report went only to the game's old analytics service). Not yet confirmed in play.


## 0.13.2-a - 2026-10-11

### New
- **Sign-in window, creating an account:** **Create account** now opens its own page in the window instead of making the account straight away. It asks for:
  - the name;
  - the password, and the password again to confirm it;
  - a tick box: "I am 16 or older and agree to the Terms and Privacy notice", with a **Read** button that opens them.

  **Create account** only works once the box is ticked and both passwords match. **Back** returns to signing in.
- **Sign-in window, Terms and Privacy notice:** when the server's notice changes, the next sign-in with a password shows the same tick box before letting you in.
- **Settings > Mod:** the Website tab is now called Mod. It shows the mod's version and has a **Check for updates** button under the Card Creator link: if the server has a newer mod, it downloads it and restarts the game (on Android, closes it; open it again).

## 0.13.0-a - 2026-10-11

### New
- **Social > Challenge:** challenge a friend with your own rules. Pick a mode (Classic or Skirmish) as the starting point, then set warlord health (x1.0 to x3.0) and health change, starting hand, extra cards for the second player, cards drawn per turn, hand limit, starting mana, mana per turn, overtime turn and turn timer. The menu also lists the mode's deck rules. Your friend sees the rules on the challenge popup, and both games play by them.
- **Replays keep their rules:** each new replay saves the rules it was played under, including a challenge's custom rules, and plays back under them. Changing a mode's rules no longer breaks old replays. A replay from a different card set or mod rules version is refused, with the reason shown. Replays saved before this play as before (not yet confirmed in play).

### Changed
- **Custom Test warlord health:** the multiplier applies to the warlord's own health first, then any health change is added.

### Fixes
- **Testers button:** shows up even when the server could not be reached while the game was loading; the mod asks again a few times.

### Android only
- **Black menu after loading** (Pixel 6 Pro, about 1 start in 5): the phone's video decoder was the cause. The new **Still** loading-video setting skips the video and shows its last frame as a picture for the whole load. Set it with "6 - fix loading video.bat" (option 1), then "7 - loading video setting.bat" (option 1). Other phones keep the video. If a menu ever does come up black, the mod repairs it within a second or two.
- **Opening video blocky, discoloured or stuttering** (Pixel 6 Pro): "6 - fix loading video.bat" also puts a WebM copy of the clip on the phone, which plays smoothly. Looks only.
- **Fewer crashes while loading:** the loader no longer places code at addresses of its own choosing, and its .NET runtime no longer pauses threads with the same signals the game uses. 0 crashes in about 40 starts on the Pixel 6 Pro since (needs the game patched again: "1 - patch.bat", then "2 - install on phone.bat").
- **A crash no longer freezes the game:** if one happens, the game closes within seconds and leaves a proper crash report, instead of hanging on the loading screen.
- **Start-up that failed to load the mod** now says to close the game fully and reopen it, instead of a loading screen that never ends.

### Android patcher
- "6 - graphics trace.bat" removed (only needed for chasing this bug). "7 - fix loading video.bat" is now **6**, and "8 - loading video setting.bat" is now **7**.

## 0.12.34-a - 2026-10-10

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

## 0.12.20-a - 2026-10-09

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

### Android only
- **Updates:** after a mod update the app now closes fully. It no longer gets stuck closing itself on every start until force-stopped (seen on the MuMu emulator).
- **PC emulators:** the mod now runs on emulators that use Intel's ARM translator (tested on MuMu). This needs the game re-patched with this release's patcher (`1 - patch.bat`, then install again). Phones that already work do not need it.
