# Mod signing

Mod 0.14.0 and later install an update from the server only when it carries the project owner's signature
(`WarpforgeRevival/ModSignature.cs`). The signing keys stay on the owner's computer; the mod carries only the public
keys. A server that was broken into, or anyone between a game and the server, cannot make players run a build the
owner did not sign. Test builds are not signed (their own start-up log says so); only builds going to the server are.

On the owner's computer, `C:\modding\warpforge revival\mod-signing\`:

| Path | What it is |
|---|---|
| `keys\` | `mod-signing-key.txt` (the everyday key; never shared), `public-keys.txt` (built into the mod; not secret), `make signing keys.bat` / `make-keys.ps1` (run once; refuses to run again). The spare key `backup-signing-key.txt` belongs off the computer. |
| `to-sign\` | Claude puts the builds that passed testing here (`WarpforgeRevival.dll`, `WarpforgeRevival.Android.dll`). |
| `signed\` | `1 - sign builds.bat` writes the signed copies here. |
| `1 - sign builds.bat` / `sign-mod.ps1` | Signs what is in `to-sign\` into `signed\`. `sign-mod.ps1 -Check` checks `signed\` against `keys\public-keys.txt`. |
| `2 - install signed mods.bat` | Checks the signatures, then installs the signed PC mod into the game and copies the signed Android mod to `..\android patch\mods` (for `3 - put mod on phone.bat`). Your own game then has the same file the server hands out. |
| `3 - push signed mods to server.bat` | Checks the signatures, asks, then copies the signed builds to `W:\mod`. |

The key files are never committed or sent anywhere (`.gitignore` has `*signing-key*.txt`).

## Format

`[build] [signature, 64 bytes] [key id: first 8 bytes of SHA-256 of the public key] ["WFRSIG01"]`. The Ed25519
signature (RFC 8032) is of `"Warpforge Revival mod v1\n" + file name + "\n" + version + "\n" + SHA-256(build)`, the
version read from the build the same way the server reads it. The updater also refuses a build that is not newer
than the running one. Windows and .NET ignore data after the program, so the signed file loads as before.

`sign-mod.ps1` and `keys/make-keys.ps1` carry a copy of `WarpforgeRevival/Ed25519.cs` (Windows PowerShell compiles it with
its own C# 5 compiler); change them together.
