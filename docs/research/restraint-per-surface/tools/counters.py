"""Research tool (issue #42): reads the research counters of the volume (VOLUME_COUNTERS 1 in volume.glsl) from the
readback volume.bin of one frame: the word source.w of the first four cell faces of the 0.25 m grid, and stats.x.
usage: counters.py <capture folder>..."""
import struct
import sys
from pathlib import Path

for folder in sys.argv[1:]:
    data = Path(folder, "volume.bin").read_bytes()
    evaluations = struct.unpack_from("<I", data, 48)[0]
    reached, raster, lamps, most = (struct.unpack_from("<I", data, 64 + 48 * k + 44)[0] for k in range(4))
    print(f"{folder}: capture fragments {raster} before the axis test, {reached} at the cell-face write; "
          f"{evaluations} lighting points, {lamps} visible lamps in all "
          f"({lamps / max(evaluations, 1):.2f} per point), at most {most} at one point")
