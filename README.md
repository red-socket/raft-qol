<p align="center"><img src="art/logo/raft-qol-logo-dark.png" alt="Raft QoL" width="420"></p>

# Raft QoL

A fan-made quality-of-life mod for [Raft](https://store.steampowered.com/app/648800/Raft/). It makes everyday survival less tedious and leaves the game's balance and feel alone. Every feature can be switched on or off.

> **Status: early development.** Nothing is released yet. Features will be listed here as they ship.

## Requirements
- Raft 1.1 (Steam). Developed against build 22312909. A game update can break the mod until it is patched.
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (5.4.23 or later 5.x, `win_x64`).

## Installing
1. Extract BepInEx 5 (`win_x64`) into the Raft game folder, next to `Raft.exe`.
2. **Linux / Steam Deck (Proton) only:** set Raft's Steam launch options to `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
3. Copy the `RaftQoL` folder into `BepInEx/plugins/`.
4. Start the game. Settings are in `BepInEx/config/raft-qol.cfg`.

## Multiplayer
Each feature runs either for you only, or on the host for everyone. Joining unmodded hosts is supported. The feature list will say which features need the host to have the mod.

## Building from source
Requires the .NET SDK (8 or later) and a local Raft install with BepInEx.

```bash
dotnet build src -p:RaftGame="/path/to/steamapps/common/Raft"
```

The build references the game's own assemblies and copies `RaftQoL.dll` into `<Raft>/BepInEx/plugins/RaftQoL/`.

## Disclaimer
This is an unofficial fan project, and it is not affiliated with or endorsed by Redbeet Interactive. Raft and its logo, fonts, textures and models belong to Redbeet Interactive. The artwork in `art/` is built from game assets for this mod's own pages and in-game use.
