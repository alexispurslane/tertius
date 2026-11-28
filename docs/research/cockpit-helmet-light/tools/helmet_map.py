"""Diagnostic tool code: the helmet's light in a film, from two renders of the same frames at the same exposure (normal
helmet, black helmet). It averages the frames of each render in 8-bit sRGB levels, takes the difference, and prints the
difference over named regions and its spatial reach. It writes the mean frames and a map of the difference x 16 around
mid-gray. Pixels where the mean difference passes 24 levels in any channel are the helmet itself (and its silhouette),
so the reach leaves them out.
usage (dev shell python): helmet_map.py <normal dir> <black dir> <out prefix> [first last]"""
import sys, glob
import numpy as np
from PIL import Image

REGIONS = {
    "panelOrange": (770, 65, 120, 130),
    "panelMid": (950, 120, 200, 120),
    "panelLow": (1000, 250, 200, 60),
    "cyanEdge": (1120, 330, 120, 40),
    "wallLeft": (20, 350, 200, 150),
    "floor": (700, 600, 200, 80),
}


def mean(folder, first, last):
    files = sorted(glob.glob(f"{folder}/cockpit-*.png"))[first:last + 1]
    acc = None
    for f in files:
        a = np.asarray(Image.open(f).convert("RGB"), dtype=np.float64)
        acc = a if acc is None else acc + a
    return acc / len(files), len(files)


first, last = (int(sys.argv[4]), int(sys.argv[5])) if len(sys.argv) > 5 else (0, 119)
n, count = mean(sys.argv[1], first, last)
b, _ = mean(sys.argv[2], first, last)
d = n - b
out = sys.argv[3]
Image.fromarray(np.clip(n, 0, 255).astype(np.uint8)).save(out + "-mean-normal.png")
Image.fromarray(np.clip(b, 0, 255).astype(np.uint8)).save(out + "-mean-black.png")
Image.fromarray(np.clip(128 + 16 * d, 0, 255).astype(np.uint8)).save(out + "-helmet-x16.png")
print(f"frames {first}..{last} ({count})")
for name, (x, y, w, h) in REGIONS.items():
    rn, rd = n[y:y + h, x:x + w].reshape(-1, 3).mean(0), d[y:y + h, x:x + w].reshape(-1, 3).mean(0)
    print(f"{name:12s} normal ({rn[0]:6.1f} {rn[1]:6.1f} {rn[2]:6.1f})  helmet ({rd[0]:+6.2f} {rd[1]:+6.2f} {rd[2]:+6.2f})")
helmet = (np.abs(d) > 24).any(axis=2)
grown = helmet.copy()
for _ in range(6):
    g = grown.copy()
    g[1:] |= grown[:-1]; g[:-1] |= grown[1:]; g[:, 1:] |= grown[:, :-1]; g[:, :-1] |= grown[:, 1:]
    grown = g
outside = ~grown
red = d[..., 0]
print(f"helmet pixels {helmet.sum()}, excluded with a 6 px margin {grown.sum()}")
for t in (0.5, 1, 2, 4):
    print(f"pixels outside the helmet with red light > {t} levels: {(outside & (red > t)).sum()}")
print(f"red light summed outside the helmet (levels x pixels): {red[outside].sum():.0f}")
