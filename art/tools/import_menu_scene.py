# Copied from the-fourth-chapter/art/tools/import_menu_scene.py (2026-11-02); keep the two in sync by hand.
"""Import the game's main-menu scene (AssetRipper .glb export) into a .blend for keyart_scene.py.

  blender -b --factory-startup -P import_menu_scene.py -- /path/to/MainMenuScene.glb /path/to/mainmenu.blend
"""
import sys

import bpy

glb, out = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
bpy.ops.wm.save_as_mainfile(filepath=out)
print("saved", out, len(bpy.data.objects), "objects")
