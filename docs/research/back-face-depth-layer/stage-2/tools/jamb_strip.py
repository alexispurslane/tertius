#!/usr/bin/env python3
"""Throwaway (t1-g4p1): rect-lamps-jamb-strip.png, the jamb of part 3 at three times its size, x 1756-1815, y 470-609.

  python3 -I jamb_strip.py <root> <out.png>

The six panels, left to right, with the inputs under <root> (/private/tmp/t1-g4p1 in this record):

1. layer under full lights: full/rect-lamps/pack/layer-000239.webp, as written at 2.7868438 stops.
2. layer with --screen-trace off: top/traceoff/s-000239.png, as written at 2.7868438 stops.
3. layer with the walk capped at level 2 (TERTIUS_WALK_TOP=2): top/2/s-000239.png, as written at 2.7868438 stops.
4. layer without the sun (TERTIUS_NO_SUN=1): nosun/rect-lamps/pack/layer-000239.webp, as written at 3.849491 stops.
5. The reference under full lights, the mean of ref/rect-window-s1.npy and -s2.npy, shown at 2.7868438 - 5.869 stops.
6. The reference with the sun and a black Earth (REF_EARTH_BLACK=1, REF_SKIP_LIGHTS unset), the sample-weighted mean of
   ref/sunonly-rect-window.npy (512 samples) and ref/sunonly2-rect-window.npy (2,048 samples), shown at 2.7868438 stops.

The reference crops start at x 1700, y 440. The engine's panels are its own 8-bit frames, with no change.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
import tone  # noqa: E402

X0, Y0, X1, Y1 = 1756, 470, 1816, 610
SCALE, GAP = 3, 6
root = Path(sys.argv[1])


def engine(path):
    return np.asarray(Image.open(root / path).convert("RGB"))[Y0:Y1, X0:X1]


def reference(linear, stops):
    return tone.display(linear[Y0 - 440:Y1 - 440, X0 - 1700:X1 - 1700], stops)


full = (np.load(root / "ref/rect-window-s1.npy") + np.load(root / "ref/rect-window-s2.npy")) / 2
sun_only = (np.load(root / "ref/sunonly-rect-window.npy") * 512 + np.load(root / "ref/sunonly2-rect-window.npy") * 2048) / 2560
panels = [
    engine("full/rect-lamps/pack/layer-000239.webp"),
    engine("top/traceoff/s-000239.png"),
    engine("top/2/s-000239.png"),
    engine("nosun/rect-lamps/pack/layer-000239.webp"),
    reference(full, 2.7868438 - 5.869),
    reference(sun_only, 2.7868438),
]
h, w = panels[0].shape[:2]
canvas = np.full((h * SCALE, len(panels) * (w * SCALE + GAP) - GAP, 3), 255, np.uint8)
for i, panel in enumerate(panels):
    x = i * (w * SCALE + GAP)
    canvas[:, x:x + w * SCALE] = np.repeat(np.repeat(panel, SCALE, 0), SCALE, 1)
Image.fromarray(canvas).save(sys.argv[2], optimize=True)
