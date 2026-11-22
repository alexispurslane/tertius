#!/usr/bin/env python3
"""Throwaway (t1-3j2j, part 5): the sun's light in three boxes of rect-lamps, by source, from sun-sources.sh's traces.

  python3 -I sun_sources.py <traces folder> [<full-frame sun.npy> ...]

Each trace's crop starts at output pixel (1300, 300). For each variant and seed, it prints the mean and the median of the
linear luminance, in W/(m^2 sr), over each box, then for each variant with two seeds the mean of both and half their
difference. A full-frame array, such as the parent agent's sun.npy, gets the same boxes.
"""
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
import tone  # noqa: E402

BOXES = {"side wall": (1300, 300, 1500, 440), "jamb": (1760, 500, 1798, 580), "floor patch": (1700, 700, 1900, 880)}
folder = Path(sys.argv[1])


def stats(lum, x0c, y0c):
    out = {}
    for name, (x0, y0, x1, y1) in BOXES.items():
        part = lum[y0 - y0c:y1 - y0c, x0 - x0c:x1 - x0c]
        out[name] = (float(part.mean()), float(np.median(part)))
    return out


print("trace, " + ", ".join(f"{b} mean / median" for b in BOXES))
means = {}
for path in sorted(folder.glob("*-s*.npy")):
    s = stats(tone.luminance(np.load(path)[..., :3].astype(np.float64)), 1300, 300)
    variant = path.stem.rsplit("-s", 1)[0]
    means.setdefault(variant, []).append([s[b][0] for b in BOXES])
    print(path.stem, "  ".join(f"{m:.5f} / {d:.5f}" for m, d in s.values()))
for extra in sys.argv[2:]:
    s = stats(tone.luminance(np.load(extra)[..., :3].astype(np.float64)), 0, 0)
    print(extra, "  ".join(f"{m:.5f} / {d:.5f}" for m, d in s.values()))
print("variant, per box: mean of the seeds ± half their difference")
for variant, rows in means.items():
    rows = np.array(rows)
    if len(rows) == 2:
        print(variant, "  ".join(f"{(a + b) / 2:.5f} ± {abs(a - b) / 2:.5f}" for a, b in rows.T))
