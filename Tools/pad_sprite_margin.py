"""
Guarantees every sprite has a transparent margin on all four sides.

Art that touches its own canvas edge is clipped: there is no room for the walk bob, and any
scaling resamples pixels that have nothing beyond them. This is lossless — it only grows the
canvas with transparent pixels, it never redraws or rescales the artwork. The character keeps
its exact pixels; the metrics that depend on the canvas (visual centre) are re-derived
afterwards by sprite_metrics.py, so growing the canvas is safe.

Run:  python Tools/pad_sprite_margin.py <min_margin> <file...>
"""
import sys
from PIL import Image

def pad(path, want):
    im = Image.open(path).convert("RGBA")
    bbox = im.getbbox()  # bounds of non-zero alpha
    if bbox is None:
        print(f"  {path}: fully transparent, skipped"); return
    l, t, r, b = bbox
    w, h = im.size
    add_l, add_t = max(0, want - l), max(0, want - t)
    add_r, add_b = max(0, want - (w - r)), max(0, want - (h - b))
    if not (add_l or add_t or add_r or add_b):
        print(f"  {path}: already has >={want}px margin, unchanged"); return
    out = Image.new("RGBA", (w + add_l + add_r, h + add_t + add_b), (0, 0, 0, 0))
    out.paste(im, (add_l, add_t))
    out.save(path)
    print(f"  {path}: {w}x{h} -> {out.size[0]}x{out.size[1]} "
          f"(+L{add_l} +T{add_t} +R{add_r} +B{add_b})")

if __name__ == "__main__":
    want = int(sys.argv[1])
    for p in sys.argv[2:]:
        pad(p, want)
