"""Tool code (t1-r9vj): the pixels of a region, the one rule that lit_ratios.py, lit_sheet.py and region_map.py share.
Tests: test_regions.py.

A region is a box [x0, y0, x1, y1] in output pixels, x1 and y1 excluded. An optional "normal" keeps the pixels whose
geometric normal lies within 0.05 of it per axis. Optional "x", "y" or "z" ranges keep the pixels whose camera-relative
position lies inside them, in m. A region with such a filter loses the pixels on its edge, one pixel deep, so that the
jitter of the engine's frames does not mix two faces. As in t1-g4p1's ratios.py, a pixel on the image's own border is
not on such an edge.
"""
import numpy as np


def region_mask(spec, normal, position):
    """The region's pixels as a boolean image of the geometry images' size."""
    x0, y0, x1, y1 = spec["box"]
    mask = np.zeros(normal.shape[:2], bool)
    keep = np.ones((y1 - y0, x1 - x0), bool)
    filtered = "normal" in spec
    if filtered:
        keep &= np.all(np.abs(normal[y0:y1, x0:x1] - np.array(spec["normal"])) < 0.05, axis=-1)
    for axis, name in ((0, "x"), (1, "y"), (2, "z")):
        if name in spec:
            lo, hi = spec[name]
            keep &= (position[y0:y1, x0:x1, axis] > lo) & (position[y0:y1, x0:x1, axis] < hi)
            filtered = True
    mask[y0:y1, x0:x1] = keep
    if filtered:
        inner = mask.copy()
        inner[1:-1, 1:-1] = mask[1:-1, 1:-1] & mask[:-2, 1:-1] & mask[2:, 1:-1] & mask[1:-1, :-2] & mask[1:-1, 2:]
        mask = inner
    return mask
