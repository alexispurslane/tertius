#!/usr/bin/env python3
"""Tool code (t1-8b4w): the room's brightness note of the ticket. The mean linear luminance, in W/(m^2 sr), of three
boxes of rect-lamps' frame 239 at 1920 x 1080, from the 8-bit still through the inverse of the engine's exposure, tone
curve and sRGB encoding (stage-2/tools/tone.py, bloom off).

  python3 -I brightness.py <stills folder of rect-lamps> <stops>
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "stage-2" / "tools"))
import tone  # noqa: E402

BOXES = {"side wall": (1300, 300, 1500, 440), "jamb": (1760, 500, 1798, 580), "floor patch": (1700, 700, 1900, 880)}

folder, stops = Path(sys.argv[1]), float(sys.argv[2])
for side in ("main", "branch"):
    rgb8 = np.asarray(Image.open(folder / "out" / "pack" / f"{side}-000239.webp").convert("RGB"))
    linear = tone.luminance(tone.scene_from_display(rgb8, stops))
    cells = ", ".join(f"{name} {linear[y0:y1, x0:x1].mean():.4f}" for name, (x0, y0, x1, y1) in BOXES.items())
    print(f"{side}: {cells}")
