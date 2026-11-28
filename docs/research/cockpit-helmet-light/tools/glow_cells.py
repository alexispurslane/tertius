"""Diagnostic tool code: the volume's cell faces whose glow differs between two dumps of one frame (such as #42 variant 4
against variant 0), with their level, cell center relative to the camera, side, area and glow, and the other occupied
faces of the same cell (reflectance and normal), to show whether a glow face shares its cell with other surfaces.
usage: glow_cells.py <dump with glow> <dump without>"""
import sys, os, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vol2 import Vol, LEVELS, SIDES, FIRST

SIDE = ["+x", "-x", "+y", "-y", "+z", "-z"]
a, b = Vol(sys.argv[1]), Vol(sys.argv[2])
for level in range(LEVELS):
    for local in range(SIDES[level] ** 3):
        cell = FIRST[level] + local
        hits = []
        for side in range(6):
            an, ca, sa = a.bucket(cell, side)
            _, _, sb = b.bucket(cell, side)
            if an[3] > 0 and abs(sa[0] - sb[0]) / an[3] > 0.05:
                hits.append((side, an, sa, sb))
        if not hits:
            continue
        c = a.coords(level, local)
        centre = a.centre(level, c)
        print(f"level {level} (cell {a.grids[level][3]:.2f} m) cell {c} center ({centre[0]:+.2f}, {centre[1]:+.2f}, {centre[2]:+.2f}) m from the camera")
        for side, an, sa, sb in hits:
            print(f"   glow face {SIDE[side]}: area {an[3] / 4096:.3f} faces, red radiance {sa[0] / an[3]:.3f} against {sb[0] / an[3]:.3f}")
        for side in range(6):
            an, ca, _ = a.bucket(cell, side)
            if an[3] > 0:
                n = [an[i] / an[3] for i in range(3)]
                print(f"   occupied {SIDE[side]}: area {an[3] / 4096:.3f}, reflectance ({ca[0] / an[3]:.2f}, {ca[1] / an[3]:.2f}, {ca[2] / an[3]:.2f}), normal ({n[0]:+.2f}, {n[1]:+.2f}, {n[2]:+.2f})")
