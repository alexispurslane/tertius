#!/usr/bin/env python3
"""Throwaway (t1-3j2j): part 2's table, the jamb's strip under each switch of the probe.

  python3 -I switch_table.py <root> <name> ...

<root> is /private/tmp/t1-3j2j. For each <name>, the still <root>/stills/rect-lamps/<name>/s-000239.png (full is the
default settings) minus the still without the sun <root>/stills/rect-lamps/nosun/s-000239.png gives the engine's direct
sunlight on the jamb of rect-lamps: the mean of rows 500-579 at each output column x 1795-1810, at 2.7868438 stops. The
strip's width is the count of columns above 0.01 W/(m^2 sr), and its onset the first such column. Beside it, the CPU copy
of the filter (engine_shadow.py's out npz <root>/cast/eng-<name>-jamb-line.npz, made from the dump <root>/dump/<name>)
gives the world x where the engine's lit share first passes 0 on the jamb's mid line, and its share at x = 10.0 m.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
import tone  # noqa: E402

root = Path(sys.argv[1])
STOPS = 2.7868438
X0, X1 = 1795, 1811


def lum(path):
    return tone.luminance(tone.scene_from_display(np.asarray(Image.open(path).convert("RGB")), STOPS))


nosun = lum(root / "stills/rect-lamps/nosun/s-000239.png")
cast = np.load(root / "cast/rl-jamb-line--0.006.npz")
print("name, onset column, columns above 0.01, mean of x 1800-1809, peak, profile x 1795-1810,"
      " | CPU: world x of the first lit point, lit share at x 10.0")
for name in sys.argv[2:]:
    sun = lum(root / f"stills/rect-lamps/{name}/s-000239.png") - nosun
    profile = sun[500:580, X0:X1].mean(0)
    above = np.nonzero(profile > 0.01)[0]
    onset = X0 + int(above[0]) if above.size else None
    line = f"{name} {onset} {above.size} {profile[5:15].mean():.3f} {profile.max():.3f} {np.round(profile, 3).tolist()}"
    engine = root / f"cast/eng-{name}-jamb-line.npz"
    if engine.exists():
        e = np.load(engine)
        lit = np.nonzero(e["lit"] > 0)[0]
        first = f"{cast['position'][lit[0]][0]:.3f}" if lit.size else "none"
        line += f" | {first} {e['lit'][-1]:.3f}"
    print(line)
