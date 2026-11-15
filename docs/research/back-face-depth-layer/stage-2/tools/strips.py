#!/usr/bin/env python3
"""Throwaway (t1-3j2j): the engine's direct sunlight in a still, and its judgment by the line cast.

  python3 -I strips.py pick <full.png> <nosun.png> <stops> <threshold> <out.npy>
  python3 -I strips.py judge <full.png> <nosun.png> <stops> <cast.npz>

The engine's direct sunlight of a pixel is the linear luminance of the still under full lights minus that of the still
without the sun (TERTIUS_NO_SUN=1), both with the screen tracer off, at one fixed exposure <stops>, in W/(m^2 sr). tone.py
inverts the 8-bit frames. `pick` writes the output pixels whose direct sunlight is above <threshold>, as (x, y) rows, for
sun_cast.py's pixels mode. `judge` reads that cast and prints, for the picked pixels, how many the truth lights, the
engine's light on each set, and the groups of pixels by the face that the camera sees (instance and normal).
"""
import sys
from collections import defaultdict
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
import tone  # noqa: E402


def sunlight(full, nosun, stops):
    def lum(path):
        return tone.luminance(tone.scene_from_display(np.asarray(Image.open(path).convert("RGB")), stops))
    return lum(full) - lum(nosun)


mode = sys.argv[1]
sun = sunlight(sys.argv[2], sys.argv[3], float(sys.argv[4]))
if mode == "pick":
    threshold = float(sys.argv[5])
    ys, xs = np.nonzero(sun > threshold)
    np.save(sys.argv[6], np.stack([xs, ys], 1))
    print(f"{len(xs)} pixels above {threshold} W/(m^2 sr); the frame's largest is {sun.max():.4f}")
    sys.exit()

cast = np.load(sys.argv[5])
x, y = cast["x"], cast["y"]
light = sun[y, x]
share = np.nan_to_num(cast["share"])
truth = share > 0
print(f"{len(x)} pixels: the truth lights {int(truth.sum())}, the engine's light on them {light[truth].sum():.3f},"
      f" on the rest {light[~truth].sum():.3f} (sums of W/(m^2 sr) over pixels)")
groups = defaultdict(list)
for i in range(len(x)):
    n = cast["normal"][i]
    key = tuple(np.round(n, 2).tolist()) if np.all(np.isfinite(n)) else "no surface"
    groups[(int(cast["instance"][i]), key)].append(i)
print("instance, normal, pixels, truth-lit pixels, engine light sum, engine peak, x range, y range, world bounds (m)")
for (inst, n), idx in sorted(groups.items(), key=lambda kv: -light[kv[1]].sum()):
    idx = np.array(idx)
    p = cast["position"][idx]
    print(inst, n, len(idx), int(truth[idx].sum()), round(float(light[idx].sum()), 3), round(float(light[idx].max()), 3),
          (int(x[idx].min()), int(x[idx].max())), (int(y[idx].min()), int(y[idx].max())),
          *(np.round(np.nanmin(p, 0), 3).tolist(), np.round(np.nanmax(p, 0), 3).tolist()) if np.isfinite(p).any() else ())
