# The soft shells cut from the body of s10_body.py: the head, the suit, the two gloves and the two boots.
#
# Each shell is a set of faces of Body, chosen by the bone weights of its vertices and by the joints. A shell is
# subdivided (the suit only), smoothed so that anatomy such as muscles and the navel goes away, pushed out along its
# normals by a thickness that depends on the bones under each vertex, and smoothed again. The open edges of the
# shells lie under the hard parts of s30_hardware.py: the neck ring, the glove gauntlets and the boot shafts.
#
# The suit gets folds: rings of bulges along the limb around each elbow and knee, like the convolutes of a real
# EVA suit, and seeded fabric wrinkles across each limb segment.
import json
import math

import bpy
import numpy as np
from mathutils import Vector

from tertius_blender.geometry import MB

P = model.params["suit"]
body = bpy.data.objects["Body"]
J = {k: np.array(v) for k, v in json.loads(bpy.context.scene["astronaut_joints"]).items()}
X = np.array([v.co[:] for v in body.data.vertices])
F_ALL = [list(p.vertices) for p in body.data.polygons]
BONES = [g.name for g in body.vertex_groups]
W = np.zeros((len(X), len(BONES)))
for v in body.data.vertices:
    for g in v.groups:
        W[v.index, g.group] = g.weight


def bone_weight(prefixes):
    """The summed weight of the bones whose names start with any of `prefixes`, per vertex of Body."""
    cols = [i for i, b in enumerate(BONES) if b.startswith(tuple(prefixes))]
    return W[:, cols].sum(axis=1)


def side_of(name):
    return "l" if name.endswith("_l") else "r"


FINGERS = [f + "_0" + str(k) for f in ("index", "middle", "ring", "pinky", "thumb") for k in (1, 2, 3)]
HAND = {s: bone_weight(["hand_" + s] + [f + "_0{}_{}".format(k, s) for f in ("index", "middle", "ring", "pinky",
                                                                             "thumb") for k in (1, 2, 3)])
        for s in "lr"}
ARM = {s: bone_weight(["upperarm_" + s, "lowerarm_" + s]) + HAND[s] for s in "lr"}
LEG = {s: bone_weight(["thigh_" + s, "calf_" + s, "foot_" + s, "ball_" + s]) for s in "lr"}


def along(points, a, b):
    """The position of each point along the segment from joint a to joint b: 0 at a, 1 at b."""
    d = J[b] - J[a]
    return (points - J[a]) @ d / (d @ d)


# --- Numpy mesh helpers -------------------------------------------------------------------------------------------
def submesh(face_ids):
    """The faces `face_ids` of Body as a local mesh: (positions, quads as an (F, 4) array, Body vertex ids). A
    triangle repeats its last vertex."""
    used = sorted({i for f in face_ids for i in F_ALL[f]})
    local = {g: k for k, g in enumerate(used)}
    quads = np.array([[local[i] for i in (F_ALL[f] + F_ALL[f][-1:])[:4]] for f in face_ids], dtype=np.int64)
    return X[used].copy(), quads, np.array(used)


def edges_of(quads):
    e = np.concatenate([quads[:, [0, 1]], quads[:, [1, 2]], quads[:, [2, 3]], quads[:, [3, 0]]])
    e = e[e[:, 0] != e[:, 1]]
    e.sort(axis=1)
    return np.unique(e, axis=0)


def boundary_mask(n, quads):
    e = np.concatenate([quads[:, [0, 1]], quads[:, [1, 2]], quads[:, [2, 3]], quads[:, [3, 0]]])
    e = e[e[:, 0] != e[:, 1]]
    e.sort(axis=1)
    uniq, count = np.unique(e, axis=0, return_counts=True)
    mask = np.zeros(n, dtype=bool)
    mask[uniq[count == 1].ravel()] = True
    return mask


def smooth(pos, edges, iterations, lam=0.5, mu=-0.53, fixed=None, weight=None, quads=None):
    """Taubin smoothing: a shrinking and an inflating Laplacian step per iteration, so the shell keeps its size.
    With mu = 0 it is plain Laplacian smoothing, which also shrinks. `weight` (0 to 1 per vertex) scales the step.
    With `quads`, each step moves a vertex only along its normal, so the vertices do not crowd together where the
    mesh is dense, which folds faces."""
    n = len(pos)
    count = np.zeros(n)
    np.add.at(count, edges[:, 0], 1)
    np.add.at(count, edges[:, 1], 1)
    count = np.maximum(count, 1)[:, None]
    for _ in range(iterations):
        for factor in (lam, mu):
            s = np.zeros_like(pos)
            np.add.at(s, edges[:, 0], pos[edges[:, 1]])
            np.add.at(s, edges[:, 1], pos[edges[:, 0]])
            step = s / count - pos
            if fixed is not None:
                step[fixed] = 0.0
            if weight is not None:
                step = step * weight[:, None]
            if quads is not None:
                n = normals(pos, quads)
                step = (step * n).sum(axis=1)[:, None] * n
            pos = pos + factor * step
    return pos


def normals(pos, quads):
    fn = np.cross(pos[quads[:, 2]] - pos[quads[:, 0]], pos[quads[:, 3]] - pos[quads[:, 1]])
    vn = np.zeros_like(pos)
    for k in range(4):
        np.add.at(vn, quads[:, k], fn)
    length = np.linalg.norm(vn, axis=1)[:, None]
    return vn / np.maximum(length, 1e-12)


def subdivide(pos, quads, values):
    """Splits each face into quads at its edge midpoints and its center. `values` (per-vertex rows) are averaged the
    same way, so the bone weights follow the new vertices."""
    edges = edges_of(quads)
    key = {(int(a), int(b)): k for k, (a, b) in enumerate(edges)}
    n, ne = len(pos), len(edges)
    mid = (pos[edges[:, 0]] + pos[edges[:, 1]]) / 2
    mid_v = (values[edges[:, 0]] + values[edges[:, 1]]) / 2
    centers, center_v, out = [], [], []
    for f, q in enumerate(quads):
        ring = [int(i) for i in dict.fromkeys(q.tolist())]
        c = n + ne + f
        centers.append(pos[ring].mean(axis=0))
        center_v.append(values[ring].mean(axis=0))
        m = len(ring)
        for k in range(m):
            a, b, z = ring[k], ring[(k + 1) % m], ring[k - 1]
            e_next = n + key[(min(a, b), max(a, b))]
            e_prev = n + key[(min(z, a), max(z, a))]
            out.append([a, e_next, c, e_prev])
    return (np.concatenate([pos, mid, np.array(centers)]), np.array(out, dtype=np.int64),
            np.concatenate([values, mid_v, np.array(center_v)]))


def finish(name, pos, quads, material, collection, smooth_angle=60.0):
    mb = MB()
    vs = {}
    for q in quads:
        ring = list(dict.fromkeys(q.tolist()))
        for i in ring:
            if i not in vs:
                vs[i] = mb.bm.verts.new(tuple(pos[i]))
        mb.bm.faces.new([vs[i] for i in ring])
    return mb.finish(name, bpy.data.materials[material], collection, smooth_angle=smooth_angle)


def thickness_of(weights, table):
    """The thickness of each vertex: the bone thicknesses of `table` (prefix -> m) weighted by the bone weights."""
    total = np.zeros(len(weights))
    norm = np.zeros(len(weights))
    for prefix, t in table.items():
        cols = [i for i, b in enumerate(BONES) if b.startswith(prefix)]
        w = weights[:, cols].sum(axis=1)
        total += w * t
        norm += w
    return total / np.maximum(norm, 1e-6)


def faces_where(test):
    """The ids of the faces of Body whose every vertex passes `test` (a boolean array over the vertices of Body)."""
    return [f for f, vs in enumerate(F_ALL) if all(test[i] for i in vs)]


# --- Regions ------------------------------------------------------------------------------------------------------
z = X[:, 2]
head_cut = J["neck"][2] + P["neck_cut"]
t_arm = {s: along(X, "{}-elbow".format(s), "{}-hand".format(s)) for s in "lr"}
ankle_z = {s: J["{}-ankle".format(s)][2] for s in "lr"}

in_hand = np.zeros(len(X), dtype=bool)
in_glove = np.zeros(len(X), dtype=bool)
in_foot = np.zeros(len(X), dtype=bool)
in_boot = np.zeros(len(X), dtype=bool)
for s in "lr":
    arm = ARM[s] > 0.5
    in_hand |= arm & (t_arm[s] > P["sleeve_end"])
    in_glove |= arm & (t_arm[s] > P["glove_start"])
    leg = LEG[s] > 0.5
    in_foot |= leg & (z < ankle_z[s] + P["leg_end"])
    in_boot |= leg & (z < ankle_z[s] + P["boot_top"])

# --- Head ---------------------------------------------------------------------------------------------------------
head_faces = faces_where(z > head_cut)
hp, hq, _ = submesh(head_faces)
head = finish("Head", hp, hq, "M_Skin", "Body", smooth_angle=80.0)

# --- Suit ---------------------------------------------------------------------------------------------------------
suit_faces = faces_where((z < head_cut + P["collar_overlap"]) & ~in_hand & ~in_foot)
sp, sq, sids = submesh(suit_faces)
sw = W[sids]
# The torso keeps no anatomy: plain Laplacian smoothing, which also shrinks it, on the vertices of the trunk bones,
# before the subdivision, where each step reaches twice as far. The thicker torso of suit_thickness makes up for the
# shrink.
trunk = np.clip(sum(sw[:, i] for i, b in enumerate(BONES) if b.startswith(("spine", "pelvis", "clavicle", "neck"))),
                0.0, 1.0)
sp = smooth(sp, edges_of(sq), P["torso_smooth"], mu=0.0, fixed=boundary_mask(len(sp), sq), weight=trunk, quads=sq)
sp, sq, sw = subdivide(sp, sq, sw)
se = edges_of(sq)
sb = boundary_mask(len(sp), sq)
sp = smooth(sp, se, P["suit_smooth"], fixed=sb)
thick = thickness_of(sw, P["suit_thickness"])
# The sleeves narrow toward the wrist, so the wrist ring of s30_hardware.py can close them.
for s in "lr":
    on_arm = np.clip(sum(sw[:, i] for i, b in enumerate(BONES)
                         if b in ("lowerarm_" + s, "hand_" + s) or (b.endswith("_" + s) and b[:-2] in FINGERS)),
                     0.0, 1.0)
    t = along(sp, s + "-elbow", s + "-hand")
    taper = np.clip((t - P["wrist_taper_start"]) / (P["sleeve_end"] - P["wrist_taper_start"]), 0.0, 1.0)
    taper = taper * taper * (3 - 2 * taper) * on_arm
    thick = thick + (P["wrist_thickness"] - thick) * taper
sp = sp + normals(sp, sq) * thick[:, None]
sp = smooth(sp, se, P["suit_smooth_after"], fixed=sb)


def limb_frame(points, a, b):
    """The position along the limb from joint a to joint b in meters, and the angle around it (0 toward the front)."""
    axis = J[b] - J[a]
    axis = axis / np.linalg.norm(axis)
    rel = points - J[a]
    d = rel @ axis
    front = np.array((0.0, -1.0, 0.0))
    front = front - (front @ axis) * axis
    front /= np.linalg.norm(front)
    side = np.cross(axis, front)
    return d, np.arctan2(rel @ side, rel @ front)


def limb_weight(weights, bones):
    cols = [i for i, name in enumerate(BONES) if name in bones]
    return np.clip(weights[:, cols].sum(axis=1), 0.0, 1.0)


# Convolutes: bulges in rings around each elbow and knee, along the limb.
fold = P["convolute"]
wr = P["wrinkles"]
rng = np.random.default_rng(model.rng.getrandbits(64))
n_suit = normals(sp, sq)
bulge = np.zeros(len(sp))
wrinkle = np.zeros(len(sp))
for s in "lr":
    for a, b, c, bones in (("shoulder", "elbow", "hand", ("upperarm_", "lowerarm_")),
                           ("upper-leg", "knee", "ankle", ("thigh_", "calf_"))):
        a, b, c = s + "-" + a, s + "-" + b, s + "-" + c
        mask = limb_weight(sw, [n + s for n in bones])
        d, _ = limb_frame(sp, b, c)
        window = np.clip(1.0 - (np.abs(d) / fold["half_length"]) ** 2, 0.0, 1.0)
        # A squared cosine: rounded ridges with narrow valleys between them, like the convolutes of a real joint.
        wave = (0.5 + 0.5 * np.cos(2.0 * math.pi * d / fold["pitch"])) ** 2
        bulge = np.maximum(bulge, mask * fold["depth"] * wave * window)
        # Fabric wrinkles on each limb segment: waves across the limb, bent and faded around it, seeded per segment.
        for j0, j1, bone in ((a, b, bones[0] + s), (b, c, bones[1] + s)):
            m = limb_weight(sw, [bone])
            d, th = limb_frame(sp, j0, j1)
            pitch = rng.uniform(*wr["pitch"])
            p1, p2, p3 = rng.uniform(0, 2 * math.pi, size=3)
            wave = np.sin(2 * math.pi * d / pitch + wr["bend"] * np.sin(2 * th + p1) + p2)
            fade = np.clip(0.55 + 0.45 * np.sin(th + p3), 0.0, 1.0)
            wrinkle += m * wr["depth"] * wave * fade
sp = sp + n_suit * (bulge + wrinkle)[:, None]
# Where a limb meets the trunk, the offset surfaces of the two cross and crumple. Smoothing along the normals there,
# weighted most where the trunk and the limb bones share a vertex equally, irons them out.
trunk = np.clip(sum(sw[:, i] for i, b in enumerate(BONES) if b.startswith(("spine", "pelvis", "clavicle", "neck"))),
                0.0, 1.0)
junction = np.clip(4.0 * trunk * (1.0 - trunk) * P["junction_gain"], 0.0, 1.0)
sp = smooth(sp, se, P["junction_smooth"], fixed=sb, weight=junction, quads=sq)
suit = finish("Suit", sp, sq, "M_Suit", "Suit", smooth_angle=70.0)

# --- Gloves -------------------------------------------------------------------------------------------------------
# Each glove is the hand pushed out a little. The faces on the last bone of each finger become a separate
# fingertip cap of grip rubber.
gloves = {}
for s in "lr":
    gfaces = faces_where(in_glove & (ARM[s] > 0.5))
    gp, gq, gids = submesh(gfaces)
    ge = edges_of(gq)
    gb = boundary_mask(len(gp), gq)
    gp = smooth(gp, ge, P["glove_smooth"], fixed=gb)
    gt = thickness_of(W[gids], P["glove_thickness"])
    gp = gp + normals(gp, gq) * gt[:, None]
    gp = smooth(gp, ge, 2, fixed=gb)
    tip_weight = sum(W[gids][:, i] for i, b in enumerate(BONES) if b.endswith("_03_" + s))
    tip_face = tip_weight[gq].mean(axis=1) > P["tip_weight"]
    gloves[s] = finish("Glove_" + s.upper(), gp, gq[~tip_face], "M_Glove", "Suit", smooth_angle=70.0)
    # The caps stand a hair proud of the glove, so the two surfaces do not fight where they meet.
    tips = gp + normals(gp, gq) * 0.0012
    finish("Glove_Tips_" + s.upper(), tips, gq[tip_face], "M_GloveTip", "Suit", smooth_angle=70.0)

# --- Boots --------------------------------------------------------------------------------------------------------
# Each boot is a metaball: a heel ball, a long foot ellipsoid, a toe box and a shaft capsule up the shin, placed from
# the measured foot, and fused into one smooth, puffy boot. The bottom is cut flat onto the sole of
# s30_hardware.py. The suit leg ends inside the shaft.
sole = P["sole"]
B = P["boot"]
FIELD = 0.575  # Measured in Blender 5.2: a lone ball of radius 0.1 has a surface of diameter 0.115.
boots = {}
boot_report = {}
for s in "lr":
    foot = X[(LEG[s] > 0.5) & (z < ankle_z[s])]
    heel_y, toe_y = float(foot[:, 1].max()), float(foot[:, 1].min())
    mid_x = float(foot[:, 0].mean())
    length = heel_y - toe_y
    ball = J[s + "-foot-1"]
    # The shin is sparse in the base mesh, a ring every few centimeters, so its radius is the farthest skin point
    # from the ankle-knee line over the height of the shaft.
    shin = X[(LEG[s] > 0.5) & (z > ankle_z[s] + 0.04) & (z < ankle_z[s] + P["boot_top"])]
    shin_axis = (J[s + "-knee"] - J[s + "-ankle"]) / np.linalg.norm(J[s + "-knee"] - J[s + "-ankle"])
    rel = shin - J[s + "-ankle"]
    shin_r = float(np.linalg.norm(rel - np.outer(rel @ shin_axis, shin_axis), axis=1).max())
    shin_c = J[s + "-ankle"] + shin_axis * (P["boot_top"] / shin_axis[2])
    data = bpy.data.metaballs.new("Boot" + s.upper())
    data.resolution = B["resolution"]
    data.render_resolution = B["resolution"]
    data.threshold = B["threshold"]

    def element(kind, co, radius, semi=None):
        """A metaball element whose lone surface has the radius (BALL, CAPSULE) or semi-axes (ELLIPSOID) given, in
        meters. FIELD is the share of an element's radius where its lone surface lies, at this threshold and
        stiffness."""
        e = data.elements.new(type=kind)
        e.co = tuple(co)
        e.radius = radius / FIELD if semi is None else 1.0
        e.stiffness = B["stiffness"]
        if semi is not None:
            e.size_x, e.size_y, e.size_z = (v / FIELD for v in semi)
        return e

    t = B["thickness"]
    element("BALL", (mid_x, heel_y - 0.05, sole + 0.05), 0.05 + t)
    element("ELLIPSOID", (mid_x, (heel_y + toe_y) / 2, sole + 0.045), None,
            (B["width"] / 2 + t, length / 2 + t * 0.5, 0.045 + t * 0.5))
    element("ELLIPSOID", (ball[0], toe_y + 0.065, sole + 0.035), None, (B["width"] / 2 + t, 0.075, 0.038 + t * 0.4))
    top = np.array((shin_c[0], shin_c[1], ankle_z[s] + P["boot_top"]))
    low = np.array((shin_c[0], heel_y - 0.07, sole + 0.1))
    # A capsule's size_x is the half length of its core along its local X axis, which turns to run from low to top.
    axis = top - low
    e = element("CAPSULE", (top + low) / 2, shin_r + t)
    e.size_x = float(np.linalg.norm(axis)) / 2
    e.rotation = Vector((1.0, 0.0, 0.0)).rotation_difference(Vector(axis / np.linalg.norm(axis)))
    ball_ob = bpy.data.objects.new("Boot" + s.upper(), data)
    bpy.data.collections["Suit"].objects.link(ball_ob)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = ball_ob.evaluated_get(depsgraph)
    me = evaluated.to_mesh()
    bp = np.array([v.co[:] for v in me.vertices])
    polys = [list(p.vertices) for p in me.polygons]
    evaluated.to_mesh_clear()
    bpy.data.objects.remove(ball_ob)
    bpy.data.metaballs.remove(data)
    # Keep what lies below the top of the shaft, flatten the bottom onto the sole.
    keep = [f for f in polys if all(bp[i][2] < top[2] for i in f)]
    bq = np.array([(f + f[-1:])[:4] for f in keep], dtype=np.int64)
    bp[:, 2] = np.maximum(bp[:, 2], sole + 0.004)
    boots[s] = finish("Boot_" + s.upper(), bp, bq, "M_Boot", "Suit", smooth_angle=60.0)
    boot_report[s] = {"shin_r": round(shin_r, 3), "length": round(length, 3), "top": round(float(top[2]), 3),
                      "size": [round(float(v), 3) for v in bp[np.unique(bq)].max(axis=0) - bp[np.unique(bq)].min(axis=0)]}

result = {
    "boots": boot_report,
    "folds": {"bulge_max": round(float(bulge.max()), 4), "bulge_vertices": int((bulge > 0.005).sum()),
              "wrinkle_max": round(float(np.abs(wrinkle).max()), 4)},
    "joints": {k: [round(float(c), 3) for c in J[k]] for k in ("neck", "head", "pelvis", "l-shoulder", "l-elbow",
                                                                "l-hand", "l-knee", "l-ankle", "spine-1")},
    "faces": {"head": len(head.data.polygons), "suit": len(suit.data.polygons),
              "gloves": [len(g.data.polygons) for g in gloves.values()],
              "boots": [len(b.data.polygons) for b in boots.values()]},
}

# Body stays in the scene for the later scripts to measure, but it lies inside the shells and never renders.
# s80_assemble.py deletes it.
body.hide_render = True
