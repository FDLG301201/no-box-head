"""
Generates side-view weapon sprites in Assets/Sprites/Weapons, matching the blocky,
thick-dark-outline look of the character sprites. Each weapon points right and is centred on
a uniform 120x56 canvas so the player can pin them all to the same "hand" point and just
flip horizontally to face the other way.

Run:  python Tools/generate_weapon_sprites.py
(then let Godot reimport, or run:  Godot --headless --import)
"""

import os
from PIL import Image, ImageDraw

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Sprites", "Weapons")

W, H = 120, 56
OUTLINE = (26, 20, 15, 255)

# Palette
METAL_D = (74, 78, 88, 255)
METAL   = (120, 124, 134, 255)
WOOD    = (128, 78, 42, 255)
WOOD_D  = (92, 54, 28, 255)
BLADE   = (208, 214, 224, 255)
GREEN   = (95, 120, 55, 255)
GREEN_D = (60, 80, 35, 255)
YELLOW  = (222, 190, 40, 255)
BARREL  = (150, 96, 44, 255)
BARREL_D = (110, 68, 30, 255)


def box(d, x0, y0, x1, y1, fill, outline=4):
    """Filled rectangle with a chunky dark outline that never inverts on thin shapes."""
    d.rectangle([x0, y0, x1, y1], fill=OUTLINE)
    o = min(outline, (x1 - x0) // 2, (y1 - y0) // 2)
    if o < 1:
        return
    d.rectangle([x0 + o, y0 + o, x1 - o, y1 - o], fill=fill)


def poly(d, pts, fill):
    # Outline by drawing the polygon slightly grown (cheap: thick outline via line loop).
    d.polygon(pts, fill=fill, outline=OUTLINE)
    for w in range(1, 4):
        d.line(pts + [pts[0]], fill=OUTLINE, width=7)
    d.polygon(pts, fill=fill)


def new_canvas():
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


# ── Weapons (all drawn pointing right, gripped near canvas centre x≈50) ───────

def pistol():
    img, d = new_canvas()
    box(d, 40, 20, 84, 34, METAL)          # slide / barrel
    box(d, 44, 32, 60, 50, METAL_D)        # grip
    box(d, 78, 24, 90, 30, METAL_D)        # muzzle
    return img


def shotgun():
    img, d = new_canvas()
    box(d, 30, 22, 104, 32, METAL)         # long barrel
    box(d, 24, 26, 44, 40, WOOD)           # stock
    box(d, 52, 30, 66, 42, WOOD_D)         # pump
    box(d, 98, 24, 108, 30, METAL_D)       # muzzle
    return img


def machinegun():
    img, d = new_canvas()
    box(d, 28, 20, 106, 30, METAL_D)       # receiver + barrel
    box(d, 26, 24, 42, 38, METAL)          # stock
    box(d, 60, 30, 74, 50, METAL)          # magazine
    box(d, 46, 30, 58, 42, METAL_D)        # grip
    box(d, 100, 22, 110, 28, METAL_D)      # muzzle
    return img


def knife():
    img, d = new_canvas()
    poly(d, [(44, 34), (92, 22), (94, 30), (48, 40)], BLADE)  # blade
    box(d, 34, 30, 50, 40, WOOD)           # handle
    return img


def grenade():
    img, d = new_canvas()
    # round body
    d.ellipse([44, 22, 78, 52], fill=OUTLINE)
    d.ellipse([48, 26, 74, 48], fill=GREEN)
    d.ellipse([54, 30, 64, 38], fill=GREEN_D)   # shading dimple
    box(d, 56, 14, 68, 24, METAL_D)        # cap
    poly(d, [(66, 16), (86, 12), (84, 22), (66, 22)], METAL)  # lever
    return img


def barrel():
    img, d = new_canvas()
    box(d, 46, 14, 76, 50, BARREL)         # body
    d.rectangle([46, 24, 76, 28], fill=BARREL_D)   # band
    d.rectangle([46, 38, 76, 42], fill=BARREL_D)   # band
    return img


def railgun():
    img, d = new_canvas()
    box(d, 26, 24, 108, 30, METAL_D)       # long thin rail barrel
    box(d, 30, 18, 100, 24, METAL)         # upper rail
    box(d, 44, 30, 60, 46, WOOD_D)         # grip
    box(d, 62, 14, 78, 20, YELLOW)         # capacitor coil
    box(d, 100, 20, 110, 28, METAL)        # muzzle coil
    return img


def flakshotgun():
    img, d = new_canvas()
    box(d, 30, 22, 104, 32, METAL)         # long barrel, same silhouette as shotgun
    box(d, 24, 26, 44, 40, WOOD)           # stock
    box(d, 52, 30, 66, 42, WOOD_D)         # pump
    box(d, 98, 22, 110, 32, YELLOW)        # flak-tipped muzzle to read differently from Shotgun
    return img


def chainsaw():
    img, d = new_canvas()
    box(d, 34, 28, 52, 42, WOOD_D)         # rear handle/engine housing
    box(d, 48, 20, 100, 46, METAL_D)       # bar
    for x in range(52, 98, 6):             # teeth along the bar
        d.polygon([(x, 20), (x + 3, 20), (x + 1, 16)], fill=BLADE, outline=OUTLINE)
    box(d, 36, 18, 46, 28, METAL)          # pull-start cap
    return img


def rocketlauncher():
    img, d = new_canvas()
    box(d, 26, 18, 100, 40, METAL_D)       # wide launch tube
    box(d, 30, 22, 96, 36, METAL)          # tube highlight band
    box(d, 20, 24, 34, 40, WOOD_D)         # rear grip/shoulder pad
    box(d, 48, 34, 62, 46, WOOD_D)         # front grip
    poly(d, [(96, 20), (112, 26), (96, 32)], YELLOW)  # exposed warhead tip
    return img


def mine():
    img, d = new_canvas()
    d.ellipse([44, 26, 78, 46], fill=OUTLINE)
    d.ellipse([48, 29, 74, 43], fill=METAL_D)   # squat disc body
    d.ellipse([56, 32, 66, 40], fill=(200, 60, 40, 255))  # armed indicator light
    for ang_x in (46, 76):
        box(d, ang_x - 3, 22, ang_x + 3, 30, METAL)  # trigger prongs
    return img


def flamethrower():
    img, d = new_canvas()
    box(d, 26, 20, 96, 38, METAL_D)        # wide fuel-fed barrel
    box(d, 30, 24, 80, 34, METAL)          # highlight band
    box(d, 18, 22, 34, 44, METAL_D)        # tank slung underneath
    box(d, 44, 30, 58, 42, WOOD_D)         # grip
    poly(d, [(94, 20), (114, 26), (110, 32), (94, 32)], YELLOW)  # flared nozzle
    return img


def cryogun():
    img, d = new_canvas()
    CYAN   = (110, 200, 230, 255)
    CYAN_D = (70, 150, 190, 255)
    box(d, 30, 20, 100, 32, METAL_D)       # barrel
    box(d, 34, 23, 86, 29, CYAN)           # frost-blue highlight
    box(d, 44, 30, 60, 46, METAL)          # grip
    box(d, 20, 16, 36, 30, CYAN_D)         # coolant canister
    box(d, 96, 18, 108, 34, CYAN)          # nozzle vent
    return img


def turret():
    img, d = new_canvas()
    box(d, 20, 30, 60, 46, METAL_D)        # base plate / stand
    box(d, 34, 16, 100, 28, METAL)         # barrel
    box(d, 38, 12, 60, 34, METAL_D)        # turret body
    box(d, 96, 18, 108, 26, YELLOW)        # muzzle tip
    return img


WEAPONS = {
    "pistol.png":      pistol,
    "shotgun.png":     shotgun,
    "machinegun.png":  machinegun,
    "knife.png":       knife,
    "grenade.png":     grenade,
    "barrel.png":      barrel,
    "railgun.png":     railgun,
    "flakshotgun.png": flakshotgun,
    "chainsaw.png":    chainsaw,
    "rocketlauncher.png": rocketlauncher,
    "mine.png":           mine,
    "flamethrower.png":   flamethrower,
    "cryogun.png":        cryogun,
    "turret.png":         turret,
}

if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    print("Generating weapon sprites:")
    for name, builder in WEAPONS.items():
        builder().save(os.path.normpath(os.path.join(OUT_DIR, name)))
        print(f"  {name}")
    print(f"\nWrote {len(WEAPONS)} files to {os.path.normpath(OUT_DIR)}")
