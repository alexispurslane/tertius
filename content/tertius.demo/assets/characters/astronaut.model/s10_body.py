# The human body, made from parameters with the MakeHuman data of MPFB 2.0.18 (native/mpfb.nix, TERTIUS_MPFB).
# No code of the add-on runs. This script reads the base mesh (hm08, data/3dobjs/base.obj), sums the macro targets
# (data/targets/macrodetails) with the weights that MPFB's TargetService.calculate_target_stack_from_macro_info_dict
# gives for the values of parameters.human, and reads the bone weights of MPFB's "game_engine" rig to sort the
# vertices into body regions.
#
# It makes:
#   Body     the skin, every face of the base mesh group "body", in the order of base.obj. Its vertex groups are the
#            bones of the rig. Later scripts cut the head, the suit, the gloves and the boots from it, and
#            s80_assemble.py deletes it.
#   Eyes     the two eyeballs (helper-l-eye, helper-r-eye), a final part.
#   the scene property astronaut_joints: a JSON object, joint name -> [x, y, z] in meters, from the joint cubes.
#
# base.obj is in decimeters with +Y up and the face toward +Z. A point (x, y, z) of the file becomes
# (x, -z, y) / 10 here, as in MPFB, so the face looks toward -Y. MakeHuman's "l" side is the person's left, at +X.
import gzip
import json
import math
import os

import bmesh
import bpy
import numpy as np

from tertius_blender.geometry import MB

DATA = os.path.join(os.environ["TERTIUS_MPFB"], "data")
human = model.params["human"]


def read_obj(path):
    verts, faces, groups = [], [], []
    group = None
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            if line.startswith("v "):
                verts.append([float(t) for t in line.split()[1:4]])
            elif line.startswith("g "):
                group = line[2:].strip()
            elif line.startswith("f "):
                faces.append([int(t.split("/")[0]) - 1 for t in line.split()[1:]])
                groups.append(group)
    return np.array(verts, dtype=np.float64), faces, groups


# --- The macro weights, as MPFB computes them -------------------------------------------------------------------
with open(os.path.join(DATA, "targets", "macrodetails", "macro.json"), encoding="utf-8") as fh:
    MACRO = json.load(fh)["macrotargets"]


def components(name, value):
    """The (target word, weight) pairs of one macro value: MPFB's _interpolate_macro_components."""
    out = []
    for part in MACRO[name]["parts"]:
        lowest, highest = part["lowest"], part["highest"]
        if lowest < value < highest:
            position = (value - lowest) / (highest - lowest)
            if part["low"]:
                out.append((part["low"], round(1 - position, 4)))
            if part["high"]:
                out.append((part["high"], round(position, 4)))
    return out


def target_stack(h, cutoff=0.01):
    """The targets and their weights. Breast targets (cup size and firmness) are left out: at their default 0.5 MPFB
    loads none either."""
    c = {k: components(k, h[k]) for k in ("gender", "age", "muscle", "weight", "proportions", "height")}
    stack = []
    for race, share in h["race"].items():
        for a, wa in c["age"]:
            for g, wg in c["gender"]:
                w = share * wg * wa
                if w > cutoff:
                    stack.append(("macrodetails/{}-{}-{}".format(race, g, a), w))
    for g, wg in c["gender"]:
        for a, wa in c["age"]:
            for m, wm in c["muscle"]:
                for wt, ww in c["weight"]:
                    base = wg * wa * wm * ww
                    if base > cutoff:
                        stack.append(("macrodetails/universal-{}-{}-{}-{}".format(g, a, m, wt), base))
                    for ht, wh in c["height"]:
                        if base * wh > cutoff:
                            stack.append(("macrodetails/height/{}-{}-{}-{}-{}".format(g, a, m, wt, ht), base * wh))
                    for p, wp in c["proportions"]:
                        if base * wp > cutoff and a != "baby":
                            stack.append(("macrodetails/proportions/{}-{}-{}-{}-{}".format(g, a, m, wt, p),
                                          base * wp))
    return stack


def apply_target(coords, name, weight):
    path = os.path.join(DATA, "targets", name + ".target.gz")
    with gzip.open(path, "rt", encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line or line[0] in "#\"":
                continue
            p = line.split()
            coords[int(p[0])] += weight * np.array((float(p[1]), float(p[2]), float(p[3])))


coords, faces, groups = read_obj(os.path.join(DATA, "3dobjs", "base.obj"))
stack = target_stack(human)
for name, weight in stack:
    apply_target(coords, name, weight)
# File axes in decimeters -> Blender axes in meters.
coords = np.stack((coords[:, 0], -coords[:, 2], coords[:, 1]), axis=1) * 0.1

with open(os.path.join(DATA, "mesh_metadata", "basemesh_vertex_groups.json"), encoding="utf-8") as fh:
    vgroups = json.load(fh)


def indices(group):
    return [i for lo, hi in vgroups[group] for i in range(lo, hi + 1)]


body_range = indices("body")
n_body = len(body_range)
assert body_range == list(range(n_body))

# Stand on the soles: the lowest skin point sits at the height of the boot sole.
shift = np.array((0.0, 0.0, model.params["suit"]["sole"] - coords[:n_body, 2].min()))
joints = {}
for group in vgroups:
    if group.startswith("joint-"):
        joints[group[6:]] = coords[indices(group)].mean(axis=0)
shift[1] = -joints["pelvis"][1]

with open(os.path.join(DATA, "rigs", "standard", "weights.game_engine.json"), encoding="utf-8") as fh:
    weights = json.load(fh)["weights"]


# --- A relaxed hand: the fingers curl a little, by linear blend skinning with the rig's weights. ----------------------
def turn(center, axis, degrees):
    """The 4x4 rotation by `degrees` about the line through `center` along `axis`."""
    a = np.asarray(axis) / np.linalg.norm(axis)
    t = math.radians(degrees)
    k = np.array(((0, -a[2], a[1]), (a[2], 0, -a[0]), (-a[1], a[0], 0)))
    r = np.eye(3) + math.sin(t) * k + (1 - math.cos(t)) * (k @ k)
    m = np.eye(4)
    m[:3, :3] = r
    m[:3, 3] = center - r @ center
    return m


curl = model.params["pose"]["finger_curl_deg"]
posed = coords[:n_body].copy()
for s in "lr":
    # Across the knuckles, from the little finger to the index finger. The sign of the curl turns the fingers toward
    # the palm.
    across = joints[s + "-finger-2-1"] - joints[s + "-finger-5-1"]
    sign = model.params["pose"]["curl_sign"][s]
    for f, finger in ((2, "index"), (3, "middle"), (4, "ring"), (5, "pinky")):
        m = np.eye(4)
        for k in (1, 2, 3):
            m = m @ turn(joints["{}-finger-{}-{}".format(s, f, k)], across, sign * curl[k - 1])
            bone = "{}_0{}_{}".format(finger, k, s)
            for i, w in weights[bone]:
                if i < n_body and w > 0.0:
                    p = np.append(coords[i], 1.0)
                    posed[i] += w * ((m @ p)[:3] - coords[i])
coords[:n_body] = posed
coords += shift
joints = {k: [round(float(c), 6) for c in v + shift] for k, v in sorted(joints.items())}
bpy.context.scene["astronaut_joints"] = json.dumps(joints)

# --- Body: the skin with the bone weights as vertex groups --------------------------------------------------------

bm = bmesh.new()
bverts = [bm.verts.new(tuple(c)) for c in coords[:n_body]]
for face, group in zip(faces, groups):
    if group == "body":
        bm.faces.new([bverts[i] for i in face])
me = bpy.data.meshes.new("Body")
bm.to_mesh(me)
bm.free()
body = bpy.data.objects.new("Body", me)
bpy.data.collections["Body"].objects.link(body)
for bone in sorted(weights):
    pairs = [(i, w) for i, w in weights[bone] if i < n_body and w > 0.0]
    if not pairs:
        continue
    vg = body.vertex_groups.new(name=bone)
    for i, w in pairs:
        vg.add([i], w, "REPLACE")

# --- Eyes ---------------------------------------------------------------------------------------------------------
mb = MB()
eye_index = {}
for face, group in zip(faces, groups):
    if group in ("helper-l-eye", "helper-r-eye"):
        vs = []
        for i in face:
            if i not in eye_index:
                eye_index[i] = mb.bm.verts.new(tuple(coords[i]))
            vs.append(eye_index[i])
        mb.bm.faces.new(vs)
eyes = mb.finish("Eyes", bpy.data.materials["M_Eye"], "Body", smooth_angle=80.0)

lo, hi = coords[:n_body].min(axis=0), coords[:n_body].max(axis=0)
result = {"targets": [[n, round(w, 4)] for n, w in stack], "body_vertices": n_body,
          "body_faces": len(me.polygons), "size_m": [round(float(v), 3) for v in hi - lo],
          "joints": len(joints)}
