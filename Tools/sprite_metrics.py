"""
Derives the Sprite2D `scale` and `Position` constants used by every BuildVisual() from the
art itself, instead of by hand.

Why this exists
---------------
Each character is drawn with Centered=false and Position = -visualCentre * scale, where
visualCentre was measured by eye once and then frozen as a literal in C#. That works right up
until the art is re-exported: the moment the character occupies a different fraction of its
canvas, every one of those literals is silently wrong, the body hangs off its own collision
circle, and the on-screen size changes. That is exactly what a re-export with added margin
does, so the constants have to be re-derived rather than carried over.

The measurement that must stay constant across a re-export is the character's ON-SCREEN size,
not its scale factor — the same character drawn smaller inside a bigger canvas needs a bigger
scale to look identical.

Usage
-----
  python Tools/sprite_metrics.py snapshot   # BEFORE swapping art: record current on-screen sizes
  python Tools/sprite_metrics.py emit       # AFTER swapping art: print the new C# constants

`snapshot` writes Tools/sprite_metrics.json. Keep it — it is the only record of how big each
character was supposed to be once the old art is gone.
"""

import json
import os
import struct
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SNAPSHOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sprite_metrics.json")

# (label, C# file that owns the constants, png path relative to repo root)
SPRITES = [
    ("Enemy (zombie)",  "Scripts/Entities/Enemy.cs",       "Assets/Sprites/Enemies/zombie.png"),
    ("Sprinter",        "Scripts/Entities/Sprinter.cs",    "Assets/Sprites/Enemies/runner_amarillo.png"),
    ("Demon",           "Scripts/Entities/Demon.cs",       "Assets/Sprites/Enemies/demonio.png"),
    ("Ogre",            "Scripts/Entities/Ogre.cs",        "Assets/Sprites/Enemies/troll_ogro.png"),
    ("Boss (butcher)",  "Scripts/Entities/Boss.cs",        "Assets/Sprites/Enemies/boss_grotesco.png"),
    ("BossGigante",     "Scripts/Entities/BossGigante.cs", "Assets/Sprites/Enemies/boss_gigante.png"),
    ("Player",          "Scripts/Entities/Player.cs",      "Assets/Sprites/Player/jugador.png"),
]


def read_png_rgba(path):
    """Minimal PNG reader. Only handles 8-bit RGBA (colour type 6), which is what every
    sprite in this project exports as — anything else is a red flag worth failing loudly on."""
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", f"{path}: not a PNG"

    pos, idat, width, height = 8, bytearray(), None, None
    while pos < len(data):
        length = struct.unpack(">I", data[pos:pos + 4])[0]
        ctype = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        if ctype == b"IHDR":
            width, height, depth, colour = struct.unpack(">IIBB", body[:10])
            assert depth == 8 and colour == 6, \
                f"{path}: expected 8-bit RGBA, got depth={depth} colour_type={colour}"
        elif ctype == b"IDAT":
            idat += body
        elif ctype == b"IEND":
            break
        pos += 12 + length

    raw = zlib.decompress(bytes(idat))
    stride = width * 4
    out = bytearray(height * stride)
    prev = bytearray(stride)
    src = 0
    for y in range(height):
        filt = raw[src]; src += 1
        line = bytearray(raw[src:src + stride]); src += stride
        # PNG per-scanline filters, undone in place.
        if filt == 1:
            for i in range(4, stride):
                line[i] = (line[i] + line[i - 4]) & 0xFF
        elif filt == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif filt == 3:
            for i in range(stride):
                left = line[i - 4] if i >= 4 else 0
                line[i] = (line[i] + ((left + prev[i]) >> 1)) & 0xFF
        elif filt == 4:
            for i in range(stride):
                a = line[i - 4] if i >= 4 else 0
                b = prev[i]
                c = prev[i - 4] if i >= 4 else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        out[y * stride:(y + 1) * stride] = line
        prev = line
    return width, height, out


def alpha_bounds(path, threshold=8):
    """Bounding box of pixels that are actually visible, plus its centre.

    The centre is taken from this box, NOT from the canvas middle: a character sitting low or
    off to one side of its canvas would otherwise be pinned to the node origin by empty space.
    """
    w, h, px = read_png_rgba(path)
    minx, miny, maxx, maxy = w, h, -1, -1
    stride = w * 4
    for y in range(h):
        row = y * stride
        for x in range(w):
            if px[row + x * 4 + 3] > threshold:
                if x < minx: minx = x
                if x > maxx: maxx = x
                if y < miny: miny = y
                if y > maxy: maxy = y
    assert maxx >= 0, f"{path}: fully transparent"
    return {
        "canvas": [w, h],
        "bounds": [minx, miny, maxx, maxy],
        "size": [maxx - minx + 1, maxy - miny + 1],
        "centre": [(minx + maxx + 1) / 2.0, (miny + maxy + 1) / 2.0],
        # Touching an edge means the art is clipped — the sprite was cropped tight against its
        # own canvas, so there is no margin for any animation that moves or scales it.
        "clipped": minx == 0 or miny == 0 or maxx == w - 1 or maxy == h - 1,
    }


def current_scale(cs_path):
    with open(os.path.join(ROOT, cs_path), encoding="utf-8") as f:
        for line in f:
            if "const float scale" in line:
                return float(line.split("=")[1].strip().rstrip("f;").strip())
    raise SystemExit(f"{cs_path}: no `const float scale` found")


def snapshot():
    out = {}
    for label, cs, png in SPRITES:
        full = os.path.join(ROOT, png)
        if not os.path.exists(full):
            print(f"  SKIP {label}: {png} missing")
            continue
        m = alpha_bounds(full)
        s = current_scale(cs)
        m["scale"] = s
        # The number that must survive the swap.
        m["onScreen"] = [round(m["size"][0] * s, 3), round(m["size"][1] * s, 3)]
        out[label] = m
        flag = "  <-- CLIPPED" if m["clipped"] else ""
        print(f"  {label:16s} art {m['size'][0]}x{m['size'][1]} in {m['canvas'][0]}x{m['canvas'][1]}"
              f" -> on-screen {m['onScreen'][0]}x{m['onScreen'][1]}px{flag}")
    with open(SNAPSHOT, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=2)
    print(f"\nWrote {SNAPSHOT}")


def emit():
    if not os.path.exists(SNAPSHOT):
        raise SystemExit("No snapshot. Run `python Tools/sprite_metrics.py snapshot` on the OLD art first.")
    old = json.load(open(SNAPSHOT, encoding="utf-8"))
    for label, cs, png in SPRITES:
        full = os.path.join(ROOT, png)
        if label not in old or not os.path.exists(full):
            continue
        m = an = alpha_bounds(full)
        want_h = old[label]["onScreen"][1]
        # Height drives the scale: these are top-down characters, and height is the dimension
        # a player reads as "how big is this thing" relative to the arena.
        scale = want_h / m["size"][1]
        cx, cy = m["centre"]
        flag = "   // WARNING: still clipped, no room for animation" if m["clipped"] else ""
        print(f"\n// {label}  ({png}){flag}")
        print(f"//   art {m['size'][0]}x{m['size'][1]} in {m['canvas'][0]}x{m['canvas'][1]},"
              f" visual centre ({cx:g}, {cy:g}) from alpha bounds")
        print(f"const float scale = {scale:.5f}f;")
        print(f"Position = new Vector2(-{cx:g}f * scale, -{cy:g}f * scale),")
        print(f"//   -> on-screen {m['size'][0]*scale:.1f}x{m['size'][1]*scale:.1f}px"
              f" (was {old[label]['onScreen'][0]}x{old[label]['onScreen'][1]})")


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "snapshot"
    if mode == "snapshot":
        snapshot()
    elif mode == "emit":
        emit()
    else:
        raise SystemExit(__doc__)
