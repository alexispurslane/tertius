# Tool code of t0-9fo0, part B. regionlog.py: the reader of the per-anchor region log of scratch-region-log.patch, for
# regionspans.py and regionnoise.py. A log is two files with one name: <name>.txt, one line per draw ("<draw since the
# process start> <draw index since the last reset> <anchors> <anchors with 36 valid draws> <anchors that fired> <anchors
# per ring row>"), and <name>.f32, per draw and anchor, row by row, three little-endian floats: the region's light, the
# count of valid draws in a row (at most 36) and the moved answer (r, g and b of the anchors' ring). The first form of the
# patch (scratch-region-log-v1.patch) wrote only the first five columns of the .txt.
from pathlib import Path
import numpy as np

RING_DRAWS = 36


def load(path):
    """The log at <path> (without extension) as (index, light, tested, moved, wide): the draw index per draw, and per draw
    and anchor the region's light, whether the test ran (36 valid draws) and whether it fired, and the anchors per row."""
    path = Path(path)
    lines = np.array([[int(v) for v in l.split()] for l in path.with_suffix('.txt').read_text().splitlines()])
    anchors, wide = int(lines[0, 2]), int(lines[0, 5])
    data = np.fromfile(path.with_suffix('.f32'), dtype='<f4').reshape(len(lines), anchors, 3).astype(np.float64)
    tested = data[..., 1] >= RING_DRAWS
    return lines[:, 1], data[..., 0], tested, data[..., 2] != 0, wide
