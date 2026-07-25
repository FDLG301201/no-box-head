"""
Generates ammo pickup sprites in Assets/Sprites/Ammo, in the same blocky thick-outline style
as the weapons. One per ammo type routed by AmmoPack.WeaponType:
  pistol / shotgun / machinegun / grenade / barrel

Run:  python Tools/generate_ammo_sprites.py
(then let Godot reimport, or run:  Godot --headless --import)
"""

import os
from PIL import Image, ImageDraw

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Sprites", "Ammo")

S = 48
OUTLINE = (26, 20, 15, 255)

BRASS   = (226, 184, 66, 255)
BRASS_D = (188, 148, 44, 255)
COPPER  = (205, 122, 52, 255)
RED     = (206, 58, 46, 255)
RED_D   = (168, 42, 34, 255)
GREEN   = (78, 126, 58, 255)
GREEN_D = (54, 92, 42, 255)
BARREL  = (150, 96, 44, 255)
BARREL_D = (110, 68, 30, 255)
GRN_BODY = (95, 120, 55, 255)
GRN_DIMP = (60, 80, 35, 255)
METAL_D = (74, 78, 88, 255)


def new_canvas():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def box(d, x0, y0, x1, y1, fill, o=3):
    d.rectangle([x0, y0, x1, y1], fill=OUTLINE)
    o = min(o, (x1 - x0) // 2, (y1 - y0) // 2)
    if o >= 1:
        d.rectangle([x0 + o, y0 + o, x1 - o, y1 - o], fill=fill)


def bullet(d, cx, base_y, h=20, w=9, body=BRASS, tip=COPPER):
    """Upright bullet: casing with a pointed tip, centred on cx, base at base_y."""
    x0, x1 = cx - w // 2, cx + w // 2
    body_top = base_y - h + 6
    box(d, x0, body_top, x1, base_y, body)                # casing
    # tip (triangle) with outline
    tip_pts = [(x0, body_top), (x1, body_top), (cx, body_top - 8)]
    d.polygon(tip_pts, fill=OUTLINE)
    d.polygon([(x0 + 2, body_top - 1), (x1 - 2, body_top - 1), (cx, body_top - 6)], fill=tip)
    d.rectangle([x0, base_y - 3, x1, base_y - 2], fill=BRASS_D)  # rim detail


# ── Ammo types ────────────────────────────────────────────────────────────────

def pistol():
    img, d = new_canvas()
    bullet(d, 16, 40, h=22)
    bullet(d, 26, 42, h=24)
    bullet(d, 36, 40, h=22)
    return img


def machinegun():
    img, d = new_canvas()
    box(d, 8, 24, 40, 42, GREEN)          # ammo box
    d.rectangle([8, 30, 40, 33], fill=GREEN_D)  # label stripe
    # rounds poking out of the top
    for cx in (14, 22, 30, 38):
        bullet(d, cx, 26, h=16, w=7, body=BRASS)
    return img


def shotgun():
    img, d = new_canvas()
    # two shells: red hull, brass base at the bottom
    for cx in (18, 32):
        box(d, cx - 6, 12, cx + 6, 40, RED)
        box(d, cx - 6, 32, cx + 6, 42, BRASS)
        d.rectangle([cx - 6, 20, cx + 6, 22], fill=RED_D)  # crimp line
    return img


def grenade():
    img, d = new_canvas()
    d.ellipse([12, 16, 40, 44], fill=OUTLINE)
    d.ellipse([15, 19, 37, 42], fill=GRN_BODY)
    d.ellipse([21, 25, 29, 33], fill=GRN_DIMP)   # shading
    box(d, 21, 8, 31, 18, METAL_D)               # cap
    d.polygon([(29, 10), (42, 7), (40, 15), (29, 15)], fill=OUTLINE)  # lever
    d.polygon([(30, 11), (39, 9), (38, 14), (30, 14)], fill=METAL_D)
    return img


def barrel():
    img, d = new_canvas()
    box(d, 14, 8, 34, 42, BARREL)
    d.rectangle([14, 18, 34, 21], fill=BARREL_D)
    d.rectangle([14, 30, 34, 33], fill=BARREL_D)
    return img


AMMO = {
    "pistol.png":     pistol,
    "machinegun.png": machinegun,
    "shotgun.png":    shotgun,
    "grenade.png":    grenade,
    "barrel.png":     barrel,
}

if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    print("Generating ammo sprites:")
    for name, builder in AMMO.items():
        builder().save(os.path.normpath(os.path.join(OUT_DIR, name)))
        print(f"  {name}")
    print(f"\nWrote {len(AMMO)} files to {os.path.normpath(OUT_DIR)}")
