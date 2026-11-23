#!/usr/bin/env python3
"""Tool code (t1-r9vj): the map of the regions over the dimmed layer still, at half size, with a legend.

  python3 -I region_map.py <views.json> <view> <set> <out.png>

The masks are those of regions.py, which lit_ratios.py uses too.
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).parent))
import regions  # noqa: E402

views = json.loads(Path(sys.argv[1]).read_text())
view = views[sys.argv[2]]
lights = view["sets"][sys.argv[3]]
normal = np.load(view["geometry"]["normal"])
position = np.load(view["geometry"]["position"])
COLORS = [(230, 60, 60), (60, 200, 60), (70, 110, 255), (240, 220, 40), (230, 60, 230), (40, 220, 220), (255, 140, 30),
          (160, 100, 255), (255, 255, 255), (120, 255, 160)]


still = np.asarray(Image.open(Path(lights["columns"]) / "layer-000239.webp").convert("RGB")).astype(float) * 0.4
for (name, spec), color in zip(view["regions"].items(), COLORS):
    m = regions.region_mask(spec, normal, position)
    still[m] = still[m] * 0.4 + np.array(color) * 0.6
image = Image.fromarray(still.astype(np.uint8)).resize((960, 540), Image.LANCZOS)
draw = ImageDraw.Draw(image)
draw.rectangle((4, 4, 200, 12 + 16 * len(view["regions"])), fill=(0, 0, 0))
for i, ((name, _), color) in enumerate(zip(view["regions"].items(), COLORS)):
    draw.rectangle((8, 8 + 16 * i, 20, 20 + 16 * i), fill=color)
    draw.text((26, 8 + 16 * i), name + (" (neutral)" if name == view["neutral"] else ""), fill=color)
image.save(sys.argv[4], optimize=True)
print(sys.argv[4])
