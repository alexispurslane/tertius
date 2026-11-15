#!/usr/bin/env python3
"""Throwaway (t1-3j2j): one sheet of a strip's crop, the engine's panels beside the path trace's and the geometry's.

  python3 -I leak_sheet.py <x0> <y0> <x1> <y1> <zoom> <stops> <full.png> <nosun.png> <ref.npy> <rx0> <ry0>
      <cast.npz> <engine.npz> <out.png>

The crop is x0..x1, y0..y1 in output pixels of 1920 x 1080, drawn <zoom> times its size. The panels, left to right, or
from top to bottom for a crop wider than twice its height:

1. The engine's still under full lights, with the screen tracer off, at <stops>.
2. The same without the sun (TERTIUS_NO_SUN=1).
3. The engine's direct sunlight: the linear difference of 1 and 2, as gray, drawn through the engine's tone curve at <stops>.
4. The path trace with the sun alone and a black Earth, <ref.npy>, whose crop starts at (rx0, ry0), drawn the same way.
5. The engine's lit share of the sun from the CPU copy of its filter (engine_shadow.py's <engine.npz>), 0 black to 1 white.
6. The geometry's sunlit share (sun_cast.py's <cast.npz>), 0 black to 1 white.

Panels 5 and 6 need a box cast of the same crop at step 1. A pixel that sees no surface is dark red in them.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
import tone  # noqa: E402

a = sys.argv[1:]
x0, y0, x1, y1, zoom = map(int, a[0:5])
stops = float(a[5])
full_png, nosun_png, ref_npy = a[6], a[7], a[8]
rx0, ry0 = int(a[9]), int(a[10])
cast, engine, out = np.load(a[11]), np.load(a[12]), a[13]
w, h = x1 - x0, y1 - y0


def still(path):
    return np.asarray(Image.open(path).convert("RGB"))[y0:y1, x0:x1]


def gray(linear):
    return tone.display(np.repeat(linear[..., None], 3, -1), stops)


full, nosun = still(full_png), still(nosun_png)
sun = tone.luminance(tone.scene_from_display(full, stops)) - tone.luminance(tone.scene_from_display(nosun, stops))
ref = np.load(ref_npy)[y0 - ry0:y1 - ry0, x0 - rx0:x1 - rx0, :3]


def share_map(values):
    grid = np.zeros((h, w, 3), np.uint8)
    grid[...] = (90, 0, 0)
    for x, y, v in zip(cast["x"], cast["y"], values):
        if x0 <= x < x1 and y0 <= y < y1 and np.isfinite(v):
            grid[y - y0, x - x0] = int(round(255 * v))
    return grid


panels = [full, nosun, gray(np.maximum(sun, 0)), gray(tone.luminance(ref)), share_map(engine["lit"]),
          share_map(cast["share"])]
gap = 4
# A crop wider than twice its height stacks its panels from top to bottom, in the same order.
stack = w > 2 * h
step = (0, h * zoom + gap) if stack else (w * zoom + gap, 0)
size = (w * zoom, len(panels) * step[1] - gap) if stack else (len(panels) * step[0] - gap, h * zoom)
sheet = Image.new("RGB", size, (255, 255, 255))
for i, p in enumerate(panels):
    sheet.paste(Image.fromarray(p).resize((w * zoom, h * zoom), Image.NEAREST), (i * step[0], i * step[1]))
sheet.save(out)
print("wrote", out, sheet.size)
