"""Throwaway (t1-3j2j): the geometry's answer to "does the sun light this point", with no renderer.

  blender -b --factory-startup --python sun_cast.py -- <export folder> <out.npz> box <x0> <y0> <x1> <y1> [<step>]
  blender -b --factory-startup --python sun_cast.py -- <export folder> <out.npz> line <x y z> <x y z> <nx ny nz> <count>
  blender -b --factory-startup --python sun_cast.py -- <export folder> <out.npz> pixels <pixels.npy>

The export is `scene.json` and `mesh-<id>.bin` of the probe's SceneExport (stage-2/reference-switch-and-export.patch):
camera-relative positions in the engine's world axes, in m. The box is in output pixels of 1920 x 1080 from the top left,
x1 and y1 excluded, every <step>-th pixel (1). The line mode takes <count> points from the first to the second point, on a
surface whose normal is <nx ny nz>. The pixels mode takes the output pixels of a NumPy array of (x, y) rows.

Box and pixels mode cast one camera ray through each pixel's center. The ray meets the nearest front face of a visible instance,
as the engine's G-buffer does: back faces are culled for instances that are not two-sided, glass is not in it, and an
instance without the Visible flag is not either. From that point, it casts CAST_SUN_SAMPLES (256) lines to points spread
evenly over the sun's disc, through every shadow caster: each triangle of an instance with CastsShadow, and each
celestial body as its exact sphere. Glass casts no shadow, as in the engine. Each line starts 0.1 mm off the surface
along its normal. The share of the lines that reach the sun is the point's sunlit share in the truth. A face that
turns from the sun by more than 0.01 in its cosine, more than the disc's angular radius of 0.0047, gets 0 with no cast.
CAST_TARGET=<instance index> aims at that instance's sphere in place of the sun: its `share` is the seen part of its disc.

The npz holds, per point: `position` (m), `normal` (the face's geometric normal toward the camera), `instance` (the index
in scene.json, -1 for none, -2 - k for celestial body k), `ndotl` (with the sun's center), `center` (1 when the line to
the sun's center is free), `blocker` (the instance that blocks that line, -1 for none or a face turned away), `share`
(the sunlit share of the disc), and `x` and `y`, the output pixels (-1 in line
mode).
"""
import json
import math
import os
import struct
import sys
from pathlib import Path

import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
folder, out, mode = Path(argv[0]), argv[1], argv[2]
W, H = 1920, 1080
FAR = 1e9
CELESTIAL = 1 << 4
OFFSET = 1e-4
SAMPLES = int(os.environ.get("CAST_SUN_SAMPLES", "256"))

scene = json.loads((folder / "scene.json").read_text())


def mesh(mesh_id):
    data = (folder / f"mesh-{mesh_id}.bin").read_bytes()
    nv, ni = struct.unpack_from("<ii", data, 0)
    pos = np.frombuffer(data, "<f4", 3 * nv, 8).reshape(nv, 3).astype(np.float64)
    nrm = np.frombuffer(data, "<f4", 3 * nv, 8 + 12 * nv).reshape(nv, 3).astype(np.float64)
    idx = np.frombuffer(data, "<u4", ni, 8 + 24 * nv).reshape(-1, 3)
    return pos, nrm, idx


def placed(inst):
    x, y, z, w = inst["rotation"]
    rot = np.array(Quaternion((w, x, y, z)).to_matrix(), dtype=np.float64)
    return rot @ np.diag(inst["scale"]), np.array(inst["position"], dtype=np.float64)


# The scene's sets: the visible surfaces (camera rays) and the casters (sun lines), with each triangle's instance and its
# outward normal, from the winding, turned to agree with the mean of its vertex normals.
visible = {"verts": [], "polys": [], "inst": [], "normal": [], "two": []}
casters = {"verts": [], "polys": [], "inst": [], "normal": [], "two": []}
spheres = []  # (instance, center, radius, visible, caster)
sun = None
for n, light in enumerate(scene["lights"]):
    if light["kind"] == "point" and light["distance"] > FAR:
        sun = (n, np.array(light["position"], dtype=np.float64), light["radius"], light["distance"])
# CAST_TARGET=<instance index> aims the lines at that instance's sphere, such as the Moon, in place of the sun's disc.
target = int(os.environ.get("CAST_TARGET", "-1"))
if target >= 0:
    body = scene["instances"][target]
    sun = (target, np.array(body["position"], dtype=np.float64), body["radius"], float(np.linalg.norm(body["position"])))
for n, inst in enumerate(scene["instances"]):
    flags = inst["flags"]
    glass = "Glass" in inst["alpha"]
    center = np.array(inst["position"], dtype=np.float64)
    if np.linalg.norm(center) > FAR:
        continue  # the sun's own sphere: the sun itself
    if flags & CELESTIAL:
        spheres.append((n, center, inst["radius"], bool(flags & 1), bool(flags & 2)))
        continue
    if glass:
        continue
    pos, nrm, idx = mesh(inst["mesh"])
    m, t = placed(inst)
    world = pos @ m.T + t
    winding = np.cross(world[idx[:, 1]] - world[idx[:, 0]], world[idx[:, 2]] - world[idx[:, 0]])
    winding /= np.maximum(np.linalg.norm(winding, axis=1, keepdims=True), 1e-30)
    vertex_mean = (nrm[idx[:, 0]] + nrm[idx[:, 1]] + nrm[idx[:, 2]]) @ np.linalg.inv(m)
    winding *= np.where((winding * vertex_mean).sum(1) < 0, -1.0, 1.0)[:, None]
    for target, take in ((visible, flags & 1), (casters, flags & 2)):
        if not take:
            continue
        base = sum(len(v) for v in target["verts"])
        target["verts"].append(world)
        target["polys"].append(idx.astype(np.int64) + base)
        target["inst"].append(np.full(len(idx), n))
        target["normal"].append(winding)
        target["two"].append(np.full(len(idx), inst["twoSided"]))


def tree(s):
    verts = np.concatenate(s["verts"])
    polys = np.concatenate(s["polys"])
    s["inst"] = np.concatenate(s["inst"])
    s["normal"] = np.concatenate(s["normal"])
    s["two"] = np.concatenate(s["two"])
    return BVHTree.FromPolygons([Vector(v) for v in verts], [tuple(int(i) for i in p) for p in polys], epsilon=0.0)


visible_tree, caster_tree = tree(visible), tree(casters)


def sphere_hit(origin, direction, center, radius):
    """The distance along a unit direction to a sphere's front surface, or None. In float64."""
    oc = origin - center
    b = float(oc @ direction)
    c = float(oc @ oc) - radius * radius
    disc = b * b - c
    if disc < 0:
        return None
    root = math.sqrt(disc)
    t = -b - root if -b - root > 0 else -b + root
    return t if t > 0 else None


def camera_hit(direction):
    """The first front face that a camera ray meets: (position, normal, instance, distance) or None."""
    origin = Vector((0.0, 0.0, 0.0))
    d = Vector(direction)
    travelled = 0.0
    best = None
    for _ in range(64):
        loc, _normal, index, dist = visible_tree.ray_cast(origin, d)
        if loc is None:
            break
        normal = visible["normal"][index]
        travelled += dist
        if visible["two"][index] or float(normal @ direction) < 0:
            best = (np.array(loc), normal if float(normal @ direction) < 0 else -normal, int(visible["inst"][index]), travelled)
            break
        origin = loc + d * OFFSET
        travelled += OFFSET
    for k, (n, center, radius, vis, _) in enumerate(spheres):
        if not vis:
            continue
        t = sphere_hit(np.zeros(3), np.array(direction), center, radius)
        if t is not None and (best is None or t < best[3]):
            p = np.array(direction) * t
            best = (p, (p - center) / radius, -2 - k, t)
    return best


def disc_directions(from_point):
    """SAMPLES unit directions spread evenly over the sun's disc seen from a point, by the sunflower pattern."""
    _, position, radius, _ = sun
    axis = position - from_point
    distance = np.linalg.norm(axis)
    axis /= distance
    angular = math.asin(min(radius / distance, 1.0))
    helper = np.array([1.0, 0.0, 0.0]) if abs(axis[0]) < 0.9 else np.array([0.0, 1.0, 0.0])
    t1 = np.cross(axis, helper)
    t1 /= np.linalg.norm(t1)
    t2 = np.cross(axis, t1)
    golden = math.pi * (3 - math.sqrt(5))
    out = []
    for i in range(SAMPLES):
        rho = angular * math.sqrt((i + 0.5) / SAMPLES)
        phi = i * golden
        d = axis + math.tan(rho) * (math.cos(phi) * t1 + math.sin(phi) * t2)
        out.append(d / np.linalg.norm(d))
    return axis, out


def blocker(origin, direction):
    """The instance whose caster first meets the line toward the sun, or -1 when the line is free."""
    loc, _, index, _ = caster_tree.ray_cast(Vector(origin), Vector(direction))
    if loc is not None:
        return int(casters["inst"][index])
    for n, center, radius, _, cast in spheres:
        if cast and sphere_hit(origin, direction, center, radius) is not None:
            return n
    return -1


def reaches_sun(origin, direction):
    return blocker(origin, direction) == -1


def sunlit(point, normal):
    axis, directions = disc_directions(point)
    if float(normal @ axis) < -0.01:
        return float(normal @ axis), 0.0, 0.0, -1  # the face turns from the whole disc: no line needs casting
    origin = point + normal * OFFSET
    free = sum(reaches_sun(origin, d) for d in directions)
    first = blocker(origin, axis)
    return float(normal @ axis), float(first == -1), free / len(directions), first


right, up, forward = (np.array(scene["camera"][k], dtype=np.float64) for k in ("right", "up", "forward"))
tan_x, tan_y = scene["camera"]["tanHalfX"], scene["camera"]["tanHalfY"]
points = []
if mode in ("box", "pixels"):
    if mode == "box":
        x0, y0, x1, y1 = map(int, argv[3:7])
        step = int(argv[7]) if len(argv) > 7 else 1
        pixels = [(x, y) for y in range(y0, y1, step) for x in range(x0, x1, step)]
    else:
        pixels = [tuple(int(v) for v in xy) for xy in np.load(argv[3])]
    for x, y in pixels:
            nx = (x + 0.5) / W * 2 - 1
            ny = 1 - (y + 0.5) / H * 2
            d = forward + right * nx * tan_x + up * ny * tan_y
            points.append((x, y, camera_hit(d / np.linalg.norm(d))))
else:
    a = np.array(list(map(float, argv[3:6])))
    b = np.array(list(map(float, argv[6:9])))
    normal = np.array(list(map(float, argv[9:12])))
    count = int(argv[12])
    for i in range(count):
        p = a + (b - a) * (i / max(count - 1, 1))
        points.append((-1, -1, (p, normal / np.linalg.norm(normal), -1, float(np.linalg.norm(p)))))

result = {k: [] for k in ("x", "y", "position", "normal", "instance", "ndotl", "center", "share", "blocker")}
for x, y, hit in points:
    result["x"].append(x)
    result["y"].append(y)
    if hit is None or hit[2] <= -2:
        result["position"].append(hit[0] if hit else np.full(3, np.nan))
        result["normal"].append(hit[1] if hit else np.full(3, np.nan))
        result["instance"].append(hit[2] if hit else -1)
        result["ndotl"].append(np.nan)
        result["center"].append(np.nan)
        result["share"].append(np.nan)
        result["blocker"].append(-1)
        continue
    position, normal, instance, _ = hit
    ndotl, center, share, first = sunlit(position, normal)
    result["blocker"].append(first)
    result["position"].append(position)
    result["normal"].append(normal)
    result["instance"].append(instance)
    result["ndotl"].append(ndotl)
    result["center"].append(center)
    result["share"].append(share)
np.savez(out, **{k: np.array(v) for k, v in result.items()}, sun_index=sun[0], samples=SAMPLES)
lit = np.array(result["share"])
print("CAST", len(points), "points,", int(np.nansum(lit > 0)), "with any sunlight, wrote", out)
