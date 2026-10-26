# Greebles scattered over the hull surfaces, plus the frame ribs.
# A sample point on a hull face is kept only when:
#   - nothing of the hull sits within `clear` meters above it along the face normal, and
#   - the greeble's footprint corners all land on hull faces facing the same way.
# The greeble's local frame has Z along the face normal and Y as close to ship-forward as it can.
# In the build frame: nose toward +Y (see s00_scene.py).
import bisect
import math
import random

import bpy
from mathutils import Matrix, Vector

from tertius_blender.geometry import MB, coll, frame_matrix

M = bpy.data.materials
# The seed of the earlier project. The places also follow the order of the hull faces, which MB.finish fixes, so
# they differ from those of the earlier project. The rules and the density are the same.
rng = random.Random(model.params["seeds"]["greebles"])
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
scene = bpy.context.scene

light = MB()
gray = MB()
metal = MB()


def pick_mb():
    r = rng.random()
    return light if r < 0.6 else (gray if r < 0.85 else metal)


def clear_above(p, n, dist):
    hit, *_ = scene.ray_cast(dg, p + n * 0.03, n, distance=dist)
    return not hit


def footprint_ok(Mf, n, hw, hd):
    for cx, cy in ((-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)):
        c = Mf @ Vector((cx, cy, 0.6))
        hit, loc, nrm, *_ = scene.ray_cast(dg, c, -n, distance=1.4)
        if not hit or nrm.dot(n) < 0.97:
            return False
    return True


SINK = 0.15  # how far each greeble sinks into the hull, so that slopes leave no gaps


def g_box(mb, Mf, w, d, h, bevel=0.05):
    mb.box_local(Mf, (0, 0, (h - SINK) / 2), (w, d, h + SINK), bevel=bevel)


def g_stack(mb, Mf, w, d, h):
    g_box(mb, Mf, w, d, h)
    w2, d2 = w * rng.uniform(0.35, 0.7), d * rng.uniform(0.35, 0.7)
    ox = rng.uniform(-(w - w2) / 2, (w - w2) / 2)
    oy = rng.uniform(-(d - d2) / 2, (d - d2) / 2)
    h2 = rng.uniform(0.3, 1.2)
    mb.box_local(Mf, (ox, oy, h + h2 / 2), (w2, d2, h2), bevel=0.04)
    if rng.random() < 0.5:
        r = min(w2, d2) * 0.3
        mb.cyl_local(Mf, (ox, oy, h + h2 + 0.4), (0, 0, 1), r, 0.8, segs=12)


def g_tank(mb, Mf, r, length):
    for s in (-0.3, 0.3):
        metal.box_local(Mf, (0, s * length, r * 0.45), (r * 1.6, 0.4, r * 0.9 + SINK), bevel=0.03)
    mb.cyl_local(Mf, (0, 0, r * 1.05), (0, 1, 0), r, length, segs=20)
    for s in (-0.5, 0.5):
        metal.cyl_local(Mf, (0, s * length, r * 1.05), (0, 1, 0), r * 0.75, 0.3, segs=20)
    for s in (-0.15, 0.15):
        metal.cyl_local(Mf, (0, s * length, r * 1.05), (0, 1, 0), r * 1.04, 0.18, segs=20)


def g_vent(Mf, w, d, slats, vertical=False):
    metal.box_local(Mf, (0, 0, 0.1), (w, d, 0.2 + SINK * 2), bevel=0.03)
    light.box_local(Mf, (0, -d / 2 + 0.08, 0.35), (w, 0.16, 0.5), bevel=0.02)
    light.box_local(Mf, (0, d / 2 - 0.08, 0.35), (w, 0.16, 0.5), bevel=0.02)
    for i in range(slats):
        y = -d / 2 + 0.3 + (d - 0.6) * i / max(1, slats - 1)
        Mr = Mf @ Matrix.Translation((0, y, 0.38)) @ Matrix.Rotation(math.radians(30), 4, "X")
        gray.box_local(Mr, (0, 0, 0), (w - 0.25, 0.08, 0.4), bevel=0.0)


def g_hatch_round(mb, Mf, r):
    mb.cyl_local(Mf, (0, 0, 0.06), (0, 0, 1), r, 0.12 + SINK * 2, segs=24)
    mb.cyl_local(Mf, (0, 0, 0.2), (0, 0, 1), r * 0.82, 0.16, segs=24)
    metal.box_local(Mf, (0, 0, 0.33), (r * 1.1, 0.14, 0.12), bevel=0.0)
    metal.box_local(Mf, (r * 0.75, 0, 0.18), (0.4, 0.5, 0.25), bevel=0.0)


def g_stackpipe(Mf, r, h):
    metal.cyl_local(Mf, (0, 0, (h - SINK) / 2), (0, 0, 1), r, h + SINK, segs=14)
    metal.cyl_local(Mf, (0, 0, h), (0, 0, 1), r * 1.3, 0.25, segs=14)


def g_panel(mb, Mf, w, d, t):
    mb.box_local(Mf, (0, 0, (t - SINK) / 2), (w, d, t + SINK), bevel=0.03)
    if rng.random() < 0.4:
        # Raised inner plate.
        mb.box_local(Mf, (0, 0, t + 0.04), (w * 0.8, d * 0.8, 0.08), bevel=0.0)


def g_door(Mf, w, d):
    metal.box_local(Mf, (0, 0, 0.05), (w + 0.4, d + 0.4, 0.1 + SINK * 2), bevel=0.02)
    light.box_local(Mf, (0, 0, 0.18), (w, d, 0.16), bevel=0.03)
    gray.box_local(Mf, (w * 0.3, 0, 0.3), (0.3, 0.6, 0.12), bevel=0.0)
    for s in (-1, 1):
        metal.box_local(Mf, (0, s * (d / 2 + 0.45), 0.1), (w + 0.6, 0.3, 0.2), bevel=0.0)


def g_jbox(Mf, w, d, h):
    mb = pick_mb()
    g_box(mb, Mf, w, d, h, bevel=0.04)
    # Conduit stub out of the box.
    metal.cyl_local(Mf, (0, d / 2 + 0.4, h * 0.4), (0, 1, 0), min(w, h) * 0.15, 0.9, segs=10)


def sample_points(ob, density):
    me = ob.data
    me.calc_loop_triangles()
    tris = []
    total = 0.0
    for lt in me.loop_triangles:
        total += lt.area
        tris.append((total, lt))
    count = int(total * density)
    pts = []
    keys = [t[0] for t in tris]
    for _ in range(count):
        k = bisect.bisect_left(keys, rng.random() * total)
        lt = tris[min(k, len(tris) - 1)][1]
        a, b, c = (me.vertices[i].co for i in lt.vertices)
        u, v = rng.random(), rng.random()
        if u + v > 1:
            u, v = 1 - u, 1 - v
        p = ob.matrix_world @ (a + (b - a) * u + (c - a) * v)
        n = (ob.matrix_world.to_3x3() @ lt.normal).normalized()
        pts.append((p, n))
    return pts


def scatter(ob, density, skip):
    placed = 0
    for p, n in sample_points(ob, density):
        if skip(p, n):
            continue
        if not clear_above(p, n, 3.5):
            continue
        hint = (0, 1, 0) if abs(n.y) < 0.9 else (0, 0, 1)
        Mf = frame_matrix(p, n, hint)
        # Keep footprints on the grid of the hull: no random yaw.
        if n.z > 0.7:
            kind = rng.random()
            if kind < 0.36:
                w, d, h = rng.uniform(0.8, 5), rng.uniform(0.8, 7), rng.uniform(0.3, 2.0)
                if footprint_ok(Mf, n, w / 2, d / 2):
                    g_box(pick_mb(), Mf, w, d, h)
                    placed += 1
            elif kind < 0.52:
                w, d, h = rng.uniform(1.5, 5), rng.uniform(1.5, 6), rng.uniform(0.5, 1.8)
                if footprint_ok(Mf, n, w / 2, d / 2):
                    g_stack(pick_mb(), Mf, w, d, h)
                    placed += 1
            elif kind < 0.64:
                r, length = rng.uniform(0.6, 1.5), rng.uniform(3, 9)
                if footprint_ok(Mf, n, r, length / 2):
                    g_tank(rng.choice((light, gray)), Mf, r, length)
                    placed += 1
            elif kind < 0.80:
                w, d = rng.uniform(1.5, 4), rng.uniform(2, 5)
                if footprint_ok(Mf, n, w / 2, d / 2):
                    g_vent(Mf, w, d, rng.randint(5, 9))
                    placed += 1
            elif kind < 0.91:
                r = rng.uniform(0.7, 1.4)
                if footprint_ok(Mf, n, r, r):
                    g_hatch_round(rng.choice((light, gray)), Mf, r)
                    placed += 1
            else:
                g_stackpipe(Mf, rng.uniform(0.3, 0.9), rng.uniform(1.0, 3.5))
                placed += 1
        elif n.z < -0.7:
            w, d = rng.uniform(1, 5), rng.uniform(1, 6)
            if footprint_ok(Mf, n, w / 2, d / 2):
                if rng.random() < 0.5:
                    g_box(pick_mb(), Mf, w, d, rng.uniform(0.3, 1.2))
                else:
                    g_panel(pick_mb(), Mf, w, d, rng.uniform(0.1, 0.25))
                placed += 1
        else:
            kind = rng.random()
            if kind < 0.45:
                w, d = rng.uniform(1.2, 5), rng.uniform(1, 6)
                if footprint_ok(Mf, n, w / 2, d / 2):
                    g_panel(pick_mb(), Mf, w, d, rng.uniform(0.1, 0.3))
                    placed += 1
            elif kind < 0.62:
                w, d = rng.uniform(1.5, 3), rng.uniform(2, 4)
                if footprint_ok(Mf, n, w / 2, d / 2):
                    g_vent(Mf, w, d, rng.randint(5, 8))
                    placed += 1
            elif kind < 0.9:
                w, d, h = rng.uniform(0.6, 2), rng.uniform(0.6, 2), rng.uniform(0.3, 1.0)
                if footprint_ok(Mf, n, w / 2, d / 2 + 0.9):
                    g_jbox(Mf, w, d, h)
                    placed += 1
            else:
                w, d = rng.uniform(1.6, 2.6), rng.uniform(1.2, 1.8)
                if footprint_ok(Mf, n, w / 2 + 0.3, d / 2 + 0.6):
                    g_door(Mf, w, d)
                    placed += 1
    return placed


def skip_fore(p, n):
    # The bridge's slanted brow carries the windows. The head's front carries the sensors.
    if n.y > 0.3 and p.z > 9.0:
        return True
    return n.y > 0.6


def skip_mid(p, n):
    return abs(n.y) > 0.6


def skip_aft(p, n):
    # The rear face carries the engine bells.
    return n.y < -0.6


stats = {
    "Hull_Fore": scatter(bpy.data.objects["Hull_Fore"], 0.07, skip_fore),
    "Hull_Mid": scatter(bpy.data.objects["Hull_Mid"], 0.07, skip_mid),
    "Hull_Aft": scatter(bpy.data.objects["Hull_Aft"], 0.06, skip_aft),
}

# --- Frame ribs around the mid hull ------------------------------------------
for k in range(14):
    y = -52 + 8 * k
    metal.box((0, y, 0), (33.4, 0.9, 19.4), bevel=0.15)
    if y < 45:
        metal.box((0, y, 11.8), (21.4, 0.9, 5.4), bevel=0.15)
    metal.box((0, y, -11.6), (15.2, 0.9, 5.2), bevel=0.15)
# Heavy frame bands where the mid hull meets the engine block and the nose.
for y in (-55.5, 55.0):
    metal.box((0, y, 0), (34.5, 2.0, 21.0), bevel=0.3)

light.finish("Greebles_Light", M["M_PaintLight"], "Greebles")
gray.finish("Greebles_Gray", M["M_PaintGray"], "Greebles")
metal.finish("Greebles_Metal", M["M_FrameMetal"], "Greebles")
stats["faces"] = {o.name: len(o.data.polygons) for o in coll("Greebles").objects}
result = stats
