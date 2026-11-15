#!/usr/bin/env python3
"""Throwaway (t1-g4p1): a comparison image of crops at full size, the columns then the reference, with a gap of 4 px.

  python3 -I sheet.py <views.json> <view> <set> <x0> <y0> <x1> <y1> <out.png> [<column> ...]

The engine's columns come as written. The reference is shown through the engine's tone curve, at the exposure that
gives its neutral region the luminance of the column ten's neutral region (views.json), so that equal ratios to the
neutral region show as equal brightness. The box is in output pixels, x1 and y1 excluded.
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
x0, y0, x1, y1 = map(int, sys.argv[4:8])
out = Path(sys.argv[8])
columns = sys.argv[9:] or lights["order"]
neutral = view["regions"][view["neutral"]]["box"]
nx0, ny0, nx1, ny1 = neutral

ten = tone.scene_from_display(np.asarray(Image.open(Path(lights["columns"]) / "ten-000239.webp").convert("RGB")), lights["stops"])
canvas = np.full((1080, 1920, 3), np.nan)
for seed in lights["reference"]:
    for path, x, y in seed:
        part = np.load(path)
        region = canvas[y:y + part.shape[0], x:x + part.shape[1]]
        canvas[y:y + part.shape[0], x:x + part.shape[1]] = np.where(np.isnan(region), part / 2, region + part / 2)
match = np.log2(tone.luminance(ten[ny0:ny1, nx0:nx1]).mean() / tone.luminance(canvas[ny0:ny1, nx0:nx1]).mean())
crops = [np.asarray(Image.open(Path(lights["columns"]) / f"{c}-000239.webp").convert("RGB"))[y0:y1, x0:x1] for c in columns]
reference = canvas[y0:y1, x0:x1]
if np.isnan(reference).any():
    raise SystemExit("the reference's crops do not cover the box")
crops.append(tone.display(reference, lights["stops"] + match))
gap = 4
h, w = crops[0].shape[:2]
sheet = np.full((h, len(crops) * w + (len(crops) - 1) * gap, 3), 255, dtype=np.uint8)
for i, c in enumerate(crops):
    sheet[:, i * (w + gap): i * (w + gap) + w] = c
Image.fromarray(sheet).save(out, optimize=True)
print(out, sheet.shape[1], "x", sheet.shape[0], out.stat().st_size, "bytes; reference shown at", round(match, 3), "stops from the engine's")
