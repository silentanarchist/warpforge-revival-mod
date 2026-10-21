# Changelog

Newest first. Each release page on GitHub carries its own section from this file. Since 0.14.0 one release holds both
builds (Windows `-w`, Android `-a`); a line that applies to one of them only says so. Earlier Windows releases are
listed below; earlier Android ones are in [CHANGELOG-android.md](CHANGELOG-android.md).
Changes made on the server take effect for everyone at once; the ones listed here are those players notice.


## Unreleased

### New
- **Server deal (test), for testers:** a friend challenge can ask for the server-deal test (a rule on Social > Challenge that only testers see, or the "Server deal (test)" tile in the Testers window, which opens the Challenge page with it on). In such a match the server supplies the match's random numbers (the shuffle and every random effect) instead of one player's game, and hovering or dragging a card in your hand no longer tells the opponent's game which card it was. First step of hidden hands and decks; every other match plays exactly as before.
- **Translations (groundwork):** the mod's own texts can now be shown in the game's language (the language set in the game's Settings). Translations come from the server, so they can be added and corrected without a mod update; texts not yet translated stay in English. First texts: Friends / Requests, the alliance invite box, the Social > Challenge page (its mode names too), the Challenge tab, the Testers button and window tabs, the mod's sign-in window (and its Updating / Signed out screens), Settings > Account and Settings > Mod (with their messages, the update messages and the card art export's progress). Words the game already has (Friends, Offline, On / Off, Random, OK, the card rarities) use the game's own translations, so they match the rest of the game. First translations, made by machine and waiting for players who speak each language to check them, are up for all 12 languages the game has besides English.

### Changed
- **Sign-in window:** a language button at its bottom right lists the game's 13 languages; picking one sets the game's own language (the same setting as Settings > General > Language, kept for the next start), so new players can switch before they reach Settings.
- **Social > Challenge:** the page's text is much larger, and the page is taller to make room. A text too long for its place shrinks back to fit.
- **Social > Challenge:** the rules say **Energy** instead of mana, the game's own word for it (Starting Energy, Energy gained per turn, Unspent Energy carried over), also in the rules shown on the challenge popup.

- **Skulls in friend matches:** friend challenges now hand out the game's three skulls (the enemy warlord at 20, 10 and 0 life) like the other modes, and cards that trigger on earning a skull work in them; the game gave none in those matches, so the skull bar stayed empty, those cards never fired and the match result said 0. On only when both players have 0.14.32 or newer (the server tells each game the other's version); with an older opponent the match plays as before.
- **Social > Community (new tab):** Replays, with "Watch a replay code" (moved here from Settings > Mod; plays the code on the clipboard) and the replays shared in Discord with /replay, each with a Watch button; and News from the server, with a "Join the Discord" button when the server names one.
- **Social > Challenge layout:** the friend list and the rules list each scroll on their own, with a bar, so more rules can be added later; the rules text is larger; the buttons stay at the bottom; the friend-code box sits right under its label.
- **Challenge by friend code:** Social > Challenge has a friend-code box under the friend list: type someone's friend code and press Find, and you can challenge them with your rules even if you are not friends (the challenge, its online check and the answer get through for 15 minutes).
- **Challenge from a profile or the alliance list:** the Challenge button there now opens Social > Challenge with that player picked (the game's Classic / Skirmish popup is skipped), instead of sending a challenge that cannot carry the mode or any rules (it played as a default challenge).

### Fixes
- **Who goes first (0.14.17):** the server now decides who goes first in every online match and tells both games, so both games always agree. Friend challenges with custom rules could have both players start first, which broke the match on the first move (not yet confirmed in play). The challenge's own "Goes first" choice still applies. Takes effect when both players have 0.14.17 or newer; until then the match plays as before.
- **Social > Friends:** the gold line under the Friends / Requests buttons is as long as on the alliance page, instead of running over the menu on the left.

## 0.14.0 - 2026-10-17

### New
- **Signed updates:** the mod now installs an update from the server only when it is signed by the project owner, so nobody who got into the server, or between your game and the server, can slip you a different mod. Nothing changes for you: updates arrive as before.
- **Friend requests:** adding someone by friend code now sends them a request, and you are on each other's friend lists once they accept. Social > Friends has two buttons at the top: **Friends** (your list) and **Requests** (requests waiting for you, with accept and decline, and the ones you sent, with cancel; the button shows how many are waiting). Removing a friend takes you off each other's lists.
- **Alliances are back (testing):** Social > Alliances lets you create an alliance (free), find and join open ones, invite players, set roles, and leave; the revival server keeps them. Shown only where the server turns it on. Invite players from their profile or by pasting their friend code next to the member count (Moderator and up); alliance chat opens in the chat window for members only. Trophies and alliance badges are not back yet.
- **Recovery code reminder:** if your linked account has no recovery code, the game's own popup on the main menu asks you to make one. **Set it up** opens Settings > Account; **Not now** asks again 3 days later. Until a code exists it asks once each time the game starts, never during a match.
- **Report a player:** in your Profile's battle log, **REPORT** (next to the opponent's name) on a match against another player opens a short form: why (cheating, unsportsmanlike, stalling / AFK, other), what happened, and whether to send your recent game log. The admins review the match from both players' recordings and the server's own record of it.
- **Anti-cheat (report only):** during online matches the game's own built-in detectors are switched on: one notices the game's memory being edited (mana, health and the like), the other the game being sped up. They block nothing; an alarm only goes to the admins as a report.
- **Settings > Account: recovery code.** Once linked, a password box and **Create recovery code** sit under Open profile and Sign out. The code is saved to a file (Downloads, or the mod's UserData folder if the game cannot write there) and shown hidden, with **Show** and **Copy** buttons, so it stays safe on stream. It is the same code the website's Profile page makes; making one cancels the old one.
- **Replay codes:** in your Profile's battle log, **CODE** next to a match copies a short code for its replay (like K7M2-QX9P) to share on Discord or anywhere. To watch one, copy the code and press **Watch a replay code** in **Settings > Mod**. The CODE button (left of your own name) says COPIED when the code is on your clipboard. A code keeps working after the match leaves your battle log; see and remove your codes on the website's Profile page.
- **Friend challenges:** a new rule, **Unspent mana carried over (at most)** (0 to 5). It starts at the chosen mode's value (Skirmish: 1, as in its original rules), and replays keep it; replays saved before this play back without any carried over, as they were played. Both players need this version.
- **Friend challenges:** the - and + buttons are hidden when they would not change a value (at its lowest or highest).
- **Settings > Mod > Compress Card Art:** a tick box under Export card art, on by default (the art is saved at 512 x 512, as before). Untick it to save the art at the game's own size (1024 x 1024) for the best quality; this takes much longer, and slower devices may need 5 minutes or longer.

### Changed
- **Social > Challenge is a tab of its own:** it opens straight away like the game's other tabs, instead of the Alliances tab being swapped for it after the click; picking a friend, a mode or a rule updates the page at once. The game's Alliances tab is kept for when alliances return (hidden until then).
- **Testers button and Testers window:** the Testers button on the menu bar now appears with the rest of the bar, picture included, and the Testers window's PLAY tab opens straight away like the other tabs, instead of being swapped in after a moment.
- **Settings and Profile:** Settings > Mod and Settings > Account appear already finished instead of changing a moment after they open; the battle log's CODE and REPORT buttons are in place as each row appears and stay put while scrolling; your own Record on the Profile page is ready when you open it.
- **Menu ribbons drawn once:** the Social window's CHALLENGE tab and the Testers window's PLAY / DECKS / CARDS tabs are now set up as each window is built, before it appears, instead of being changed after it opens; the buttons no longer change in front of you.
- **Android only:** the game no longer asks for permission to send phone notifications, and no longer shows its own "Please enable notifications" popup (a revival server never sends any).
- **More robust start-up:** the mod's changes to the game are applied one at a time, so if one cannot be applied (for example after a game update) only that one is left out and everything else still works.
- **Settings > Account:** once linked, the "Linked to ..." line and the buttons sit right under the username box, with a new **Open profile on website** button (left) beside Sign out (right), which opens your profile page on the website. The page no longer shows "Could not reach the server" when a re-check of the link fails while the link is known.
- **Error popups** that told you to contact the game's old publisher (support@everguild.com) now give the revival's address, wf-revival@silentanarchist.com.
- **Server name:** the mod now connects to **wf-revival.silentanarchist.com**, and the website moves there too. If your settings still name warpforge.silentanarchist.com, the mod switches them over by itself; while the new name is not reachable yet, the old one (the same server) is used. The old name keeps working for now and will be retired in a later update.

### Fixes
- **Log:** the line "scene changes cannot be followed: Method unstripping failed" no longer repeats over and over in the game's log.
- **Notification counts:** the red count circle no longer shows on the Testers button and Social's Challenge tab (they showed Social's and the alliance invitations' counts).
- **Match connection after a server restart:** a game left open while the server restarted was turned away by the match service every 2 seconds for up to 20 minutes (its match ticket was no longer known). The mod now gets a new ticket as soon as one is refused.
- **Turn timer:** the timer length is taken from the server once per match, so the two players can no longer end up with different timers if the server setting changes mid-match.
- **Smoother menus:** the Social page and the collection no longer search the whole scene over and over in the background.
- **Challenges:** with a friend's custom-rules challenge still waiting, an ordinary challenge you sent to someone else could take those rules on your side only, so the two games disagreed.
- **Shorter logs:** the game's "No listener registered to this event" line (about 70% of a PC log) is left out, and old investigation tools were removed (freeze trace, deck and AI diagnostics). Diagnostic logging stays on.
- **"While in play" on enchantments:** an enchantment's "while in play" ability was applied once and never refreshed, so Space Wolves' Everstorm (all units -1 Ranged) only ever affected the two warlords. It now reaches units played later too; a jammed unit still loses its effects.
- **The AI's deck fits the mode:** the AI could play any of the game's prebuilt decks, whatever the mode (a server setting kept from the original game switched off the game's own bot-list check), so a 12-card Skirmish deck could face a 30-card Classic AI. The check is back on, and the AI's deck now follows the mode's deck size: Classic and Classic Draft get Classic bot decks, Skirmish gets Skirmish ones, and modes with no bot deck of their size (60-card Custom Test) get a Classic one.
- **Friend challenges:** a Custom Test deck can no longer end up in a Classic challenge. Choosing a deck on the Custom Test page also set it as your Classic deck, so a challenge could start with a 60-card deck. Now the challenge stops with a message asking you to pick a Classic deck first.
- **Deck editor > Deck info:** the Troops and Stratagems lines show how many of each the deck has again (they showed only the words).


## 0.13.26-w - 2026-10-14

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


## 0.13.2-w - 2026-10-11

### New
- **Sign-in window, creating an account:** **Create account** now opens its own page in the window instead of making the account straight away. It asks for:
  - the name;
  - the password, and the password again to confirm it;
  - a tick box: "I am 16 or older and agree to the Terms and Privacy notice", with a **Read** button that opens them.

  **Create account** only works once the box is ticked and both passwords match. **Back** returns to signing in.
- **Sign-in window, Terms and Privacy notice:** when the server's notice changes, the next sign-in with a password shows the same tick box before letting you in.
- **Settings > Mod:** the Website tab is now called Mod. It shows the mod's version and has a **Check for updates** button under the Card Creator link: if the server has a newer mod, it downloads it and restarts the game (on Android, closes it; open it again).

## 0.13.0-w - 2026-10-11

### New
- **Social > Challenge:** challenge a friend with your own rules. Pick a mode (Classic or Skirmish) as the starting point, then set warlord health (x1.0 to x3.0) and health change, starting hand, extra cards for the second player, cards drawn per turn, hand limit, starting mana, mana per turn, overtime turn and turn timer. The menu also lists the mode's deck rules. Your friend sees the rules on the challenge popup, and both games play by them.
- **Replays keep their rules:** each new replay saves the rules it was played under, including a challenge's custom rules, and plays back under them. Changing a mode's rules no longer breaks old replays. A replay from a different card set or mod rules version is refused, with the reason shown. Replays saved before this play as before (not yet confirmed in play).

### Changed
- **Custom Test warlord health:** the multiplier applies to the warlord's own health first, then any health change is added.

### Fixes
- **Testers button:** shows up even when the server could not be reached while the game was loading; the mod asks again a few times.

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
