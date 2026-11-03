<p align="center"><img src="art/logo/raft-qol-logo-dark.png" alt="Raft QoL" width="420"></p>

# Raft QoL

A fan-made quality-of-life mod for [Raft](https://store.steampowered.com/app/648800/Raft/). It makes everyday survival less tedious and leaves the game's balance and feel alone. Every feature can be switched on or off.

> **Status: early development.** Nothing is released yet. The features below work in single-player and when you host; tests with other players are still to come.

## Requirements
- Raft 1.1 (Steam). Developed against build 22312909. A game update can break the mod until it is patched.
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (5.4.23 or later 5.x, `win_x64`).

## Installing
1. Extract BepInEx 5 (`win_x64`) into the Raft game folder, next to `Raft.exe`.
2. **Linux / Steam Deck (Proton) only:** set Raft's Steam launch options to `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
3. Copy the `RaftQoL` folder into `BepInEx/plugins/`.
4. Start the game. Settings are in the **QoL** tab of Raft's Settings window (main menu or pause menu). They are stored in `BepInEx/config/raft-qol.cfg`, and edits to that file apply while the game runs.

## Presets
- **Vanilla+** (default): convenience features only. Balance stays vanilla.
- **Relaxed:** Vanilla+ plus opt-in balance changes.
- **Custom:** set automatically as soon as you change a setting the presets control.

## Features

**Vanilla+** features are on by default. **Relaxed** features change the game's balance, so they are off until you pick the Relaxed preset or switch them on yourself. "Host" means the feature works when you host the world or play alone; as a guest it does nothing (or, for hold to fill, falls back to one item per press).

| Feature | What it does | Preset | Keys | Multiplayer |
|---|---|---|---|---|
| Craft from nearby chests | Crafting counts and uses items in closed chests within 20 m, after your inventory | Vanilla+ | | Host |
| Build from nearby chests | Building, upgrading, repairing and reinforcing use items from nearby chests | Vanilla+ | | Host |
| Quick deposit | Moves your items into nearby chests that already hold the same item. Tools, equipment and the hotbar stay with you | Vanilla+ | G | Host |
| Bulk craft | Shift-click Craft makes 5, Ctrl-click makes as many as you can | Vanilla+ | Shift / Ctrl + click | Anyone |
| Hold to fill | Hold Interact on tanks, grills, smelters, purifiers and cooking pots to keep adding or collecting | Vanilla+ | Hold E | Host |
| Return containers | Bowls, plates and glasses come back after eating and drinking | Vanilla+ | | Anyone |
| Raft controls | All engines on/off, reverse all engines, raise/drop all anchors, open/close all sails | Vanilla+ | K, Shift+K, J, L | Anyone |
| Steering wheel recenter | A released wheel turns back to straight; lock it with a key | Vanilla+ | U | Host |
| Look-at info | Tank levels, battery charge, fuel, cooking time left and chest fill under the crosshair | Vanilla+ | | Anyone |
| Item details | Food, water, durability and cooking result in item descriptions | Vanilla+ | | Anyone |
| Compass and raft marker | Your heading, and distance and direction to the raft when you are away from it | Vanilla+ | | Anyone |
| Keep inventory on death | Keep everything, or keep the hotbar and equipment | Relaxed | | Anyone |
| Full refund | Removing built blocks gives back all materials (adjustable), foundation armor included | Relaxed | | Anyone |
| Faster hook | The hook reels in and grabs debris twice as fast (adjustable) | Relaxed | | Anyone |
| Stacking sails, stronger engines | Extra open sails add speed; engines can move more foundations | Relaxed | | Host |

Every key can be changed in the QoL tab. Keys do nothing while a menu or the chat is open.

## Multiplayer
Each feature runs either for you only, or on the host for everyone, and only uses the game's own network messages, so players without the mod (including console players) can join a modded host, and you can join an unmodded host. The table above says which features need you to be the host.

## Building from source
Requires the .NET SDK (8 or later) and a local Raft install with BepInEx.

```bash
dotnet build src -p:RaftGame="/path/to/steamapps/common/Raft"
```

The build references the game's own assemblies and copies `RaftQoL.dll` into `<Raft>/BepInEx/plugins/RaftQoL/`.

## Disclaimer
This is an unofficial fan project, and it is not affiliated with or endorsed by Redbeet Interactive. Raft and its logo, fonts, textures and models belong to Redbeet Interactive. The artwork in `art/` is built from game assets for this mod's own pages and in-game use.
