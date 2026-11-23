#!/usr/bin/env python3
"""Tool code (t1-r9vj), from t1-g4p1's sheet.py: a sheet of the columns of one view, each with its name, then the path
trace when the set has one.

  python3 -I lit_sheet.py <views.json> <view> <set> <out.png> <scale> [<x0> <y0> <x1> <y1>] [-- <column> ...]

Without a box, each tile is the whole frame at <scale>, two tiles to a row. With a box (output pixels, x1 and y1
excluded), each tile is that crop at <scale>, all in one row. The engine's columns come as written. The path trace is
shown through the engine's tone curve at the exposure that gives its neutral region (the region's pixels, not its
box as in sheet.py) the luminance of the column ten's neutral region, so that equal ratios to the neutral region show as equal brightness. With
"neutral_from" in the set, that match comes from the other set's ten and path trace (see lit_ratios.py).
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).parent.parent))
sys.path.insert(0, str(Path(__file__).parent))
import regions  # noqa: E402
import tone  # noqa: E402

args = sys.argv[1:]
columns_given = args.index("--") if "--" in args else len(args)
fixed, chosen = args[:columns_given], args[columns_given + 1:]
views = json.loads(Path(fixed[0]).read_text())
view = views[fixed[1]]
lights = view["sets"][fixed[2]]
out = Path(fixed[3])
scale = float(fixed[4])
box = list(map(int, fixed[5:9])) if len(fixed) >= 9 else [0, 0, 1920, 1080]
x0, y0, x1, y1 = box
columns = chosen or lights["order"]
folder = Path(lights["columns"])

tiles = [(c, np.asarray(Image.open(folder / f"{c}-000239.webp").convert("RGB"))[y0:y1, x0:x1]) for c in columns]
if "reference" in lights:
    neutral = regions.region_mask(
        view["regions"][view["neutral"]], np.load(view["geometry"]["normal"]), np.load(view["geometry"]["position"]))

    def trace(of):
        canvas = np.full((1080, 1920, 3), np.nan)
        for seed in of["reference"]:
            for path, x, y in seed:
                part = np.load(path)
                region = canvas[y:y + part.shape[0], x:x + part.shape[1]]
                canvas[y:y + part.shape[0], x:x + part.shape[1]] = np.where(np.isnan(region), part / 2, region + part / 2)
        return canvas

    # With "neutral_from", the match comes from that set's ten and path trace, as lit_ratios.py takes its neutral region.
    matched = view["sets"][lights["neutral_from"]] if "neutral_from" in lights else lights
    ten = tone.scene_from_display(
        np.asarray(Image.open(Path(matched["columns"]) / "ten-000239.webp").convert("RGB")), matched["stops"])
    canvas = trace(lights)
    match = np.log2(tone.luminance(ten)[neutral].mean() / tone.luminance(trace(matched))[neutral].mean())
    reference = canvas[y0:y1, x0:x1]
    if np.isnan(reference).any():
        raise SystemExit("the path trace's crops do not cover the box")
    tiles.append((f"path trace, {match:+.2f} stops", tone.display(reference, lights["stops"] + match)))
    print("path trace shown at", round(float(match), 3), "stops from the engine's")

w, h = round((x1 - x0) * scale), round((y1 - y0) * scale)
per_row = 2 if (x0, y0, x1, y1) == (0, 0, 1920, 1080) else len(tiles)
rows = (len(tiles) + per_row - 1) // per_row
gap = 4
sheet = Image.new("RGB", (per_row * w + (per_row - 1) * gap, rows * h + (rows - 1) * gap), "white")
for i, (name, pixels) in enumerate(tiles):
    tile = Image.fromarray(pixels).resize((w, h), Image.LANCZOS)
    draw = ImageDraw.Draw(tile)
    draw.rectangle((0, 0, 8 + 7 * len(name), 20), fill=(0, 0, 0))
    draw.text((4, 4), name, fill=(255, 210, 90))
    sheet.paste(tile, ((i % per_row) * (w + gap), (i // per_row) * (h + gap)))
sheet.save(out, optimize=True)
print(out, sheet.size[0], "x", sheet.size[1], out.stat().st_size, "bytes")
