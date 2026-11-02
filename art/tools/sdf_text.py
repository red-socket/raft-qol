# Copied from the-fourth-chapter/art/tools/sdf_text.py (2026-11-02); keep the two in sync by hand.
"""Render text with one of Raft's own TextMeshPro SDF font assets.

Reads the TMP font asset (YAML, from the AssetRipper export) for glyph metrics and the
atlas PNG's alpha channel for the signed distance field, lays the text out, and writes
an 8-bit grayscale coverage mask (PGM). Colour and compositing are done with ImageMagick.

Usage:
  python3 -I sdf_text.py FONT_ASSET ATLAS_PNG OUT.pgm --size 96 [--width 1400] [--align left|center]
         [--weight 0.0] [--line 1.0] "text with\nnewlines"
"""
import argparse
import re
import subprocess
import sys

import numpy as np
import yaml


def load_font(asset_path):
    src = open(asset_path, encoding="utf-8").read()
    src = re.sub(r"^%TAG.*$", "", src, flags=re.M)
    src = re.sub(r"^--- !u!\d+ &\d+", "---", src, flags=re.M)
    mb = yaml.safe_load(src)["MonoBehaviour"]
    glyphs = {g["m_Index"]: g for g in mb["m_GlyphTable"]}
    chars = {c["m_Unicode"]: glyphs[c["m_GlyphIndex"]] for c in mb["m_CharacterTable"] if c["m_GlyphIndex"] in glyphs}
    face = mb["m_FaceInfo"]
    return chars, face, mb["m_AtlasPadding"]


def load_atlas_alpha(atlas_path):
    w, h = map(int, subprocess.check_output(["magick", "identify", "-format", "%w %h", atlas_path]).split())
    raw = subprocess.check_output(["magick", atlas_path, "-alpha", "extract", "-depth", "8", "gray:-"])
    return np.frombuffer(raw, dtype=np.uint8).reshape(h, w).astype(np.float32) / 255.0


def resize_bilinear(img, out_h, out_w):
    h, w = img.shape
    ys = (np.arange(out_h) + 0.5) * h / out_h - 0.5
    xs = (np.arange(out_w) + 0.5) * w / out_w - 0.5
    y0 = np.clip(np.floor(ys).astype(int), 0, h - 1)
    x0 = np.clip(np.floor(xs).astype(int), 0, w - 1)
    y1 = np.clip(y0 + 1, 0, h - 1)
    x1 = np.clip(x0 + 1, 0, w - 1)
    wy = np.clip(ys - y0, 0, 1)[:, None]
    wx = np.clip(xs - x0, 0, 1)[None, :]
    a = img[y0][:, x0]
    b = img[y0][:, x1]
    c = img[y1][:, x0]
    d = img[y1][:, x1]
    return (a * (1 - wx) + b * wx) * (1 - wy) + (c * (1 - wx) + d * wx) * wy


def layout(text, chars, face, max_width_units, line_mult):
    """Greedy word wrap in font units. Returns list of (line_text, width_units)."""
    def word_w(word):
        return sum(chars.get(ord(ch), chars.get(32))["m_Metrics"]["m_HorizontalAdvance"] for ch in word)
    space = chars[32]["m_Metrics"]["m_HorizontalAdvance"]
    lines = []
    for para in text.split("\n"):
        words = para.split(" ")
        cur, cur_w = "", 0.0
        for wd in words:
            ww = word_w(wd)
            if cur and max_width_units and cur_w + space + ww > max_width_units:
                lines.append((cur, cur_w))
                cur, cur_w = wd, ww
            else:
                cur_w = cur_w + (space if cur else 0) + ww
                cur = f"{cur} {wd}" if cur else wd
        lines.append((cur, cur_w))
    return lines


def render(text, chars, face, pad, atlas, size_px, width_px, align, weight, line_mult):
    s = size_px / face["m_PointSize"]
    line_h = face["m_LineHeight"] * line_mult
    lines = layout(text, chars, face, (width_px / s) if width_px else 0, line_mult)
    content_w = max(w for _, w in lines) if lines else 0
    box_w_units = (width_px / s) if width_px else content_w
    margin = pad + 4
    W = int(np.ceil((box_w_units + 2 * margin) * s))
    H = int(np.ceil((len(lines) * line_h + face["m_DescentLine"] * -1 + 2 * margin) * s))
    canvas = np.zeros((H, W), np.float32)
    AH = atlas.shape[0]
    for li, (ln, lw) in enumerate(lines):
        pen = margin + {"left": 0, "center": (box_w_units - lw) / 2, "right": box_w_units - lw}[align]
        base = margin + face["m_AscentLine"] + li * line_h
        for ch in ln:
            g = chars.get(ord(ch)) or chars[32]
            m, r = g["m_Metrics"], g["m_GlyphRect"]
            if r["m_Width"] > 0 and r["m_Height"] > 0:
                top = AH - (r["m_Y"] + r["m_Height"]) - pad
                crop = atlas[max(top, 0): top + r["m_Height"] + 2 * pad, max(r["m_X"] - pad, 0): r["m_X"] + r["m_Width"] + pad]
                qx = (pen + m["m_HorizontalBearingX"] - pad) * s
                qy = (base - m["m_HorizontalBearingY"] - pad) * s
                qw = int(round((m["m_Width"] + 2 * pad) * s))
                qh = int(round((m["m_Height"] + 2 * pad) * s))
                if qw > 0 and qh > 0:
                    patch = resize_bilinear(crop, qh, qw)
                    x0, y0 = int(round(qx)), int(round(qy))
                    ys, xs = slice(max(y0, 0), min(y0 + qh, H)), slice(max(x0, 0), min(x0 + qw, W))
                    pv = patch[ys.start - y0: ys.stop - y0, xs.start - x0: xs.stop - x0]
                    canvas[ys, xs] = np.maximum(canvas[ys, xs], pv)
            pen += m["m_HorizontalAdvance"]
    # SDF threshold: 0.5 is the edge; spread = atlas padding px. Weight shifts the edge (bolder > 0).
    edge = 0.5 - weight
    e = (0.5 / pad) / s * 0.9
    cov = np.clip((canvas - (edge - e)) / (2 * e), 0, 1)
    cov = cov * cov * (3 - 2 * cov)
    return (cov * 255).astype(np.uint8)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("font_asset")
    ap.add_argument("atlas")
    ap.add_argument("out")
    ap.add_argument("text")
    ap.add_argument("--size", type=float, default=64)
    ap.add_argument("--width", type=float, default=0)
    ap.add_argument("--align", default="left", choices=["left", "center", "right"])
    ap.add_argument("--weight", type=float, default=0.0)
    ap.add_argument("--line", type=float, default=1.0)
    a = ap.parse_args()
    chars, face, pad = load_font(a.font_asset)
    atlas = load_atlas_alpha(a.atlas)
    img = render(a.text.replace("\\n", "\n"), chars, face, pad, atlas, a.size, a.width, a.align, a.weight, a.line)
    with open(a.out, "wb") as f:
        f.write(b"P5\n%d %d\n255\n" % (img.shape[1], img.shape[0]))
        f.write(img.tobytes())
    print(a.out, img.shape[1], "x", img.shape[0], file=sys.stderr)


if __name__ == "__main__":
    main()
