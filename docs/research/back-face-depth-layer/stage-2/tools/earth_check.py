#!/usr/bin/env python3
"""Throwaway (t1-3j2j): a directly seen Earth pixel in the engine, in the path trace and by the formula.

  python3 -I earth_check.py <scene.json> <cast.npz> <still.png> <stops> <ref.npy> <rx0> <ry0>

The cast is sun_cast.py's box cast of the exported frame. Its Earth pixels are those whose camera ray meets Earth's
sphere (instance -2 - k for celestial body k, Earth being the one of radius 6,371 km). For each, it takes the point's
normal n and the direction s toward the sun, and the expected radiance albedo x E x max(n . s, 0) / pi, with E the sun's
irradiance from the export. Two albedos, in linear light: EarthGrid's deep blue (35, 85, 170) in sRGB, which most of the
engine's Earth shows, and the area-weighted mean that the path trace uses, (0.0840, 0.1655, 0.4655).

It prints, per pixel of the path trace's crop (whose top left output pixel is rx0, ry0): the engine's radiance from the
8-bit still inverted at <stops> (tone.py), the path trace's, and both expected values. Then the medians of the ratios
over the pixels whose engine color is EarthGrid's deep blue, in the luminance and in the blue channel.
"""
import json
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
import tone  # noqa: E402

scene = json.loads(Path(sys.argv[1]).read_text())
cast = np.load(sys.argv[2])
rgb8 = np.asarray(Image.open(sys.argv[3]).convert("RGB"))
stops = float(sys.argv[4])
ref = np.load(sys.argv[5])[..., :3]
rx0, ry0 = int(sys.argv[6]), int(sys.argv[7])
engine = tone.scene_from_display(rgb8, stops)

sun = next(light for light in scene["lights"] if light["kind"] == "point" and light["distance"] > 1e9)
s = np.array(sun["position"], dtype=np.float64)
s /= np.linalg.norm(s)
irradiance = max(sun["intensity"]) / sun["distance"] ** 2
celestial = [inst for inst in scene["instances"] if inst["flags"] & 16 and np.linalg.norm(inst["position"]) < 1e9]
k = next(i for i, inst in enumerate(celestial) if abs(inst["radius"] - 6.371e6) < 1e3)
earth = celestial[k]
center = np.array(earth["position"], dtype=np.float64)
d = np.linalg.norm(center)
print(f"Earth: center direction {np.round(center / d, 6).tolist()}, distance {d:.0f} m, radius {earth['radius']:.0f} m, "
      f"angular radius {math.degrees(math.asin(earth['radius'] / d)):.3f} deg; sun irradiance {irradiance:.1f} W/m^2, "
      f"toward {np.round(s, 6).tolist()}")
blue = tone.srgb_to_linear(np.array([35, 85, 170]) / 255.0)
mean = np.array([0.0840, 0.1655, 0.4655])
rows = []
for x, y, inst, p in zip(cast["x"], cast["y"], cast["instance"], cast["position"]):
    if inst != -2 - k or not (rx0 <= x < rx0 + ref.shape[1] and ry0 <= y < ry0 + ref.shape[0]):
        continue
    n = (p - center) / np.linalg.norm(p - center)
    cos = max(float(n @ s), 0.0)
    e = engine[y, x]
    r = ref[y - ry0, x - rx0]
    deep_blue = abs(int(rgb8[y, x, 2]) - int(rgb8[y, x, 0])) > 60
    rows.append((x, y, cos, e, r, blue * irradiance * cos / math.pi, mean * irradiance * cos / math.pi, deep_blue))
print("x, y, cos, engine RGB, path trace RGB, expected with deep blue, expected with the mean")
for x, y, cos, e, r, eb, em, _ in rows[:: max(len(rows) // 12, 1)]:
    print(x, y, round(cos, 4), np.round(e, 3).tolist(), np.round(r, 3).tolist(), np.round(eb, 3).tolist(),
          np.round(em, 3).tolist())
sel = [r for r in rows if r[7]]
lum = tone.luminance
print(f"{len(rows)} Earth pixels in the crop, {len(sel)} of them deep blue in the engine")
if sel:
    print("median engine / expected (deep blue): luminance",
          round(float(np.median([lum(r[3]) / lum(r[5]) for r in sel])), 3), "blue",
          round(float(np.median([r[3][2] / r[5][2] for r in sel])), 3))
    print("median path trace / expected (mean): luminance",
          round(float(np.median([lum(r[4]) / lum(r[6]) for r in sel])), 3), "blue",
          round(float(np.median([r[4][2] / r[6][2] for r in sel])), 3))
    print("median engine luminance", round(float(np.median([lum(r[3]) for r in sel])), 3), "path trace",
          round(float(np.median([lum(r[4]) for r in sel])), 3))
