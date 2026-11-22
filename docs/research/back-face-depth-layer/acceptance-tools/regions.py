#!/usr/bin/env python3
"""Tool code (t1-8b4w), copied from stage-1b/tools/pictures/regions.py with main as the base column in place of ten.
The places where a column of a stills job differs from the main column above the noise floor.

  python3 -I regions.py <job folder> <column> [<tile px>] [<x0>,<y0>,<x1>,<y1> ...]

The job folder holds out/pack/<column>-000239.webp for each column and main-000231.webp (mac-stills.sh). The measure is
the signed luminance difference of the 8-bit sRGB values, 0.2126 R + 0.7152 G + 0.0722 B, in units of 1/255, column
minus main. Its mean over each tile of <tile px> x <tile px> output pixels (16 by default) is the tile's difference. The
noise floor is the largest |tile difference| between the frames 231 and 239 of main, two settled frames 8 draws apart of
one run. A region is a connected set of tiles (8 neighbours) of one sign whose |tile difference| passes the noise floor. The script
prints the per-pixel shares and the regions, largest first, and writes <job>/regions-<column>.png: the still of main,
dimmed, with the region tiles in red (brighter) or blue (darker). Boxes after the tile size, in output pixels, leave
their tiles out of the noise floor and of the regions: a part of the view that moves between the two frames of main.
"""
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image
from scipy import ndimage

job, column = Path(sys.argv[1]), sys.argv[2]
tile = int(sys.argv[3]) if len(sys.argv) > 3 else 16
excluded = [tuple(int(v) for v in box.split(",")) for box in sys.argv[4:]]
pack = job / "out" / "pack"


def load(name):
    return np.asarray(Image.open(pack / f"{name}.webp").convert("RGB"), dtype=np.float64)


def luminance(rgb):
    return rgb @ np.array([0.2126, 0.7152, 0.0722])


def tiles(d):
    h, w = d.shape
    return d[: h // tile * tile, : w // tile * tile].reshape(h // tile, tile, w // tile, tile).mean(axis=(1, 3))


ten, ten231, other = load("main-000239"), load("main-000231"), load(f"{column}-000239")
noise = luminance(ten231) - luminance(ten)
diff = luminance(other) - luminance(ten)
t = tiles(diff)
keep = np.ones(t.shape, dtype=bool)
for x0, y0, x1, y1 in excluded:
    keep[y0 // tile:(y1 + tile - 1) // tile, x0 // tile:(x1 + tile - 1) // tile] = False
floor = np.abs(tiles(noise))[keep].max()
above = (np.abs(t) > floor) & keep
regions = []
# Brighter and darker tiles form separate regions: a region holds tiles of one sign.
for sign in (1, -1):
    labels, count = ndimage.label(above & (sign * t > 0), structure=np.ones((3, 3)))
    for k in range(1, count + 1):
        ys, xs = np.nonzero(labels == k)
        values = t[labels == k]
        regions.append({"box": [int(xs.min() * tile), int(ys.min() * tile), int((xs.max() + 1) * tile), int((ys.max() + 1) * tile)],
                        "tiles": int(len(values)), "mean": round(float(values.mean()), 2),
                        "peak": round(float(values[np.abs(values).argmax()]), 2)})
regions.sort(key=lambda r: -r["tiles"] * abs(r["mean"]))
channel = np.abs(other - ten).max(axis=2)
noiseChannel = np.abs(ten231 - ten).max(axis=2)
print(f"{job.name} {column} against main, tiles of {tile} px, {int((~keep).sum())} tiles left out:")
print(f"  per pixel, the largest channel difference above 8 of 255: column {100 * (channel > 8).mean():.2f} %, noise pair {100 * (noiseChannel > 8).mean():.2f} %")
print(f"  mean |luminance difference|: column {np.abs(diff).mean():.3f}, noise pair {np.abs(noise).mean():.3f}")
print(f"  noise floor (largest |tile difference| of the noise pair): {floor:.2f}; largest |tile difference| of the column: {np.abs(t).max():.2f}")
print(f"  tiles above the floor: {int(above.sum())} of {above.size}, brighter {int((t > floor).sum())}, darker {int((t < -floor).sum())}")
for r in regions[:25]:
    print("  ", json.dumps(r))
(job / f"regions-{column}.json").write_text(json.dumps({"tile": tile, "floor": floor, "regions": regions}, indent=1))
view = ten * 0.45
mask = np.kron(t, np.ones((tile, tile)))
mabove = np.kron(above, np.ones((tile, tile))).astype(bool)
h, w = mask.shape
v = view[:h, :w]
v[mabove & (mask > 0)] = v[mabove & (mask > 0)] * 0.4 + np.array([255, 40, 40]) * 0.6
v[mabove & (mask < 0)] = v[mabove & (mask < 0)] * 0.4 + np.array([40, 90, 255]) * 0.6
Image.fromarray(np.clip(view, 0, 255).astype(np.uint8)).save(job / f"regions-{column}.png")
