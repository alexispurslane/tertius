"""Diagnostic tool code: a side view (z, y) and a top view (z, x) of the shuttle art's glow patches (orange) and the
demo's lamp lights (blue) in the shuttle's frame, drawn from the data of art_glow_patches.py.
usage (dev shell python): art_glow_map.py <lunarshuttle model.glb> <out.png>"""
import sys, subprocess, os
from PIL import Image, ImageDraw, ImageFont

here = os.path.dirname(os.path.abspath(__file__))
rows = [r.split(' | ') for r in subprocess.run([sys.executable, f"{here}/art_glow_patches.py", sys.argv[1]],
                                               capture_output=True, text=True, check=True).stdout.splitlines()[1:]]
vec = lambda s: [float(v) for v in s.strip('()').split(',')]
patches = [(r[0].split('.')[-1], vec(r[3]), vec(r[4]), vec(r[7])[0]) for r in rows]
lamps = [("ceiling-aft 18 W", (-0.65, 3.04, 2.30), (0.65, 3.09, 3.30)), ("ceiling-mid-aft 18 W", (-0.65, 3.04, -0.95), (0.65, 3.09, 0.05)),
         ("ceiling-ramp 18 W", (-0.60, 3.04, -3.55), (0.60, 3.09, -2.55)), ("ceiling-fore 15 W", (-0.52, 3.04, -6.05), (0.52, 3.09, -5.15)),
         ("ceiling-cockpit 12 W", (-0.32, 1.93, -10.95), (0.32, 1.98, -10.35))]
sconces = [("sconce fore 4 W", -5.0), ("sconce aft 4 W", 0.2)]
font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 15)
W, H, S = 1400, 900, 60  # pixels, pixels, pixels per metre
img = Image.new("RGB", (W, H), "white")
d = ImageDraw.Draw(img)


def view(top, title, other, lo, hi):
    # z runs left (nose, -16 m) to right (+4 m); the other axis runs up.
    zx = lambda z: 40 + (z + 16) * S
    oy = lambda v: top + 30 + (hi - v) * S
    d.text((40, top), title, fill="black", font=font)
    d.rectangle([zx(-16), oy(hi), zx(4), oy(lo)], outline="#999")
    for z in range(-16, 5, 2):
        d.text((zx(z) - 8, oy(lo) + 4), f"{z}", fill="#666", font=font)
    for name, a, b in lamps:
        d.rectangle([zx(a[2]), oy(max(a[other], b[other]) + 0.03), zx(b[2]), oy(min(a[other], b[other]) - 0.03)], fill="#2a6fdb")
        d.text((zx(a[2]), oy(b[other]) - 20), name, fill="#2a6fdb", font=font)
    if other == 1:
        for name, z in sconces:
            d.rectangle([zx(z - 0.08), oy(1.65), zx(z + 0.08), oy(0.85)], fill="#2a6fdb")
            d.text((zx(z) + 6, oy(0.9)), name, fill="#2a6fdb", font=font)
    for i, (name, a, b, red) in enumerate(patches):
        d.rectangle([zx(a[2]), oy(b[other]), zx(b[2]), oy(a[other])], outline="#e07000", width=3)
        d.text((zx(b[2]) + 4 + 0 * i, oy(b[other]) + 14 * (i % 4) - 20), f"{name} {red:.1f} W", fill="#e07000", font=font)


view(10, "Side view: z (m, nose left) across, y (m) up. Blue: demo lamp lights. Orange: art glow patches, red-channel power.", 1, -3, 3.5)
view(470, "Top view: z (m) across, x (m) up.", 0, -3, 3)
img.save(sys.argv[2])
