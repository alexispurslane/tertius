# Lit windows and navigation lights (emissive materials).
# In the build frame: nose toward +Y (see s00_scene.py).
import random

import bpy
from mathutils import Matrix, Vector

from tertius_blender.geometry import MB, coll, frame_matrix

M = bpy.data.materials
win = MB()
frame = MB()
paint = MB()
red, green, white = MB(), MB(), MB()
# The seed of the earlier project, so the same portholes are lit.
rng = random.Random(model.params["seeds"]["windows"])

# --- Bridge brow: a band of panes on the slanted front face ---------------------------
# The face runs from (y=92, z=9.5) at the bottom to (y=86, z=22) at the top.
u = Vector((0, -6.0, 12.5)).normalized()        # up the face
n = Vector((0, 12.5, 6.0)).normalized()          # outward
base = Vector((0, 92.0, 9.5))
length = Vector((0, -6.0, 12.5)).length
Mface = Matrix((
    (1, u.x, n.x, 0), (0, u.y, n.y, 0), (0, u.z, n.z, 0), (0, 0, 0, 1)))


def on_brow(t, x, off):
    c = base + u * (t * length) + n * off
    return Matrix.Translation(c) @ Mface @ Matrix.Translation((x, 0, 0))


frame.box_local(on_brow(0.635, 0, 0.1), (0, 0, 0), (19.8, 3.0, 0.3), bevel=0.05)
for i in range(9):
    x = -8.4 + i * 2.1
    win.box_local(on_brow(0.635, x, 0.28), (0, 0, 0), (1.75, 2.3, 0.1))
# Visor above the band.
paint.box_local(on_brow(0.76, 0, 0.6), (0, 0, 0), (21.0, 0.5, 1.3), bevel=0.08)
# A lower row of small panes.
for i in range(12):
    x = -8.25 + i * 1.5
    win.box_local(on_brow(0.38, x, 0.06), (0, 0, 0), (0.9, 0.7, 0.1))

# --- Bridge sides -----------------------------------------------------------------
for s in (-1, 1):
    nside = Vector((s * 12.5, 0, 2.0)).normalized()
    for i in range(9):
        y = 64.5 + i * 2.4
        c = Vector((s * 10.76, y, 17.25)) + nside * 0.06
        Mf = frame_matrix(c, nside, (0, 0, 1))
        frame.box_local(Mf, (0, 0, 0.0), (1.7, 1.5, 0.12))
        win.box_local(Mf, (0, 0, 0.08), (1.3, 1.1, 0.1))

# --- Portholes on the sponsons' outer faces and on the nose sides ------------------------
for s in (-1, 1):
    for y0, y1 in ((-50, -28), (-24, -2), (2, 24), (28, 44)):
        y = y0 + 1.6
        while y < y1 - 1.5:
            xs = s * (22.0 - 0.5 * (y - y0) / (y1 - y0))
            nn = Vector((s, 0, 0))
            Mf = frame_matrix(Vector((xs, y, -2.0)) + nn * 0.02, nn, (0, 0, 1))
            frame.box_local(Mf, (0, 0, 0), (1.0, 0.75, 0.12))
            if rng.random() < 0.65:
                win.box_local(Mf, (0, 0, 0.06), (0.75, 0.5, 0.1))
            y += 2.6
    for y in range(60, 94, 3):
        xs = s * (19 - (y - 56) * 6 / 42)
        nn = Vector((s * 42, 6, 0)).normalized()
        Mf = frame_matrix(Vector((xs, y, -3.0)) + nn * 0.02, nn, (0, 0, 1))
        frame.box_local(Mf, (0, 0, 0), (1.1, 0.8, 0.12))
        if rng.random() < 0.7:
            win.box_local(Mf, (0, 0, 0.06), (0.85, 0.55, 0.1))
    # Tower windows near the top of each tower's outer face.
    for y in range(-92, -72, 3):
        xs = s * (25.0 - (y + 96) / 26)
        nn = Vector((s, 0, 0))
        Mf = frame_matrix(Vector((xs, y, 31.5)) + nn * 0.02, nn, (0, 0, 1))
        frame.box_local(Mf, (0, 0, 0), (1.3, 0.9, 0.12))
        win.box_local(Mf, (0, 0, 0.06), (1.0, 0.6, 0.1))

# --- Navigation lights ---------------------------------------------------------------
for p in ((-35.4, -79, 2.0), (-13.6, 96.5, 0.0), (-22.6, 10, 5.4)):
    red.sphere(p, 0.45, segs=12, rings=6)
for p in ((35.4, -79, 2.0), (13.6, 96.5, 0.0), (22.6, 10, 5.4)):
    green.sphere(p, 0.45, segs=12, rings=6)
for p in ((-4.0, 72.0, 42.4), (4.0, 76.0, 36.9), (-19.0, -86.0, 56.4), (19.0, -86.0, 52.4),
          (-30.5, -103.2, 21.2), (30.5, -103.2, 21.2), (0, 111.0, 2.6)):
    white.sphere(p, 0.4, segs=12, rings=6)

win.finish("Windows", M["M_Window"], "Lights")
frame.finish("Window_Frames", M["M_FrameMetal"], "Greebles")
paint.finish("Bridge_Visor", M["M_PaintLight"], "Structures")
red.finish("Nav_Red", M["M_NavRed"], "Lights")
green.finish("Nav_Green", M["M_NavGreen"], "Lights")
white.finish("Nav_White", M["M_NavWhite"], "Lights")
result = {o.name: len(o.data.polygons) for o in coll("Lights").objects}
