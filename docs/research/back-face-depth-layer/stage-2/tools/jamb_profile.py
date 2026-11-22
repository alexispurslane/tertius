#!/usr/bin/env python3
"""Throwaway (t1-g4p1): the profile across the jamb of rect-lamps in part 3, the mean linear luminance of rows 500-579 at
each output column x 1760-1815, in W/(m^2 sr).

  python3 -I jamb_profile.py <root> [<stops> <image> ...]

With no image, it profiles the full-light columns of <root>/full/rect-lamps/pack (frame 239, and frame 231 of ten), at
the fixed exposure of 2.7868438 stops, and the reference <root>/ref/rect-window-s1.npy and -s2.npy, whose crop starts at
x 1700, y 440. With pairs of <stops> <image>, it profiles those 8-bit engine frames instead, such as the walk-cap runs of
<root>/top/<cap>/s-000239.png. <root> is /private/tmp/t1-g4p1 in this record.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
import tone  # noqa: E402

X0, X1, Y0, Y1 = 1760, 1816, 500, 580
root = Path(sys.argv[1])


def engine(path, stops):
    linear = tone.scene_from_display(np.asarray(Image.open(path).convert("RGB")), stops)
    return tone.luminance(linear[Y0:Y1, X0:X1]).mean(0)


profiles = {}
if len(sys.argv) > 2:
    pairs = sys.argv[2:]
    for stops, path in zip(pairs[::2], pairs[1::2]):
        profiles[Path(path).parent.name + "/" + Path(path).stem] = engine(path, float(stops))
else:
    pack = root / "full/rect-lamps/pack"
    for column in ["ten", "layer", "layerfade", "dfront", "dmiss05", "dblack05"]:
        profiles[column] = engine(pack / f"{column}-000239.webp", 2.7868438)
    profiles["ten 231"] = engine(pack / "ten-000231.webp", 2.7868438)
    reference = (np.load(root / "ref/rect-window-s1.npy") + np.load(root / "ref/rect-window-s2.npy")) / 2
    profiles["reference"] = tone.luminance(reference[Y0 - 440:Y1 - 440, X0 - 1700:X1 - 1700]).mean(0)

print("x    " + " ".join(f"{k[-12:]:>12}" for k in profiles))
for i in range(X1 - X0):
    print(f"{X0 + i} " + " ".join(f"{profiles[k][i]:12.4f}" for k in profiles))
