#!/usr/bin/env python3
"""Throwaway (t1-tbz9): the engine's crop beside the reference's crop at the same exposure, then the reference at the
exposure that matches the engine's neutral region.

  python3 -I earth_light.py <views.json> <view> <x0> <y0> <x1> <y1> <out.png>

Three panels with a gap of 4 px, under the set `full`: the column ten as the engine wrote it, the reference through
the engine's tone curve at the engine's stops, and the reference at the matched stops of sheet.py.
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
lights = view["sets"]["full"]
x0, y0, x1, y1 = map(int, sys.argv[3:7])
out = Path(sys.argv[7])
nx0, ny0, nx1, ny1 = view["regions"][view["neutral"]]["box"]

frame = np.asarray(Image.open(Path(lights["columns"]) / "ten-000239.webp").convert("RGB"))
ten = tone.scene_from_display(frame, lights["stops"])
canvas = np.full((1080, 1920, 3), np.nan)
for seed in lights["reference"]:
    for path, x, y in seed:
        part = np.load(path)
        region = canvas[y:y + part.shape[0], x:x + part.shape[1]]
        canvas[y:y + part.shape[0], x:x + part.shape[1]] = np.where(np.isnan(region), part / 2, region + part / 2)
engine_neutral = tone.luminance(ten[ny0:ny1, nx0:nx1]).mean()
reference_neutral = tone.luminance(canvas[ny0:ny1, nx0:nx1]).mean()
match = np.log2(engine_neutral / reference_neutral)
reference = canvas[y0:y1, x0:x1]
if np.isnan(reference).any():
    raise SystemExit("the reference's crops do not cover the box")
crops = [frame[y0:y1, x0:x1], tone.display(reference, lights["stops"]), tone.display(reference, lights["stops"] + match)]
gap = 4
h, w = crops[0].shape[:2]
sheet = np.full((h, len(crops) * w + (len(crops) - 1) * gap, 3), 255, dtype=np.uint8)
for i, c in enumerate(crops):
    sheet[:, i * (w + gap): i * (w + gap) + w] = c
Image.fromarray(sheet).save(out, optimize=True)
print(out, "neutral region: engine", round(float(engine_neutral), 4), "reference", round(float(reference_neutral), 4),
      "W/(m2 sr); matched at", round(float(match), 3), "stops; crop mean luminance: engine",
      round(float(tone.luminance(ten[y0:y1, x0:x1]).mean()), 4), "reference", round(float(tone.luminance(reference).mean()), 4))
