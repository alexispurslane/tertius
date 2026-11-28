"""Research tool (issue #42): sums the outgoing light that each grid of the volume holds after the frame (area times
outgoing radiance, float words of every cell face, in units of one cell face), from the readback volume.bin. Grid 0 is
the 0.25 m grid, 1 the 0.5 m grid and 2 the 1 m grid. Each grid's coarser parents also hold the light of their children.
usage: volume-totals.py <capture folder>..."""
import sys
from pathlib import Path

import numpy as np

SIDE = [16, 16, 18]
for folder in sys.argv[1:]:
    data = np.fromfile(Path(folder) / "volume.bin", dtype=np.uint8)
    cells = sum(n**3 for n in SIDE)
    buckets = data[64:64 + cells * 6 * 48].view(np.uint32).reshape(cells * 6, 12)
    source = buckets[:, 8:11].view(np.float32).astype(np.float64)
    start = 0
    parts = []
    for level, n in enumerate(SIDE):
        part = source[start * 6:(start + n**3) * 6].sum(0) / 4096
        parts.append(f"grid {level}: ({part[0]:.2f}, {part[1]:.2f}, {part[2]:.2f})")
        start += n**3
    print(f"{folder}: " + ", ".join(parts))
