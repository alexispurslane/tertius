# Engine bells on the rear face (y = -103), their housings and cooling rings, and the glow
# discs deep in each throat. Bells point along -Y.
# In the build frame: nose toward +Y (see s00_scene.py).
import math

import bpy
from mathutils import Matrix, Vector

from tertius_blender.geometry import MB

M = bpy.data.materials
# Local +Z of a lathe profile -> world -Y.
TO_REAR = Matrix.Rotation(math.radians(90), 4, "X")


def bell(mb, x, z, y0, r_throat, r_exit, length, segs=64):
    """A bell with wall thickness, revolved from (radius, distance-from-throat) pairs."""
    t = max(0.25, r_exit * 0.035)
    outer = []
    inner = []
    n = 10
    for i in range(n + 1):
        u = i / n
        # Bell curve: fast growth near the exit.
        r = r_throat + (r_exit - r_throat) * (u ** 1.6)
        outer.append((r + t, u * length))
        inner.append((r, u * length))
    profile = outer + [(r_exit + t * 0.5, length + t * 0.6)] + list(reversed(inner))
    Mw = Matrix.Translation((x, y0, z)) @ TO_REAR
    mb.lathe(profile, Mw, segs=segs)
    # Cooling rings at a few stations.
    for u in (0.3, 0.62, 0.9):
        r = r_throat + (r_exit - r_throat) * (u ** 1.6) + t
        ring = [(r - 0.05, -0.25), (r + 0.35, -0.2), (r + 0.35, 0.2), (r - 0.05, 0.25)]
        mb.lathe(ring, Mw @ Matrix.Translation((0, 0, u * length)), segs=segs)
    return Mw


eng = MB()
glow = MB()
MAIN = [(-15, -3), (15, -3), (0, 12)]
SMALL = [(-25.5, 13.5), (25.5, 13.5), (-25.5, -11), (25.5, -11)]
for x, z in MAIN:
    # Housing between the collar and the bell.
    eng.cyl((x, -104.5, z), (0, 1, 0), 6.4, 3.0, segs=48)
    eng.cyl((x, -106.4, z), (0, 1, 0), 5.6, 1.0, segs=48)
    # Gimbal struts around the throat.
    for k in range(6):
        a = 2 * math.pi * k / 6 + 0.3
        p = Vector((x + 5.0 * math.cos(a), -105.5, z + 5.0 * math.sin(a)))
        eng.cyl(p, (0, 1, 0), 0.45, 4.5, segs=10)
    Mw = bell(eng, x, z, -106.8, 4.4, 8.6, 15.0)
    glow.cyl((x, -107.5, z), (0, 1, 0), 4.3, 0.2, segs=48)
for x, z in SMALL:
    eng.cyl((x, -103.8, z), (0, 1, 0), 2.6, 1.6, segs=32)
    bell(eng, x, z, -104.5, 1.4, 3.0, 6.0, segs=32)
    glow.cyl((x, -105.0, z), (0, 1, 0), 1.35, 0.15, segs=24)

eng.finish("Engines", M["M_Engine"], "Engines", smooth_angle=50.0)
glow.finish("Engine_Glow", M["M_EngineGlow"], "Lights")
result = {"Engines": len(bpy.data.objects["Engines"].data.polygons)}
