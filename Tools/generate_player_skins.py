"""
Generates player skin variants in Assets/Sprites/Player from the base jugador.png.

Only the shirt (main + shadow) and cap colours are swapped; skin tone and the dark outline
are left alone so every variant still reads as the same character. Adding a skin = adding an
entry to SKINS here plus a matching entry in PlayerSkins.cs.

Run:  python Tools/generate_player_skins.py
(then let Godot reimport, or run:  Godot --headless --import)
"""

import os
from PIL import Image

BASE_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Sprites", "Player")
BASE_FILE = os.path.join(BASE_DIR, "jugador.png")

# Colours sampled from the base sprite.
SHIRT      = (91, 107, 74)
SHIRT_DARK = (70, 81, 56)
CAP        = (194, 42, 42)

# name -> (shirt, shirt shadow, cap)
SKINS = {
    "skin_soldier": ((91, 107, 74),   (70, 81, 56),    (194, 42, 42)),
    "skin_blue":    ((58, 92, 150),   (42, 68, 112),   (232, 196, 60)),
    "skin_crimson": ((162, 48, 48),   (122, 34, 34),   (44, 44, 52)),
    "skin_violet":  ((110, 68, 152),  (82, 50, 114),   (232, 196, 60)),
    "skin_teal":    ((48, 130, 124),  (34, 98, 94),    (232, 120, 60)),
    "skin_orange":  ((198, 110, 42),  (150, 80, 30),   (60, 60, 70)),
}


def recolor(img, shirt, shirt_dark, cap):
    out = img.copy()
    px = out.load()
    w, h = out.size
    mapping = {SHIRT: shirt, SHIRT_DARK: shirt_dark, CAP: cap}
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            repl = mapping.get((r, g, b))
            if repl:
                px[x, y] = (repl[0], repl[1], repl[2], a)
    return out


if __name__ == "__main__":
    base = Image.open(BASE_FILE).convert("RGBA")
    print("Generating player skins:")
    for name, (shirt, shirt_dark, cap) in SKINS.items():
        recolor(base, shirt, shirt_dark, cap).save(
            os.path.normpath(os.path.join(BASE_DIR, f"{name}.png")))
        print(f"  {name}.png")
    print(f"\nWrote {len(SKINS)} skins to {os.path.normpath(BASE_DIR)}")
