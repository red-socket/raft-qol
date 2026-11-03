# Art: Raft QoL (preliminary)

Mod-page and in-game art for Raft QoL. Everything is built from **the game's own assets** (item icons, UI textures, fonts and the 3D menu raft from the AssetRipper export in `assets/`), and the scripts in `tools/` regenerate all of it. Status: **preliminary** (first pass, 2026-11-02). Nothing is used in game or published yet.

**Visual language:** the game's **inventory UI**, with real item icons sitting on the tan `ItemBG` slots, plus the **green notebook tape**. That reads as "tools and convenience", and it keeps the mod clearly a sibling of `the-fourth-chapter`'s art (splash blue and orange tape) rather than a copy of it.

| File | Size | What | Built from |
|---|---|---|---|
| `banner/banner-1920x1080.png` | 1920×1080 | Mod-page banner: key art, logo lockup, a hotbar of five headline features with handwritten labels, and the "A fan-made quality-of-life mod for Raft" tagline | key-art render, logo, `ItemIcons` + `ItemBG`/`ItemBG_Highlighted`, handwriting font |
| `banner/header-1300x372.png` | 1300×372 | Wide mod-page header (Nexus size): a horizon band of the key art with the raft on the left, the logo and the five headline slots (no labels) on the right | key-art render, logo, `ItemIcons` + `ItemBG`/`ItemBG_Highlighted`. `build_art.py header` rebuilds only this |
| `renders/keyart-raft-porch-1920x1080.png` | 1920×1080 | Clean key art with no text: the porch side of the game's main-menu raft, framed left so text fits on the right, low sun from behind the camera, one seagull | `MainMenuScene.glb` (the game's own meshes and textures), `Seagull.glb`, Blender EEVEE |
| `logo/raft-qol-logo-white.png` / `-dark.png` | 1499×1140, transparent | Raft logo over a green tape reading "QUALITY OF LIFE". White for dark or colourful backgrounds, dark for light ones | `Raft_Logo_White.png`, `NoteBook_Thumbnail_8.png`, `ChineseRocks.otf` |
| `icon/icon-256.png`, `icon/icon-1024.png` | 256² / 1024² | Mod icon (Thunderstore needs exactly 256×256): splash blue, a 2×2 inventory (large storage, plastic hook, sail, hammer) and a green "QoL" tape | `Splash_Background.png`, `ItemIcons`, `ItemBG`, tape, `ChineseRocks.otf` |
| `feature-icons/<module>.png` (+ `@2x`) | 156×160 (312×320) | One slot icon per feature module, at the game's native slot size. For the settings UI, the README and mod-page feature lists. `_sheet.png` shows them all | `ItemIcons`, `ItemBG` |

Feature-icon mapping: `storage` = small storage, `crafting` = hammer, `gathering` = plastic hook, `raft-control` = steering wheel, `hud` = compass, `creatures` = shark trophy (the shark head item has the game's "wearable" badge baked in, so it isn't used), `building` = plank, `survival` = clay bowl, `farming` = sprinkler, `power` = battery. Change them in `MODULES` in `tools/build_art.py`.

## Where the game assets come from
- **Item icons:** every inventory icon is a 128 px cell in one atlas, `assets/unity-project/ExportedProject/Assets/Texture2D/ItemIcons.png` (4096²). `build_art.py` cuts an icon out by reading its `Sprite/<name>.asset` rect (Unity's y axis is bottom-up). There are 451 icons; the names match the sprite assets (`Item_Plank`, `ItemPlasticHook`, …).
- **Inventory UI:** `ItemBG.png` (slot), `ItemBG_Highlighted.png` (selected slot), plus `InventorySortIcon.png`, `InventorySortIntoStorage.png` and `Equipslot.png` if you need more UI pieces.
- **Tapes:** `NoteBook_Thumbnail_2/6/7/8.png` (orange, yellow, red, green). raft-qol uses 8 (green).
- **Fonts:** ChineseRocks (`assets/primary-content/Assets/Font/ChineseRocks.otf`, heading font) and TT Marks Rough Bold (notebook handwriting, rebuilt from its TMP SDF atlas by `tools/sdf_text.py`).

## Regenerating
```bash
blender -b --factory-startup -P raft-qol/art/tools/import_menu_scene.py -- assets/primary-content/Assets/SceneHierarchyObject/MainMenuScene.glb /tmp/mainmenu.blend
```
```bash
CAM="7,-26,3.0;4.8,1.5,3.3;28" SUN_AZ=295 SUN_EL=18 GULLS="-3,-3,9.5,40,0.9;2,-9,10.5,140,0.7" blender -b /tmp/mainmenu.blend -P raft-qol/art/tools/keyart_scene.py -- banner raft-qol/art/renders/keyart-raft-porch-1920x1080.png 1920 1080 64
```
```bash
python3 -I raft-qol/art/tools/build_art.py
```
- The import takes about 4 s (the `.blend` is about 280 MB, so keep it out of the repo). The render takes about 5 s, and `build_art.py` about 10 s.
- `import_menu_scene.py`, `keyart_scene.py` and `sdf_text.py` are copies of `the-fourth-chapter/art/tools/` (header line in each). The only change is a `CAM="x,y,z;lookx,looky,lookz;lens"` override in `keyart_scene.py`. The rest of that script's env knobs (`SUN_*`, `SEA_*`, `GULLS`, `FIN`, …) are documented in `the-fourth-chapter/art/README.md`.
- Requirements: Blender 4.5, ImageMagick 7, Python with numpy and PyYAML.
