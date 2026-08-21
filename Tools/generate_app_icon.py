"""Generates the app icon set for NoBoxHead.

Same approach as generate_weapon_sprites.py / generate_ammo_sprites.py: the art is drawn in
code rather than hand-authored, so it is reproducible, version-controlled as source, and can be
re-rendered at any size without a designer round-trip.

Outputs into Assets/Icon/:
    icon_512.png              store listing / project icon
    icon_192.png              Android legacy launcher icon
    adaptive_foreground.png   432x432, art centred inside the 66% safe zone Android may crop to
    adaptive_background.png   432x432, flat background plate
    adaptive_monochrome.png   432x432, white-on-transparent for Android 13+ themed icons
    splash.png                480x480, boot splash

Run:  python Tools/generate_app_icon.py
Then re-import in Godot so the .import files are generated.
"""
from PIL import Image, ImageDraw
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "Assets" / "Icon"

# Palette lifted from the game itself so the icon reads as the same product: the arena floor
# dark, the zombie green, and the HUD's health red.
BG_DARK    = (24, 26, 30)
BG_PLATE   = (34, 38, 44)
ZOMBIE     = (122, 168, 76)
ZOMBIE_DK  = (84, 122, 50)
BLOOD      = (168, 28, 28)
BONE       = (232, 226, 208)
CROSSHAIR  = (240, 196, 60)


def rounded_plate(size, radius_frac=0.22, color=BG_PLATE):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = int(size * radius_frac)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=r, fill=color)
    return img


def draw_head(d, cx, cy, s, body=ZOMBIE, shade=ZOMBIE_DK, eyes=True):
    """A blocky zombie head — the 'box head' the game is named after, minus the box."""
    half = s * 0.5
    # Skull
    d.rounded_rectangle([cx - half, cy - half, cx + half, cy + half],
                        radius=int(s * 0.14), fill=body)
    # Shaded lower jaw so it reads 3D at small sizes
    d.rounded_rectangle([cx - half, cy + s * 0.16, cx + half, cy + half],
                        radius=int(s * 0.14), fill=shade)
    if eyes:
        ew, eh = s * 0.17, s * 0.20
        ey = cy - s * 0.12
        for ex in (cx - s * 0.22, cx + s * 0.22):
            d.rectangle([ex - ew / 2, ey - eh / 2, ex + ew / 2, ey + eh / 2], fill=(16, 18, 20))
            # Tiny glint keeps the eyes from looking like flat holes.
            d.rectangle([ex - ew / 2, ey - eh / 2, ex - ew / 6, ey - eh / 6], fill=BLOOD)
        # Stitched mouth
        my = cy + s * 0.30
        d.rectangle([cx - s * 0.26, my - s * 0.03, cx + s * 0.26, my + s * 0.03], fill=(16, 18, 20))
        for i in range(-2, 3):
            x = cx + i * s * 0.12
            d.rectangle([x - s * 0.02, my - s * 0.10, x + s * 0.02, my + s * 0.10], fill=(16, 18, 20))


def draw_crosshair(d, cx, cy, s, color=CROSSHAIR, w=None):
    w = w or max(2, int(s * 0.055))
    r = s * 0.5
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=color, width=w)
    for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
        d.line([cx + dx * r * 0.55, cy + dy * r * 0.55,
                cx + dx * r * 1.35, cy + dy * r * 1.35], fill=color, width=w)


def compose(size, plate=True, safe=1.0, mono=False):
    """safe<1 shrinks the art for Android adaptive icons, which may crop to the middle 66%."""
    img = rounded_plate(size, color=BG_PLATE) if plate else Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx = cy = size / 2
    s = size * 0.46 * safe

    if mono:
        # Themed icons are a single-colour mask: silhouette only, no palette.
        draw_head(d, cx, cy, s, body=(255, 255, 255), shade=(255, 255, 255), eyes=False)
        draw_crosshair(d, cx, cy, s * 1.45, color=(255, 255, 255))
        return img

    # Blood splat behind the head, so the icon still reads at 48px where detail vanishes.
    for (ox, oy, rr) in ((-0.30, 0.26, 0.13), (0.31, 0.20, 0.10), (0.02, 0.38, 0.08)):
        d.ellipse([cx + ox * s * 2 - rr * s, cy + oy * s * 2 - rr * s,
                   cx + ox * s * 2 + rr * s, cy + oy * s * 2 + rr * s], fill=BLOOD)

    draw_head(d, cx, cy, s)
    draw_crosshair(d, cx, cy, s * 1.45)
    return img


def main():
    OUT.mkdir(parents=True, exist_ok=True)

    compose(512).save(OUT / "icon_512.png")
    compose(192).save(OUT / "icon_192.png")

    # Adaptive foreground/background are separate layers; Android composites and masks them.
    fg = compose(432, plate=False, safe=0.66)
    fg.save(OUT / "adaptive_foreground.png")

    bg = Image.new("RGBA", (432, 432), BG_DARK + (255,))
    bg.save(OUT / "adaptive_background.png")

    compose(432, plate=False, safe=0.66, mono=True).save(OUT / "adaptive_monochrome.png")

    # Splash: art on transparent, Godot draws it over splash_screen/background_color.
    compose(480, plate=False, safe=0.8).save(OUT / "splash.png")

    for p in sorted(OUT.glob("*.png")):
        print(f"  {p.name:26} {Image.open(p).size}")
    print(f"Wrote icon set to {OUT}")


if __name__ == "__main__":
    main()
