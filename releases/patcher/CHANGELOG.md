# Warpforge Patcher changelog

The Android app that patches the game on the device itself. Write "device", not "phone", unless something only applies to phones. Newest first. A heading ending in
"(test build)" is published as a pre-release.

## 0.2.8 - 2026-10-17

### Changes
- The app now looks for its updates in the merged Warpforge Revival repository (`silentanarchist/warpforge-revival-mod`), where new versions of the app are released from now on. Update to this version to keep getting updates.

## 0.2.7 - 2026-10-14

### Changes
- The community server is now `wf-revival.silentanarchist.com`, matching the mod. Settings that still name `warpforge.silentanarchist.com` keep working; it's the same server.

## 0.2.6 - 2026-10-14

### New
- **Server setting on the Tools page.** Shows which server the mod connects to and lets you change it, or switch back to the community server, without editing files. Close the game fully first; the change applies the next time it starts.

### Changes
- The app's text says "device" instead of "phone".

## 0.2.5 - 2026-10-14

### Fixed
- Tapping outside Android's install, removal or update confirmation could leave the app stuck. On devices where that happens, the button now reads "Continue: …" and brings the confirmation back.

### New
- **Tools page.** A menu under the title switches between **Patcher** and **Tools**. Tools holds:
  - **The black screen fix.** Apply or remove it on an already patched game, without patching again.
  - **Save card art export to Downloads.**
  - **The signing key buttons.**

### Changes
- The log area at the bottom is half as tall.

## 0.2.4 - 2026-10-13

### New
- **Save card art export to Downloads.** After exporting card art in the game (Settings > Mod > Export card art), this button copies the export to your phone's Download folder so it's easy to find. The app shows when the export was made and its size.

## 0.2.3 - 2026-10-13

### New
- **The app updates itself.** When a newer Warpforge Patcher is released, a pop-up offers it when you open the app: **Update now** or **Later**. Updating downloads it, checks it and installs it over the old version. Your saved signing key is kept, so nothing needs redoing.
- **Check for updates** button in the top right corner, to look for a new version at any time.
- If you choose **Later**, an "Update this app" button stays under the status until you update.
- Updates are only installed when they come from this project's GitHub releases, match the checksum in their release notes, and are signed with the same key as the app you have.

Install this version once by hand. From now on, updates arrive through the app.

## 0.2.1 - 2026-10-11

### Changes
- The "Export the game's downloaded files" button is gone. The revival server hands those files out now, so they no longer need backing up.
- Before it removes the installed game, the app asks a plain "Remove the game first?" (Continue or Stop) and no longer asks about a backup.

Tested on a phone.

## 0.2.0 - 2026-10-11

### New
- First release. Patches your own copy of the game (version 1.35.0) on the phone: from the
  installed game or from a `.apks` / `.xapk` backup. Then it installs the patched game and puts the
  current mod in place. Same patching as the PC's `1 - patch.bat`.
- Warns before it has to remove the installed game, because that erases the game's downloaded files
  for good, and goes on only once you confirm you have a backup.
- **Export the game's downloaded files**, on Android 12 and older (Android 13 and newer do not let any
  app read them; use a PC there).
- **Fix black menu after loading**, ticked by default on Pixel 6 and later and on Samsungs with an
  Exynos chip and Mali graphics: shows a still picture instead of the opening video.
- **Update the mod only**, **Share logs** (never includes your game login), and saving or loading the
  app's signing key.

Tested in MuMu Player and on a Pixel 6 Pro.
