#!/usr/bin/env python3
"""Throwaway (t1-kiaw): the pixels where two dumps of the back share differ, from controls.sh.

    python3 -I share_diff.py <dump a> <dump b>

Prints the surface pixels, the pixels whose share differs, how many gain or lose a back face (share 0 against not 0),
and the largest relative difference of the back distance among the rest. The share is front / back, so the back
distance's relative difference is a / b - 1 of the shares.
"""

import sys
from pathlib import Path

import numpy as np

a_dir, b_dir = Path(sys.argv[1]), Path(sys.argv[2])
name = next(a_dir.glob("back-0-*.f32")).name
a = np.fromfile(a_dir / name, dtype=np.float32)
b = np.fromfile(b_dir / name, dtype=np.float32)
front = np.fromfile(next(a_dir.glob("front-0-*.f32")), dtype=np.float32)
surface = front > 0
diff = a != b
print(f"{name}: surface pixels {surface.sum()}, share differs {diff.sum()}")
gain = diff & (a == 0) & (b != 0)
lose = diff & (a != 0) & (b == 0)
print(f"  gain a back face {gain.sum()}, lose one {lose.sum()}")
both = diff & (a != 0) & (b != 0)
if both.any():
    rel = np.abs(a[both] / b[both] - 1)
    print(f"  both have one: {both.sum()}, relative difference of the back distance: median {np.median(rel):.2e}, largest {rel.max():.2e}")
