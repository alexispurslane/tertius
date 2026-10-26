# Antenna masts, sensor dishes and arrays, the ventral docking clamps and the folded landing struts.
# In the build frame: nose toward +Y (see s00_scene.py).
import math

import bpy
from mathutils import Matrix, Vector

from tertius_blender.geometry import MB, align_z, beam, beam_matrix, coll, rod

M = bpy.data.materials
metal = MB()     # dark frame metal
paint = MB()     # light paint
gray = MB()      # gray paint


# --- Antenna masts -----------------------------------------------------------
def pole_mast(x, y, z0, height):
    metal.cyl((x, y, z0 + 0.6), (0, 0, 1), 1.0, 1.2, segs=16)
    metal.cyl((x, y, z0 + height / 2), (0, 0, 1), 0.38, height, segs=12, radius2=0.16)
    for f, w in ((0.35, 4.5), (0.6, 3.2), (0.82, 2.0)):
        zc = z0 + height * f
        metal.box((x, y, zc), (w, 0.18, 0.18), bevel=0.0)
        for s in (-1, 1):
            metal.cyl((x + s * w / 2, y, zc - 0.5), (0, 0, 1), 0.06, 1.0, segs=6)
    metal.cyl((x, y, z0 + height + 0.6), (0, 0, 1), 0.04, 1.2, segs=6)


def lattice_mast(x, y, z0, height, r=0.9, bays=8):
    """Three-legged lattice mast with rings and diagonals in every bay."""
    legs = [Vector((x + r * math.cos(a), y + r * math.sin(a), 0)) for a in (0.0, 2.094, 4.189)]
    metal.cyl((x, y, z0 + 0.4), (0, 0, 1), r * 1.6, 0.8, segs=16)
    for p in legs:
        rod(metal, (p.x, p.y, z0), (p.x, p.y, z0 + height), 0.12, segs=6)
    for b in range(bays + 1):
        z = z0 + height * b / bays
        for i in range(3):
            a, c = legs[i], legs[(i + 1) % 3]
            rod(metal, (a.x, a.y, z), (c.x, c.y, z), 0.06, segs=5)
            if b < bays:
                rod(metal, (a.x, a.y, z), (c.x, c.y, z + height / bays), 0.05, segs=5)
    # Cap plate over the legs' tops, which carries the whip antenna.
    metal.cyl((x, y, z0 + height), (0, 0, 1), r * 1.25, 0.3, segs=16)
    metal.cyl((x, y, z0 + height + 1.5), (0, 0, 1), 0.08, 3.0, segs=6)
    # A small dish near the top, facing outward, on an arm that leaves a bay ring.
    out = Vector((x, 0, 0)).normalized() if abs(x) > 1e-3 else Vector((1, 0, 0))
    zd = z0 + height * round(0.8 * bays) / bays
    centre = Vector((x + out.x * 1.4, y, zd))
    rod(metal, (x + out.x * 0.3, y, zd), centre, 0.1, segs=6)
    dish(centre, out + Vector((0, 0, 0.3)), 1.3, mb=gray)


def dish(center, facing, radius, mb=None, depth_ratio=0.22):
    mb = mb or paint
    facing = Vector(facing).normalized()
    Mw = Matrix.Translation(center) @ align_z(facing)
    prof = []
    n = 8
    for i in range(n + 1):
        r = radius * i / n
        prof.append((max(r, 0.001), depth_ratio * radius * (r / radius) ** 2))
    t = radius * 0.03
    back = [(r, h - t) for r, h in reversed(prof)]
    mb.lathe(prof + back, Mw, segs=32)
    # Feed horn on three struts.
    f = Matrix.Translation(center) @ align_z(facing)
    focus = f @ Vector((0, 0, radius * 0.75))
    for k in range(3):
        a = 2 * math.pi * k / 3
        rim = f @ Vector((radius * 0.85 * math.cos(a), radius * 0.85 * math.sin(a), depth_ratio * radius * 0.72))
        rod(metal, rim, focus, 0.05 * radius / 3 + 0.03, segs=5)
    metal.cyl(focus, facing, radius * 0.08, radius * 0.18, segs=10)


pole_mast(-4.0, 72.0, 25.0, 16.0)
pole_mast(4.0, 76.0, 25.0, 11.0)
lattice_mast(-19.0, -86.0, 39.2, 14.0)
lattice_mast(19.0, -86.0, 39.2, 10.0, bays=6)

# Main dish on the deck, on a pedestal and yoke, tilted up and forward-left.
metal.cyl((0, 24, 15.2), (0, 0, 1), 1.4, 2.4, segs=20)
metal.box((0, 24, 16.8), (3.2, 1.2, 0.9), bevel=0.05)
for s in (-1, 1):
    metal.box((s * 1.4, 24, 17.8), (0.4, 1.0, 2.2), bevel=0.03)
dish((0, 24, 18.6), (-0.45, 0.55, 0.7), 5.5)
# Smaller dish on the engine block's dorsal block.
# The block's top slopes down toward the front: z = 25.6 at y = -80. The pedestal sinks into it,
# and a stem rises from the pedestal to the dish's back.
metal.cyl((0, -80, 26.6), (0, 0, 1), 0.9, 2.6, segs=16)
rod(metal, (0, -80, 27.5), (0, -80, 29.2), 0.35, segs=10)
dish((0, -80, 29.2), (0.3, -0.4, 0.85), 3.2, mb=gray)

# --- Sensor array on the head's front face (y = 110) ----------------------------
paint.box((0, 110.4, -0.6), (10.5, 0.8, 4.6), bevel=0.1)
for i in range(4):
    for j in range(2):
        x = -3.6 + i * 2.4
        z = -2.0 + j * 2.4
        metal.cyl((x, 111.4, z), (0, 1, 0), 0.75, 1.4, segs=16)
        gray.cyl((x, 112.2, z), (0, 1, 0), 0.5, 0.3, segs=16)
for s in (-1, 1):
    rod(metal, (s * 4.8, 110.8, 1.4), (s * 5.4, 122.0, 2.4), 0.09, segs=6)
    metal.sphere((s * 5.4, 122.0, 2.4), 0.2, segs=8, rings=4)
# Side sensor blisters on the nose.
for s in (-1, 1):
    for y in (70, 82):
        xs = s * (19 - (y - 56) * 6 / 42)
        gray.cyl((xs + s * 0.5, y, 1.0), (s, 0, 0), 1.3, 1.2, segs=20)
        metal.cyl((xs + s * 1.2, y, 1.0), (s, 0, 0), 0.8, 0.5, segs=20)

# --- Docking clamps under the keel (bottom at z = -14) ----------------------------
# Every part overlaps the part it hangs from: rails and ventral beam sink into the keel, the
# mounts into the beam, and the jaws are folded up tight under the mounts.
for s in (-1, 1):
    metal.box((s * 5.6, 0, -14.5), (1.4, 98, 1.2), bevel=0.1)
    # Rail ties.
    for k in range(17):
        metal.box((s * 5.6, -48 + 6 * k, -14.2), (2.6, 0.6, 0.6), bevel=0.0)
# Ventral beam under the keel that carries the clamps.
paint.box((0, -1, -15.3), (7.0, 92, 3.0), bevel=0.25)
for y in (-38, -14, 10, 34):
    paint.box((0, y, -15.8), (12.5, 6.4, 2.6), bevel=0.2)
    metal.box((0, y, -17.2), (8, 4.6, 1.0), bevel=0.1)
    for s in (-1, 1):
        shoulder = Vector((s * 4.6, y, -17.0))
        elbow = Vector((s * 7.6, y, -19.4))
        tip = Vector((s * 3.6, y, -20.6))
        metal.cyl(shoulder, (0, 1, 0), 1.1, 5.0, segs=16)
        beam(paint, shoulder, elbow, 4.0, 1.8, up=(s, 0, 0.3), bevel=0.15)
        metal.cyl(elbow, (0, 1, 0), 1.0, 4.8, segs=16)
        beam(gray, elbow, tip, 3.6, 1.6, up=(s * 0.3, 0, -1), bevel=0.15)
        # Grip pad at the jaw tip.
        metal.box((tip.x - s * 0.4, y, tip.z), (1.4, 4.2, 1.8), bevel=0.1)
        # Hydraulic ram from the base to the arm's middle.
        base = Vector((s * 2.2, y, -17.4))
        mid = shoulder.lerp(elbow, 0.55) + Vector((-s * 0.8, 0, 0))
        for off in (-1.3, 1.3):
            rod(metal, base + Vector((0, off, 0)), base.lerp(mid, 0.55) + Vector((0, off, 0)), 0.34, segs=10)
            rod(gray, base.lerp(mid, 0.5) + Vector((0, off, 0)), mid + Vector((0, off, 0)), 0.18, segs=8)


# --- Landing struts, folded flat against the hull -----------------------------------
def strut(x, y0, y1, zb, down):
    """A strut folded along +Y. zb(y) = z of the hull bottom at y; `down` = outward normal.
    Distances below are along `down`, from the hull surface: the bay reaches 0.1 out of the hull,
    and the leg, hinge and foot pad all reach into the bay, so nothing hangs free."""
    n = Vector(down).normalized()
    hull0 = Vector((x, y0, zb(y0)))
    hull1 = Vector((x, y1, zb(y1)))
    d = (hull1 - hull0).normalized()
    sx = 1.0 if x > 0 else -1.0
    # Bay: a dark frame sunk into the hull, 0.1 proud of it.
    Mbay, length = beam_matrix(hull0 - d * 2.5, hull1 + d * 3.5, up=-n)
    metal.box_local(Mbay, (0, 0, 0.15), (5.6, length, 0.5), bevel=0.05)
    p0 = hull0 + n * 0.8     # the leg (1.5 thick) spans 0.05 .. 1.55 out
    p1 = hull1 + n * 0.8
    # Hinge at the rear end.
    metal.cyl(p0 - d * 0.5, (1, 0, 0), 1.1, 3.6, segs=18)
    gray.cyl(p0 - d * 0.5, (1, 0, 0), 0.6, 4.2, segs=12)
    # Main leg and the shock strut beside it (the strut overlaps the leg's side by 0.1).
    beam(paint, p0, p1, 2.0, 1.5, up=n, bevel=0.12)
    rod(metal, p0 + Vector((sx * 1.3, 0, 0)) + d * 2, p1 + Vector((sx * 1.3, 0, 0)) - d * 4, 0.4, segs=12)
    # Drag brace folded alongside, on the other side.
    side = Vector((-sx * 1.2, 0, 0))
    beam(metal, p0 + side + d * 3, p0.lerp(p1, 0.7) + side, 0.6, 0.6, up=n, bevel=0.03)
    # Foot pad lying flat against the bay at the front end (0.05 .. 0.75 out), and its cap.
    metal.cyl(hull1 + d * 1.0 + n * 0.4, n, 2.5, 0.7, segs=24)
    gray.cyl(hull1 + d * 1.0 + n * 0.88, n, 2.0, 0.3, segs=24)


def nose_bottom_z(y):
    return -11.0 + (y - 56.0) * 4.0 / 42.0


def aft_bottom_z(y):
    return -22.0 + (y + 95.0) * 2.0 / 33.0


for s in (-1, 1):
    strut(s * 11.0, 63.0, 86.0, nose_bottom_z, (0, 0.095, -0.995))
    strut(s * 13.0, -90.0, -70.0, aft_bottom_z, (0, 0.06, -0.998))

metal.finish("Structures_Metal", M["M_FrameMetal"], "Structures")
paint.finish("Structures_Light", M["M_PaintLight"], "Structures")
gray.finish("Structures_Gray", M["M_PaintGray"], "Structures")
result = {o.name: len(o.data.polygons) for o in coll("Structures").objects}
