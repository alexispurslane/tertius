"""Prints the free space in MB that the build of a model needs, from its model.json. build.sh stops below it.

The estimate, measured on the Nostromo (2026-10-26):
- Each baked map takes at most 4 bytes per pixel as a PNG file in textures/, and again inside model.glb. With three
  maps per bake object, that is 24 bytes per pixel and object. The Nostromo's twelve maps of 4096 x 4096 took 257 MB.
- While Cycles bakes one map, Blender's temporary folder holds at most 32 bytes per pixel of the map. A map of
  4096 x 4096 on Metal took 344 MB at the peak, and the bake deletes these files after each map.
- 1024 MB more for model.blend, the views, the logs and Blender itself.

So the Nostromo, with four bake objects and maps of 4096 x 4096, needs 3 GB. A model with no bake needs 1 GB.
"""

import json
import math
import sys

BASE_MB = 1024
MAP_FILES_BYTES_PER_PIXEL = 24  # Three maps per object, each in textures/ and in model.glb, at 4 bytes per pixel.
BAKE_SCRATCH_BYTES_PER_PIXEL = 32  # Blender's temporary files while it bakes one map.


def needed_mb(spec):
    """The free space in MB that the build of the model `spec` (the parsed model.json) needs."""
    bake = spec.get("bake", {})
    objects = len(bake.get("objects", []))
    if objects == 0:
        return BASE_MB
    pixels = bake.get("size", 1024) ** 2
    return BASE_MB + math.ceil(pixels * (BAKE_SCRATCH_BYTES_PER_PIXEL + MAP_FILES_BYTES_PER_PIXEL * objects) / 2**20)


def main(path):
    try:
        with open(path, encoding="utf-8") as fh:
            spec = json.load(fh)
        print(needed_mb(spec))
    except (OSError, ValueError, TypeError, AttributeError):
        print(BASE_MB)


if __name__ == "__main__":
    main(sys.argv[1])
