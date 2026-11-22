#!/usr/bin/env python3
"""Throwaway (t1-g4p1): the ratio of each named region over the neutral region, in each column and in the reference.

  python3 -I ratios.py <views.json> <view> <set>

views.json names, for each view: the geometry images (normal and position, from cycles_scene.py with REF_OVERRIDE), the
regions, and for each set of lights (full or nosun) the engine's fixed exposure in stops, the folder of the columns'
frames (8-bit sRGB WebP of the 1920 x 1080 output, <column>-000239.webp) and the reference's crops: for each seed, a
list of [NumPy array of scene-linear RGB from exr_to_npy.py, x of its left column, y of its top row].

A region is a box [x0, y0, x1, y1] in output pixels, x1 and y1 excluded. An optional "normal" keeps the pixels whose
geometric normal lies within 0.05 of it per axis, and an optional "z" or "y" keeps those whose camera-relative position
lies in that range, in m. A region with a face filter loses the pixels on its edge, one pixel deep, so that the jitter
of the engine's frames does not mix two faces.

An engine column's 8-bit image is turned back into scene-linear radiance through the inverse of its tone curve and
exposure (tone.scene_from_display). The reference's ratio is that of the mean of its two seeds. Its noise is half the
difference of the two seeds' ratios. The last row is the engine's frame noise: the absolute difference of each ratio
between the frames 231 and 239 of the column ten, two settled frames of one film.
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
import tone  # noqa: E402

views = json.loads(Path(sys.argv[1]).read_text())
view = views[sys.argv[2]]
lights = view["sets"][sys.argv[3]]
normal = np.load(view["geometry"]["normal"])
position = np.load(view["geometry"]["position"])


def region_mask(spec):
    x0, y0, x1, y1 = spec["box"]
    mask = np.zeros(normal.shape[:2], bool)
    keep = np.ones((y1 - y0, x1 - x0), bool)
    if "normal" in spec:
        keep &= np.all(np.abs(normal[y0:y1, x0:x1] - np.array(spec["normal"])) < 0.05, axis=-1)
    for axis, name in ((1, "y"), (2, "z")):
        if name in spec:
            lo, hi = spec[name]
            keep &= (position[y0:y1, x0:x1, axis] > lo) & (position[y0:y1, x0:x1, axis] < hi)
    mask[y0:y1, x0:x1] = keep
    if "normal" in spec or "z" in spec or "y" in spec:
        inner = mask.copy()
        inner[1:-1, 1:-1] = mask[1:-1, 1:-1] & mask[:-2, 1:-1] & mask[2:, 1:-1] & mask[1:-1, :-2] & mask[1:-1, 2:]
        mask = inner
    return mask


masks = {name: region_mask(spec) for name, spec in view["regions"].items()}
neutral = view["neutral"]


def engine_means(path):
    linear = tone.scene_from_display(np.asarray(Image.open(path).convert("RGB")), lights["stops"])
    lum = tone.luminance(linear)
    return {name: float(lum[m].mean()) for name, m in masks.items()}


def reference_means(crops):
    canvas = np.full(normal.shape[:2], np.nan)
    for path, x, y in crops:
        lum = tone.luminance(np.load(path))
        canvas[y:y + lum.shape[0], x:x + lum.shape[1]] = lum
    means = {}
    for name, m in masks.items():
        values = canvas[m]
        if np.isnan(values).any():
            raise SystemExit(f"region {name}: the reference's crops do not cover it")
        means[name] = float(values.mean())
    return means


rows = [(c, engine_means(Path(lights["columns"]) / f"{c}-000239.webp"), None) for c in lights["order"]]
seeds = [reference_means(crops) for crops in lights["reference"]]
mean = {r: float(np.mean([s[r] for s in seeds])) for r in masks}
noise = {r: abs(seeds[0][r] / seeds[0][neutral] - seeds[1][r] / seeds[1][neutral]) / 2 for r in masks}
rows.append(("reference", mean, noise))

names = [r for r in masks if r != neutral]
print("Pixels per region: " + ", ".join(f"{r} {int(masks[r].sum())}" for r in masks))
print("| Column | " + " | ".join(names) + f" | {neutral}, W/(m² sr) |")
print("|---|" + "---:|" * (len(names) + 1))
for name, lums, err in rows:
    cells = [f"{lums[r] / lums[neutral]:.3f}" + (f" ± {err[r]:.3f}" if err else "") for r in names]
    print(f"| {name} | " + " | ".join(cells) + f" | {lums[neutral]:.4g} |")
early = engine_means(Path(lights["columns"]) / "ten-000231.webp")
late = rows[0][1]
cells = [f"{abs(early[r] / early[neutral] - late[r] / late[neutral]):.3f}" for r in names]
print("| ten, frame 231 against 239 | " + " | ".join(cells) + f" | {early[neutral]:.4g} |")
