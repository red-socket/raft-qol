"""Build the 2D art for Raft QoL from the game's own textures, item icons and fonts.

Needs: ImageMagick 7 (`magick`), numpy, PyYAML, the AssetRipper export under assets/,
and the key-art render from keyart_scene.py (renders/keyart-raft-porch-1920x1080.png).

  python3 -I raft-qol/art/tools/build_art.py

Outputs go to raft-qol/art/{logo,icon,banner,feature-icons}/.
  python3 -I raft-qol/art/tools/build_art.py header   # only banner/header-1300x372.png
Visual language: the game's inventory UI (item icons on ItemBG slots) and the green
notebook tape, so it reads as a sibling of the-fourth-chapter's art without copying it.
"""
import re
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]          # /home/luke/Projects/raft
ART = ROOT / "raft-qol" / "art"
TEX = ROOT / "assets/primary-content/Assets/Texture2D"
EP = ROOT / "assets/unity-project/ExportedProject/Assets"
FONT_ROCKS = ROOT / "assets/primary-content/Assets/Font/ChineseRocks.otf"
HAND_ASSET = EP / "MonoBehaviour/tt_marks_rough_bold SDF.asset"
HAND_ATLAS = EP / "Texture2D/tt_marks_rough_bold SDF Atlas.png"
ITEM_ATLAS = EP / "Texture2D/ItemIcons.png"         # every inventory icon, 128 px cells
SLOT = TEX / "ItemBG.png"                           # inventory slot, 156x160
SLOT_HI = TEX / "ItemBG_Highlighted.png"
TAPE = TEX / "NoteBook_Thumbnail_8.png"             # green notebook tape
SDF = Path(__file__).with_name("sdf_text.py")
TMP = Path(tempfile.mkdtemp(prefix="raft-qol-art-"))

TAPE_INK = "#2c2420"
TITLE = "QUALITY OF LIFE"
TAGLINE = "A fan-made quality-of-life mod for Raft"

# One icon per feature module, all real item icons from the game.
MODULES = [
    ("storage", "Item_StorageSmall"),       # 4.1 storage & inventory
    ("crafting", "Item_Hammer"),            # 4.2 crafting, stations, refunds
    ("gathering", "ItemPlasticHook"),       # 4.3 hook, nets, debris
    ("raft-control", "Item_SteeringWheel"),  # 4.4 sails, engines, anchors, navigation
    ("hud", "Item_Compass"),                # 4.5 HUD & info
    ("creatures", "Item_SharkTrophy"),      # 4.6 shark, seagulls
    ("building", "Item_Plank"),             # 4.7 building
    ("survival", "Item_ClayBowl"),          # 4.8 needs, containers
    ("farming", "Item_Sprinkler"),          # 4.8 farming
    ("power", "Item_Battery"),              # 4.8 batteries
]
# Banner hotbar: five headline features.
HOTBAR = [
    ("Item_StorageLarge", "Craft from chests"),
    ("ItemPlasticHook", "Faster hook"),
    ("Item_Sail", "Raft controls"),
    ("Item_Hammer", "Full refunds"),
    ("Item_Compass", "Find your raft"),
]


def mg(*a):
    subprocess.run(["magick", *map(str, a)], check=True)


def size_of(p):
    return tuple(map(int, subprocess.check_output(["magick", "identify", "-format", "%w %h", str(p)]).split()))


def item_icon(name):
    """Cut one inventory icon out of ItemIcons.png using its Sprite asset's rect (Unity y is bottom-up)."""
    out = TMP / f"{name}.png"
    if out.exists():
        return out
    s = (EP / "Sprite" / f"{name}.asset").read_text(encoding="utf-8", errors="ignore")
    x, y, w, h = (int(float(v)) for v in re.search(
        r"m_Rect:\s*serializedVersion: 2\s*x: ([\d.]+)\s*y: ([\d.]+)\s*width: ([\d.]+)\s*height: ([\d.]+)", s).groups())
    _, H = size_of(ITEM_ATLAS)
    mg(ITEM_ATLAS, "-crop", f"{w}x{h}+{x}+{H - y - h}", "+repage", out)
    return out


def slot_icon(name, out, highlighted=False, scale=1):
    """An item icon sitting in an inventory slot, as the game draws it (icon about 82% of the slot)."""
    bg = SLOT_HI if highlighted else SLOT
    sw, sh = size_of(bg)
    ic = TMP / f"slot_{name}.png"
    mg(item_icon(name), "-filter", "Lanczos", "-resize", f"{int(sw * 0.82)}x{int(sh * 0.82)}", ic)
    mg(bg, ic, "-gravity", "center", "-geometry", "+0-3", "-composite",
       "-filter", "Lanczos", "-resize", f"{scale * 100}%", out)
    return out


def hand(text, out, size, color="white", weight=0.03):
    """Handwriting in the game's notebook font (TT Marks Rough), rebuilt from its SDF atlas."""
    pgm = TMP / (Path(out).stem + ".pgm")
    subprocess.run(list(map(str, ["python3", "-I", SDF, HAND_ASSET, HAND_ATLAS, pgm, text,
                                  "--size", size, "--weight", weight])), check=True, capture_output=True)
    mg(pgm, "-trim", "+repage", "-background", color, "-alpha", "shape", out)
    return out


def shadowed(src, out, spec="70x6+0+3"):
    mg(src, "(", "+clone", "-background", "black", "-shadow", spec, ")", "+swap",
       "-background", "none", "-layers", "merge", "+repage", out)
    return out


def tape_label(text, out, w, h, text_h_frac=0.52, tilt=-1.5):
    """The green notebook tape with a ChineseRocks label, like the game's notebook tabs."""
    t = TMP / f"tape_{Path(out).stem}.png"
    mg(TAPE, "-resize", f"{w}x{h}!", t)
    lbl = TMP / f"lbl_{Path(out).stem}.png"
    mg("-background", "none", "-fill", TAPE_INK, "-font", FONT_ROCKS, "-pointsize", int(h * 1.2),
       f"label:{text}", "-trim", "+repage", lbl)
    mg(lbl, "-resize", f"{int(w * 0.80)}x{int(h * text_h_frac)}", lbl)
    lw, _ = size_of(lbl)
    mg(t, lbl, "-gravity", "west", "-geometry", f"+{int(w * 0.05 + (w * 0.80 - lw) / 2)}-{int(h * 0.04)}",
       "-composite", "-background", "none", "-rotate", tilt, out)
    return out


def logo(out_white, out_dark):
    """The game's Raft logo over a green tape reading QUALITY OF LIFE."""
    lg = TMP / "raftlogo.png"
    mg(TEX / "Raft_Logo_White.png", "-trim", "+repage", "-resize", "1100x", lg)
    _, lh = size_of(lg)
    tp = tape_label(TITLE, TMP / "title_tape.png", 1450, 210, text_h_frac=0.52, tilt=-2.0)
    tw, th = size_of(tp)
    W, H = max(1100, tw) + 80, lh + th + 120
    for out, dark in ((out_white, False), (out_dark, True)):
        L = lg
        if dark:
            L = TMP / "raftlogo_dark.png"
            mg(lg, "-channel", "RGB", "-fill", "#22313a", "-colorize", "100", "+channel", L)
        sh = shadowed(L, TMP / "logo_shadow.png", "55x10+0+8")
        mg("-size", f"{W}x{H}", "xc:none", sh, "-gravity", "north", "-geometry", "+0+30", "-composite",
           tp, "-gravity", "north", "-geometry", f"+0+{lh + 18}", "-composite",
           "-trim", "+repage", "-bordercolor", "none", "-border", "24", out)


def hotbar(out, slot_scale=1.15, gap=18, label_size=34):
    """A row of inventory slots with the headline features, labelled in notebook handwriting."""
    cells = []
    for i, (name, label) in enumerate(HOTBAR):
        s = slot_icon(name, TMP / f"hb_{i}.png", highlighted=(i == 0), scale=slot_scale)
        sw, sh = size_of(s)
        lb = shadowed(hand(label, TMP / f"hbl_{i}.png", label_size), TMP / f"hbls_{i}.png", "80x4+0+2")
        lw, lh = size_of(lb)
        cw = max(sw, lw) + 4
        cell = TMP / f"hbc_{i}.png"
        mg("-size", f"{cw}x{sh + lh + 8}", "xc:none", s, "-gravity", "north", "-composite",
           lb, "-gravity", "south", "-composite", cell)
        cells.append(cell)
    hrow(cells, out, gap)
    return out


def hrow(imgs, out, gap):
    """Append images left to right with `gap` px between them (top-aligned)."""
    rest = [a for im in imgs[1:] for a in ("(", im, "-gravity", "west", "-splice", f"{gap}x0", ")")]
    mg("-background", "none", imgs[0], *rest, "-gravity", "north", "+append", "+repage", out)
    return out


def vcol(imgs, out, gap):
    """Stack images top to bottom with `gap` px between them (centred)."""
    rest = [a for im in imgs[1:] for a in ("(", im, "-gravity", "north", "-splice", f"0x{gap}", ")")]
    mg("-background", "none", imgs[0], *rest, "-gravity", "center", "-append", "+repage", out)
    return out


def banner(keyart, logo_png, out):
    l = TMP / "banner_logo.png"
    mg(logo_png, "-resize", "700x", l)
    hb = hotbar(TMP / "hotbar.png")
    mg(hb, "-resize", "1000x>", hb)
    tag = shadowed(hand(TAGLINE, TMP / "tag.png", 44), TMP / "tag_s.png")
    mg(keyart, "-resize", "1920x1080^", "-gravity", "center", "-extent", "1920x1080",
       l, "-gravity", "northeast", "-geometry", "+170+60", "-composite",
       hb, "-gravity", "southeast", "-geometry", "+70+150", "-composite",
       tag, "-gravity", "southeast", "-geometry", "+96+56", "-composite", out)


def header(keyart, logo_png, out, W=1300, H=372):
    """Wide mod-page header: a horizon band of the key art (raft on the left), the logo and the five headline
    slots on the right. No labels: at this height they would be too small to read."""
    band = TMP / "header_band.png"
    # Scale the key art to the header width, then keep a band around the raft and the horizon.
    mg(keyart, "-resize", f"{W}x", "-gravity", "north", "-crop", f"{W}x{H}+0+150", "+repage", band)
    l = TMP / "header_logo.png"
    mg(logo_png, "-resize", "x250", l)
    slots = [slot_icon(name, TMP / f"hd_{i}.png", highlighted=(i == 0), scale=0.42) for i, (name, _) in enumerate(HOTBAR)]
    row = shadowed(hrow(slots, TMP / "header_row.png", 8), TMP / "header_row_s.png", "60x5+0+3")
    lw, _ = size_of(l)
    rw, _ = size_of(row)
    right = 70 + (lw - rw) // 2 if lw > rw else 70
    mg(band, l, "-gravity", "northeast", "-geometry", "+70+8", "-composite",
       row, "-gravity", "southeast", "-geometry", f"+{right}+14", "-composite", out)
    return out


def icon(out, size=256):
    """Square mod icon: splash blue, a 2x2 inventory of headline items, green QoL tape."""
    bg = TMP / "icon_bg.png"
    mg(TEX / "Splash_Background.png", "-gravity", "center", "-crop", "1152x1152+0+0", "+repage", "-resize", "1024x1024", bg)
    names = ["Item_StorageLarge", "ItemPlasticHook", "Item_Sail", "Item_Hammer"]
    s = [slot_icon(n, TMP / f"ic_{i}.png", highlighted=(i == 0), scale=2.2) for i, n in enumerate(names)]
    row1 = hrow(s[:2], TMP / "ic_r1.png", 28)
    row2 = hrow(s[2:], TMP / "ic_r2.png", 28)
    grid = vcol([row1, row2], TMP / "ic_grid.png", 28)
    grid_s = shadowed(grid, TMP / "ic_grid_s.png", "60x14+0+10")
    tp = tape_label("QoL", TMP / "icon_tape.png", 600, 190, text_h_frac=0.62, tilt=-4)
    mg(bg, grid_s, "-gravity", "north", "-geometry", "+0+56", "-composite",
       tp, "-gravity", "south", "-geometry", "+0+22", "-composite", "-resize", f"{size}x{size}", out)
    return out


def feature_icons():
    d = ART / "feature-icons"
    outs = []
    for slug, name in MODULES:
        o = slot_icon(name, d / f"{slug}.png")
        slot_icon(name, d / f"{slug}@2x.png", scale=2)
        outs.append(o)
    mg("montage", "-background", "none", "-geometry", "+8+8", "-tile", "5x", *outs, d / "_sheet.png")


def main():
    import sys
    if sys.argv[1:] == ["header"]:
        header(ART / "renders/keyart-raft-porch-1920x1080.png", ART / "logo/raft-qol-logo-white.png",
               ART / "banner/header-1300x372.png")
        print("built", ART / "banner/header-1300x372.png")
        return
    for sub in ("logo", "icon", "banner", "feature-icons"):
        (ART / sub).mkdir(parents=True, exist_ok=True)
    logo(ART / "logo/raft-qol-logo-white.png", ART / "logo/raft-qol-logo-dark.png")
    keyart = ART / "renders/keyart-raft-porch-1920x1080.png"
    if keyart.exists():
        banner(keyart, ART / "logo/raft-qol-logo-white.png", ART / "banner/banner-1920x1080.png")
        header(keyart, ART / "logo/raft-qol-logo-white.png", ART / "banner/header-1300x372.png")
    icon(ART / "icon/icon-256.png", 256)
    icon(ART / "icon/icon-1024.png", 1024)
    feature_icons()
    print("built into", ART)


if __name__ == "__main__":
    main()
