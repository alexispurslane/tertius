# Pipe runs and conduits, with brackets.
# In the build frame: nose toward +Y (see s00_scene.py).
import bpy
from mathutils import Vector

from tertius_blender.geometry import MB, frame_matrix

M = bpy.data.materials
pipes = MB()
brackets = MB()


def bracket_run(axis_pts, r, every, side_normal):
    """Clamp brackets along a straight run from axis_pts[0] to axis_pts[1]."""
    a, b = Vector(axis_pts[0]), Vector(axis_pts[1])
    length = (b - a).length
    d = (b - a).normalized()
    n = Vector(side_normal)
    k = int(length // every)
    for i in range(1, k):
        c = a + d * (i * every)
        Mb = frame_matrix(c - n * (r * 0.5), n, d)
        brackets.box_local(Mb, (0, 0, 0), (r * 2.8, 0.35, r * 2.2), bevel=0.03)


# Runs along the spine sides, between the sponsons' tops and the spine's top edge.
for s in (-1, 1):
    for z, r in ((5.9, 0.42), (6.95, 0.3), (7.9, 0.45)):
        x = s * (16.0 + r + 0.04)
        pipes.tube([(x, -54.5, z), (x, 53.5, z)], r, segs=14)
        bracket_run([(x, -54.5, z), (x, 53.5, z)], r, 4.0, (s, 0, 0))
    # Runs along the sponsons' outer faces; they bridge the gaps between sponson segments.
    for z, r in ((-4.6, 0.5), (0.9, 0.32), (3.3, 0.55)):
        x = s * (22.0 + r + 0.02)
        pipes.tube([(x, -51, z), (x, 45, z)], r, segs=14)
        bracket_run([(x, -51, z), (x, 45, z)], r, 4.0, (s, 0, 0))
    # Short loops that leave a sponson and enter the next one.
    for y in (-26, 0, 26):
        x = s * 21.6
        pipes.tube([(x, y - 2.5, -1.8), (s * 23.8, y - 2.5, -1.8), (s * 23.8, y + 2.5, -1.8),
                    (x, y + 2.5, -1.8)], 0.38, segs=12, bend=0.6)

# Conduits along the deck: from the engine block's front face, along the deck, over the deck's
# front edge, along the spine top, into the nose.
for x, r in ((-7.6, 0.6), (-6.2, 0.4), (-3.0, 0.5), (3.0, 0.5), (6.2, 0.4), (7.6, 0.6)):
    zt = 14.0 + r + 0.02
    zs = 9.0 + r + 0.02
    pts = [(x, -56.5, 16.4), (x, -53.5, 16.4), (x, -50.0, zt), (x, 45.0, zt), (x, 47.4, zt),
           (x, 49.0, zs), (x, 56.6, zs)]
    pipes.tube(pts, r, segs=14, bend=1.2)
    bracket_run([(x, -49, zt), (x, 44, zt)], r, 6.0, (0, 0, 1))

# Vertical bundles on the engine block's front face, which turn back over the block's top
# and run into the towers.
for s in (-1, 1):
    for i, r in enumerate((0.55, 0.4, 0.55, 0.35)):
        x = s * (20.5 + i * 1.4)
        y = -55.0 - (0.55 - r) * 0.5
        top = 18.0 + r + 0.15
        pts = [(x, y, -12.5), (x, y, top), (x, -59.0, top + 0.35), (x, -71.0, top + 0.9)]
        pipes.tube(pts, r, segs=12, bend=1.0)
        bracket_run([(x, y, -12.5), (x, y, top - 1)], r, 3.5, (0, 1, 0))

# Under the nose: a manifold of short pipes between the chin and the struts' bays.
def nose_bottom_z(y):
    """z of the nose block's bottom face at y (it rises from -11 at y=56 to -7 at y=98)."""
    return -11.0 + (y - 56.0) * 4.0 / 42.0


for s in (-1, 1):
    for i in range(3):
        x = s * (6.6 + i * 0.75)
        pts = [(x, 58.5, nose_bottom_z(58.5) - 0.34), (x, 93, nose_bottom_z(93) - 0.34)]
        pipes.tube(pts, 0.3, segs=10)
        bracket_run(pts, 0.3, 3.0, (0, 0.095, -0.995))

pipes.finish("Pipes", M["M_PipeMetal"], "Greebles", smooth_angle=60.0)
brackets.finish("Pipe_Brackets", M["M_FrameMetal"], "Greebles")
result = {"Pipes": len(bpy.data.objects["Pipes"].data.polygons),
          "Pipe_Brackets": len(bpy.data.objects["Pipe_Brackets"].data.polygons)}
