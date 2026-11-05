# Changelog

## Unreleased

### Added
- **Inventory sort button:** the inventory gets the same sort button as chests. It stacks and orders the items in your inventory and backpack; the hotbar and equipment stay as they are. On by default (Vanilla+), mouse and keyboard only, like the chest button.

## 0.1.1, 2026-11-03

### Added
- **Raft Mod Loader:** the mod manager and update mods can check RaftModding for newer versions of Raft QoL.

### Changed
- The source now builds with C# 7.3, so the Raft Mod Loader version can ship it as `.cs` files. Features, settings and the config file are unchanged; the BepInEx version behaves the same as 0.1.0.

## 0.1.0 for Raft Mod Loader, 2026-11-03

### Added
- **Raft Mod Loader (RML) version:** `RaftQoL-<version>.rmod`, with the same features, settings and QoL tab as the BepInEx version. Settings are stored in `mods/ModData/raft-qol/raft-qol.cfg`. The `.rmod` contains the mod's source files, which RML compiles when it loads the mod. The mod can be unloaded and loaded again from RML's mod manager while the game runs; unloading removes its patches, the QoL tab, the HUD and its notifications.

## 0.1.0 (beta), 2026-11-03

First public build. Tested in single-player and when hosting, on Raft 1.1.01 (build 22312909) with BepInEx 5.4.23.6.

### Features
- **Craft from nearby chests:** crafting counts and uses items in closed chests within 20 m, after your inventory. (Host)
- **Build from nearby chests:** building, upgrading, repairing and reinforcing use items from nearby chests. (Host)
- **Quick deposit (G):** moves your items into nearby chests that already hold the same item. Tools, equipment and the hotbar stay with you. (Host)
- **Bulk craft:** Shift-click Craft makes 5, Ctrl-click makes as many as you can.
- **Hold to fill:** hold Interact on tanks, grills, smelters, purifiers and cooking pots to keep adding or collecting items. (Host)
- **Return containers:** bowls, plates and drinking glasses come back after eating and drinking.
- **Raft controls:** K turns all engines on or off, Shift+K reverses them, J raises or drops all anchors, L opens or closes all sails.
- **Steering wheel recenter:** a released wheel turns back to straight; U locks it. (Host)
- **Look-at info:** tank levels, battery charge, fuel, cooking time left and chest fill under the crosshair.
- **Item details:** food, water, durability and cooking results in item descriptions.
- **Compass and raft marker:** your heading, plus the distance and direction to the raft when you are away from it, and to the island your receiver is tuned to.
- **Relaxed preset only:** keep inventory on death, full refund when removing blocks, faster hook, stacking sails and stronger engines, smarter inventory (materials go to the backpack first, and the item in your hand refills when it runs out).

### Settings
- A **QoL** tab in Raft's Settings window (main menu and pause menu) with every feature, its settings and its keys.
- Presets: **Vanilla+** (default, convenience only), **Relaxed** (adds the balance changes), **Custom**.
- Settings are stored in `BepInEx/config/raft-qol.cfg`, and edits to that file apply while the game runs.
