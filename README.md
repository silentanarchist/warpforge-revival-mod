# Warpforge Revival mod

A [MelonLoader](https://melonwiki.xyz/) mod that lets *Warhammer 40,000: Warpforge* run against a
community-hosted server now that the game's own servers are closed.

This repository is the complete source of `WarpforgeRevival.dll`. The server it talks to is in
[warpforge-revival-server](https://github.com/silentanarchist/warpforge-revival-server).

This is an unofficial fan project. It is not affiliated with or endorsed by Everguild or
Games Workshop. It contains no game files: you need your own copy of the game.

## What it does

- **Sends the game's server requests to a revival server** instead of the closed ones (`PlayFabTransport.cs`).
- **Matches** run on the revival server's own match service. The game never contacts Photon's
  servers (`PhotonService.cs`, `Matchmaking.cs`).
- **Global chat** runs through the revival server (`GlobalChat.cs`).
- **Long Game**, a 60-card mode added by the revival (`LongGame.cs`).
- **Profile additions:** win/loss record and skulls, friend codes, free renames (`ProfileStats.cs`, `ProfilePage.cs`).
- **Account link** to the server's card creator site, optional (`AccountPage.cs`).
- **Updates itself** from the server it is connected to, after asking (`Updater.cs`).
- Smaller fixes: the game keeps running in the background, scrollbars on long lists, missing text
  supplied by the server, menus for closed features hidden.

Each file starts with a comment saying what it is for and why.

## Download

The latest build is [releases/WarpforgeRevival-0.10.11.zip](releases/WarpforgeRevival-0.10.11.zip)
(open it and press "Download raw file"). It is built from the source in this repository and
holds the mod, the `manifest.json` it needs, and a README with the steps below.

## Installing

1. Install MelonLoader 0.7.3 into the game folder and start the game once.
2. Put `WarpforgeRevival.dll` in the game's `Mods` folder - either straight in `Mods`, or in
   `Mods\WarpforgeRevival\` together with a `manifest.json` (MelonLoader only loads mods from a
   sub-folder that has one).
3. Start the game. Settings are created in `UserData\WarpforgeRevival.cfg`; set `ServerUrl` there
   if you are not using the default server.

## Building

You need the .NET SDK (6 or newer) and three folders of reference libraries from your own
installation. They are not in this repository because they are not ours to publish.

| Folder | Where it comes from |
|---|---|
| `refs/net6` | the .NET 6 runtime libraries (`Microsoft.NETCore.App` 6.x) |
| `refs/ml` | `MelonLoader\net6` in your game folder |
| `refs/il2cpp` | `MelonLoader\Il2CppAssemblies` in your game folder (created on the game's first run with MelonLoader) |

```
dotnet build WarpforgeRevival/WarpforgeRevival.csproj -c Release -p:RefsDir=<path to refs>
```

The result is `WarpforgeRevival/bin/Release/net6.0/WarpforgeRevival.dll`.

Built for game version 1.35.0. The mod hooks specific game functions, so another game version
would need those checked again.

## Licence

MIT - see [LICENSE](LICENSE). The licence covers this mod's code only, not the game.
