# Copied from the-fourth-chapter/art/tools/keyart_scene.py (2026-11-02); keep the two in sync by hand.
"""Blender (4.5) scene builder for "Raft: The Fourth Chapter" key art.

Run on a .blend made by importing the game's MainMenuScene.glb (AssetRipper export):
  blender -b mainmenu.blend -P keyart_scene.py -- PRESET OUT.png W H [SAMPLES]

It keeps the game's own decorated menu raft (meshes and textures), removes the UI canvas,
skydome, colliders and lights, and adds a stylised ocean, a gradient sky with soft clouds,
a sun, a simple shark fin and the game's seagull mesh. Tunables come from env vars
(FIN="x,y,rotz", GULLS="x,y,z,rotz,scale;...", SUN_AZ, SUN_EL, SEAZ).
"""
import math
import os
import sys

import bpy
import mathutils

args = sys.argv[sys.argv.index("--") + 1:]
preset, out, W, H = args[0], args[1], int(args[2]), int(args[3])
samples = int(args[4]) if len(args) > 4 else 32
GAME = os.environ.get("RAFT_ASSETS", "/home/luke/Projects/raft/assets/primary-content/Assets")
O = bpy.data.objects
sc = bpy.context.scene

# --- clean the imported menu scene
ui = O.get("MainMenuCanvas")
if ui:
    for c in list(ui.children_recursive) + [ui]:
        O.remove(c, do_unlink=True)
for ob in list(O):
    if ob.type == 'LIGHT':
        O.remove(ob, do_unlink=True)
    elif ob.type == 'MESH':
        z = (ob.matrix_world @ mathutils.Vector(ob.bound_box[0])).z
        nm = ob.name.lower()
        if z < -40 or any(k in nm for k in ("skydome", "collider", "collision")):
            O.remove(ob, do_unlink=True)


def principled(name, color, rough=0.5, spec=0.5):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = color
    b.inputs["Roughness"].default_value = rough
    b.inputs["Specular IOR Level"].default_value = spec
    return m, b


# --- ocean: large plane, bump waves, turquoise near the raft fading to deep blue
bpy.ops.mesh.primitive_plane_add(size=6000, location=(0, 0, float(os.environ.get("SEAZ", "0.55"))))
sea = bpy.context.active_object
m, bsdf = principled("Ocean", (0.02, 0.2, 0.3, 1), rough=float(os.environ.get("SEA_ROUGH", "0.3")), spec=float(os.environ.get("SEA_SPEC", "0.12")))
N, L = m.node_tree.nodes, m.node_tree.links
tc = N.new("ShaderNodeTexCoord")
mp = N.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (1 / 420, 1 / 420, 1)
grad = N.new("ShaderNodeTexGradient"); grad.gradient_type = 'SPHERICAL'
ramp = N.new("ShaderNodeValToRGB")
ramp.color_ramp.elements[0].color = (0.30, 0.55, 0.68, 1)    # far: hazy, blends into the horizon
ramp.color_ramp.elements[1].color = (0.03, 0.42, 0.50, 1)    # near the raft: turquoise
ramp.color_ramp.elements[1].position = 0.97
mid = ramp.color_ramp.elements.new(0.80); mid.color = (0.012, 0.17, 0.30, 1)  # open water: deep teal
L.new(tc.outputs["Object"], mp.inputs["Vector"]); L.new(mp.outputs["Vector"], grad.inputs["Vector"])
L.new(grad.outputs["Fac"], ramp.inputs["Fac"])
# Mostly self-lit so the sea doesn't take hard shadows like a floor; sun only adds a little.
bsdf.inputs["Base Color"].default_value = (0.01, 0.04, 0.06, 1)
wv = N.new("ShaderNodeTexWave"); wv.inputs["Scale"].default_value = 0.35; wv.inputs["Distortion"].default_value = 14
wv.inputs["Detail"].default_value = 4; wv.bands_direction = 'X'
wmp = N.new("ShaderNodeMapping"); wmp.inputs["Scale"].default_value = (0.25, 1.0, 1.0)
L.new(tc.outputs["Object"], wmp.inputs["Vector"]); L.new(wmp.outputs["Vector"], wv.inputs["Vector"])
wr = N.new("ShaderNodeMapRange"); wr.inputs["To Min"].default_value = 0.86; wr.inputs["To Max"].default_value = 1.16
L.new(wv.outputs["Fac"], wr.inputs["Value"])
tint = N.new("ShaderNodeVectorMath"); tint.operation = 'SCALE'
L.new(ramp.outputs["Color"], tint.inputs[0]); L.new(wr.outputs["Result"], tint.inputs["Scale"])
L.new(tint.outputs["Vector"], bsdf.inputs["Emission Color"])
bsdf.inputs["Emission Strength"].default_value = float(os.environ.get("SEA_EMIT", "0.9"))
n1 = N.new("ShaderNodeTexNoise"); n1.inputs["Scale"].default_value = 0.45; n1.inputs["Detail"].default_value = 5
n2 = N.new("ShaderNodeTexWave"); n2.inputs["Scale"].default_value = 0.06; n2.inputs["Distortion"].default_value = 8
mx = N.new("ShaderNodeMath"); mx.operation = 'ADD'
L.new(n1.outputs["Fac"], mx.inputs[0]); L.new(n2.outputs["Fac"], mx.inputs[1])
bump = N.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.55; bump.inputs["Distance"].default_value = 0.5
L.new(mx.outputs[0], bump.inputs["Height"]); L.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
sea.data.materials.append(m)

# --- sky: zenith blue -> pale horizon, with soft white clouds
w = bpy.data.worlds.new("Sky"); sc.world = w; w.use_nodes = True
WN, WL = w.node_tree.nodes, w.node_tree.links
bg = WN["Background"]; bg.inputs["Strength"].default_value = float(os.environ.get("SKY_STR", "1.0"))
wtc = WN.new("ShaderNodeTexCoord")
sep = WN.new("ShaderNodeSeparateXYZ"); WL.new(wtc.outputs["Generated"], sep.inputs[0])
sky_ramp = WN.new("ShaderNodeValToRGB")
cr = sky_ramp.color_ramp
cr.elements[0].position = 0.0; cr.elements[0].color = (0.62, 0.80, 0.90, 1)
cr.elements[1].position = 0.55; cr.elements[1].color = (0.06, 0.25, 0.62, 1)
e = cr.elements.new(0.12); e.color = (0.30, 0.58, 0.86, 1)
WL.new(sep.outputs["Z"], sky_ramp.inputs["Fac"])
cmap = WN.new("ShaderNodeMapping"); cmap.inputs["Scale"].default_value = (1.6, 1.6, 7.0)
WL.new(wtc.outputs["Generated"], cmap.inputs["Vector"])
cn = WN.new("ShaderNodeTexNoise"); cn.inputs["Scale"].default_value = 1.6; cn.inputs["Detail"].default_value = 7; cn.inputs["Roughness"].default_value = 0.58
WL.new(cmap.outputs["Vector"], cn.inputs["Vector"])
cth = WN.new("ShaderNodeMapRange"); cth.inputs["From Min"].default_value = 0.55; cth.inputs["From Max"].default_value = 0.70
WL.new(cn.outputs["Fac"], cth.inputs["Value"])
band = WN.new("ShaderNodeMapRange"); band.inputs["From Min"].default_value = 0.02; band.inputs["From Max"].default_value = 0.10
WL.new(sep.outputs["Z"], band.inputs["Value"])
top = WN.new("ShaderNodeMapRange"); top.inputs["From Min"].default_value = 0.55; top.inputs["From Max"].default_value = 0.25
WL.new(sep.outputs["Z"], top.inputs["Value"])
mul1 = WN.new("ShaderNodeMath"); mul1.operation = 'MULTIPLY'
mul2 = WN.new("ShaderNodeMath"); mul2.operation = 'MULTIPLY'
WL.new(cth.outputs["Result"], mul1.inputs[0]); WL.new(band.outputs["Result"], mul1.inputs[1])
WL.new(mul1.outputs[0], mul2.inputs[0]); WL.new(top.outputs["Result"], mul2.inputs[1])
cmix = WN.new("ShaderNodeMix"); cmix.data_type = 'RGBA'
cmix.inputs["B"].default_value = (1.0, 0.98, 0.95, 1)
WL.new(mul2.outputs[0], cmix.inputs["Factor"]); WL.new(sky_ramp.outputs["Color"], cmix.inputs["A"])
WL.new(cmix.outputs["Result"], bg.inputs["Color"])

# --- sun on the camera side
az = math.radians(float(os.environ.get("SUN_AZ", "40")))
el = math.radians(float(os.environ.get("SUN_EL", "32")))
to_sun = mathutils.Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el)))
bpy.ops.object.light_add(type='SUN')
sun = bpy.context.active_object
sun.rotation_euler = (-to_sun).to_track_quat('-Z', 'Y').to_euler()
sun.data.energy = float(os.environ.get("SUN_E", "4.2")); sun.data.color = (1.0, 0.92, 0.80); sun.data.angle = math.radians(1.5)

# --- shark fin: simple wedge, the silhouette from the game's logo
fin_at = os.environ.get("FIN")
if fin_at:
    fx, fy, fr = map(float, fin_at.split(","))
    # swept-back dorsal fin: leading edge 0 -> apex 2, concave trailing edge 2 -> 5 -> 1
    verts = [(0, 0, 0), (1.55, 0, 0), (1.20, 0, 1.15), (0.70, 0.11, 0.02), (0.70, -0.11, 0.02), (1.15, 0, 0.42)]
    faces = [(0, 3, 2), (3, 5, 2), (3, 1, 5), (0, 2, 4), (4, 2, 5), (4, 5, 1), (0, 4, 1), (0, 1, 3)]
    me = bpy.data.meshes.new("Fin"); me.from_pydata(verts, [], faces); me.update()
    fin = bpy.data.objects.new("SharkFin", me); bpy.context.collection.objects.link(fin)
    fin.location = (fx, fy, float(os.environ.get("SEAZ", "0.55")) - 0.1)
    fin.rotation_euler = (0, 0, math.radians(fr))
    fin.scale = (float(os.environ.get("FIN_S", "0.85")),) * 3
    me.materials.append(principled("Fin", (0.05, 0.065, 0.08, 1), rough=0.4)[0])

# --- seagulls (game mesh)
for spec in filter(None, os.environ.get("GULLS", "").split(";")):
    gx, gy, gz, gr, gs = map(float, spec.split(","))
    before = set(O)
    bpy.ops.import_scene.gltf(filepath=f"{GAME}/Mesh/Seagull.glb")
    for ob in [o for o in O if o not in before and o.parent is None]:
        ob.location = (gx, gy, gz); ob.rotation_euler = (0, 0, math.radians(gr)); ob.scale = (gs, gs, gs)

# --- camera presets: location, look-at, focal length
P = {
    "banner":  ((24, 10, 2.4), (-4, -10, 3.6), 28),
    "banner_low": ((21, 8, 1.4), (-4, -10, 4.2), 26),
    "square":  ((20, 12, 3.0), (-3, -9, 2.8), 30),
    "tall":    ((22, 10, 1.8), (-3, -10, 5.5), 24),
}
loc, look, lens = P[preset]
if os.environ.get("CAM"):  # raft-qol addition: CAM="x,y,z;lookx,looky,lookz;lens"
    c = os.environ["CAM"].split(";")
    loc, look, lens = tuple(map(float, c[0].split(","))), tuple(map(float, c[1].split(","))), float(c[2])
bpy.ops.object.camera_add(location=loc)
cam = bpy.context.active_object
cam.rotation_euler = (mathutils.Vector(look) - mathutils.Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
cam.data.lens = lens; cam.data.clip_end = 8000; sc.camera = cam

sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = W, H, 100
sc.view_settings.view_transform = 'AgX'
looks = [i.identifier for i in sc.view_settings.bl_rna.properties['look'].enum_items]
want = os.environ.get('LOOK', 'Punchy')
sc.view_settings.look = next((l for l in looks if want in l), 'None')
engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
sc.eevee.taa_render_samples = samples
sc.render.film_transparent = False
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDERED", out)
