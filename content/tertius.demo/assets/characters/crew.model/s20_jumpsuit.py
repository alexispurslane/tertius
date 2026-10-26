# The jumpsuit and the boots, fitted to the body of s10_human.py. Nothing here comes from a garment asset: each
# piece is a set of faces of Body, chosen by the bone weights of its vertices and by the joints, then smoothed so that
# anatomy goes away and pushed out along its normals.
#
#   Jumpsuit   the torso, the sleeves to the wrists and the legs into the boots, with soft folds at the elbows, the
#              knees and the waist. A baked material (s70_materials.py).
#   Trims      the collar, the cuffs and the pockets, made solid by a Solidify modifier. A baked material of its own.
#              Rays cast at the jumpsuit place them: a ring of rays around an axis for a band (ring()), a grid of
#              rays for a pocket (patch()), so the edges are straight and the cloth under them sets their shape.
#   Belt, Buckle   a band around the waist, made the same way, and a metal buckle.
#   Zipper     a tube down the front from the collar to the crotch, and its pull.
#   Boot_L, Boot_R, Soles, Boot_Tops   a metaball boot on each foot, flat on the ground, with a rubber rand and a
#              padded band around the top of the shaft.
#
# The body faces that the jumpsuit and the boots cover go away, so the skin never shows through the cloth. The faces
# along each opening stay, under the collar, the cuffs and the boot tops.
import json
import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from tertius_blender.geometry import MB

P = model.params["suit"]
body = bpy.data.objects["Body"]
J = {k: np.array(v) for k, v in json.loads(bpy.context.scene["crew_joints"]).items()}
X = np.array([v.co[:] for v in body.data.vertices])
F_ALL = [list(p.vertices) for p in body.data.polygons]
BONES = [g.name for g in body.vertex_groups]
W = np.zeros((len(X), len(BONES)))
for v in body.data.vertices:
    for g in v.groups:
        W[v.index, g.group] = g.weight
M = bpy.data.materials


def bone_weight(prefixes, weights=W):
    cols = [i for i, b in enumerate(BONES) if b.startswith(tuple(prefixes))]
    return weights[:, cols].sum(axis=1)


FINGERS = [f + "_0" + str(k) for f in ("index", "middle", "ring", "pinky", "thumb") for k in (1, 2, 3)]
HAND = {s: bone_weight(["hand_" + s] + [f + "_" + s for f in FINGERS]) for s in "lr"}
ARM = {s: bone_weight(["upperarm_" + s, "lowerarm_" + s]) + HAND[s] for s in "lr"}
LEG = {s: bone_weight(["thigh_" + s, "calf_" + s, "foot_" + s, "ball_" + s]) for s in "lr"}
TRUNK = ("spine", "pelvis", "clavicle", "neck")


def along(points, a, b):
    """The position of each point along the segment from joint a to joint b: 0 at a, 1 at b."""
    d = J[b] - J[a]
    return (points - J[a]) @ d / (d @ d)


# --- Numpy mesh helpers, as in the astronaut ---------------------------------------------------------------------
def submesh(face_ids):
    """The faces `face_ids` of Body as a local mesh: (positions, quads (F, 4), Body vertex ids). A triangle repeats
    its last vertex."""
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


def normals(pos, quads):
    fn = np.cross(pos[quads[:, 2]] - pos[quads[:, 0]], pos[quads[:, 3]] - pos[quads[:, 1]])
    vn = np.zeros_like(pos)
    for k in range(4):
        np.add.at(vn, quads[:, k], fn)
    return vn / np.maximum(np.linalg.norm(vn, axis=1)[:, None], 1e-12)


def smooth(pos, edges, iterations, lam=0.5, mu=-0.53, fixed=None, weight=None, quads=None):
    """Taubin smoothing, which keeps the size. mu = 0 gives plain Laplacian smoothing, which shrinks. `weight` scales
    each vertex's step. With `quads`, each step moves a vertex only along its normal."""
    count = np.zeros(len(pos))
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


def subdivide(pos, quads, values):
    """Splits each face into quads at its edge midpoints and its center. `values` (rows per vertex) follow."""
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
            out.append([a, n + key[(min(a, b), max(a, b))], c, n + key[(min(z, a), max(z, a))]])
    return (np.concatenate([pos, mid, np.array(centers)]), np.array(out, dtype=np.int64),
            np.concatenate([values, mid_v, np.array(center_v)]))


def finish(name, pos, quads, material, smooth_angle=60.0, solidify=0.0):
    mb = MB()
    vs = {}
    for q in quads:
        ring = list(dict.fromkeys(q.tolist()))
        for i in ring:
            if i not in vs:
                vs[i] = mb.bm.verts.new(tuple(pos[i]))
        mb.bm.faces.new([vs[i] for i in ring])
    ob = mb.finish(name, M[material], "Suit", smooth_angle=smooth_angle)
    if solidify:
        # A band or a patch of cloth gets its thickness from Solidify, applied at once, inward from its face.
        mod = ob.modifiers.new("Solidify", "SOLIDIFY")
        mod.thickness = solidify
        mod.offset = -1.0
        mod.use_even_offset = True
        depsgraph = bpy.context.evaluated_depsgraph_get()
        mesh = bpy.data.meshes.new_from_object(ob.evaluated_get(depsgraph), depsgraph=depsgraph)
        old = ob.data
        ob.modifiers.clear()
        ob.data = mesh
        bpy.data.meshes.remove(old)
        mesh.name = name
    return ob


def thickness_of(weights, table):
    total = np.zeros(len(weights))
    norm = np.zeros(len(weights))
    for prefix, t in table.items():
        w = bone_weight([prefix], weights)
        total += w * t
        norm += w
    return total / np.maximum(norm, 1e-6)


def faces_where(test):
    return [f for f, vs in enumerate(F_ALL) if all(test[i] for i in vs)]


# --- Regions of the body ------------------------------------------------------------------------------------------
z = X[:, 2]
neck_cut = J["neck"][2] + P["neck_cut"]
t_arm = {s: along(X, s + "-elbow", s + "-hand") for s in "lr"}
ankle_z = {s: J[s + "-ankle"][2] for s in "lr"}
in_hand = np.zeros(len(X), dtype=bool)
in_foot = np.zeros(len(X), dtype=bool)
in_boot = {}
for s in "lr":
    in_hand |= (ARM[s] > 0.5) & (t_arm[s] > P["sleeve_end"])
    in_foot |= (LEG[s] > 0.5) & (z < ankle_z[s] + P["leg_end"])
    in_boot[s] = (LEG[s] > 0.5) & (z < ankle_z[s] + P["boot_top"])

# --- Jumpsuit -----------------------------------------------------------------------------------------------------
suit_faces = faces_where((z < neck_cut) & ~in_hand & ~in_foot)
sp, sq, sids = submesh(suit_faces)
sw = W[sids]
# Cloth hangs over the hollows of the torso: plain Laplacian smoothing on the trunk before the subdivision. It
# shrinks what it smooths, so it leaves the neck alone, whose thin tube it would pull together.
trunk = np.clip(bone_weight(("spine", "pelvis", "clavicle"), sw), 0.0, 1.0)
sp = smooth(sp, edges_of(sq), P["torso_smooth"], mu=0.0, fixed=boundary_mask(len(sp), sq), weight=trunk)
sp, sq, sw = subdivide(sp, sq, sw)
se = edges_of(sq)
sb = boundary_mask(len(sp), sq)
sp = smooth(sp, se, P["suit_smooth"], fixed=sb)
sp = sp + normals(sp, sq) * thickness_of(sw, P["suit_thickness"])[:, None]
sp = smooth(sp, se, P["suit_smooth_after"], fixed=sb)

# Folds: soft rings around each elbow and knee, and seeded waves across each limb segment and the waist.
rng = np.random.default_rng(model.rng.getrandbits(64))
fold = P["folds"]
n_suit = normals(sp, sq)
offset = np.zeros(len(sp))
for s in "lr":
    for a, b, c, bones in (("shoulder", "elbow", "hand", ("upperarm_", "lowerarm_")),
                           ("upper-leg", "knee", "ankle", ("thigh_", "calf_"))):
        a, b, c = s + "-" + a, s + "-" + b, s + "-" + c
        for j0, j1, bone in ((a, b, bones[0] + s), (b, c, bones[1] + s)):
            m = np.clip(bone_weight([bone], sw), 0.0, 1.0)
            axis = J[j1] - J[j0]
            length = np.linalg.norm(axis)
            d = (sp - J[j0]) @ (axis / length)
            pitch = rng.uniform(*fold["pitch"])
            p1, p2 = rng.uniform(0, 2 * math.pi, size=2)
            rel = sp - J[j0]
            theta = np.arctan2(rel[:, 0], -rel[:, 1])
            wave = np.sin(2 * math.pi * d / pitch + fold["bend"] * np.sin(2 * theta + p1) + p2)
            # Folds gather near the joint at the far end of each segment.
            near = np.clip(d / length, 0.0, 1.0) ** 2 if j1 == b else np.clip(1.0 - d / length, 0.0, 1.0) ** 2
            offset += m * fold["depth"] * wave * (0.35 + 0.65 * near)
waist = np.exp(-((sp[:, 2] - (J["pelvis"][2] + P["belt"]["above_pelvis"])) / fold["waist_band"]) ** 2)
theta = np.arctan2(sp[:, 0], -sp[:, 1])
sw_trunk = np.clip(bone_weight(TRUNK, sw), 0.0, 1.0)
offset += sw_trunk * waist * fold["waist_depth"] * np.sin(theta * fold["waist_waves"] + rng.uniform(0, 2 * math.pi))
sp = sp + n_suit * offset[:, None]
# Where a limb meets the trunk, the offset surfaces cross. Smoothing along the normals there irons them out.
junction = np.clip(4.0 * sw_trunk * (1.0 - sw_trunk) * P["junction_gain"], 0.0, 1.0)
sp = smooth(sp, se, P["junction_smooth"], fixed=sb, weight=junction, quads=sq)
suit = finish("Jumpsuit", sp, sq, "M_Suit", smooth_angle=70.0)
n_suit = normals(sp, sq)


def ring(tree, origin, axis, rows, segments, reach, tilt=0.0):
    """A closed band around an axis that follows a surface. Each row is (t, out, cast). A cast row sends rays from
    origin + t * axis out across the axis at `segments` angles, tilted back along the axis by `tilt` degrees, and
    each point is the first hit on the surface of `tree` within `reach`, pushed out by `out`. In any other row, and
    where a later ray misses, a point reuses the point of the row before, moved along the axis, so a band can reach
    over the open edge of the surface. A miss in the first row is filled in from its neighbors. Returns (positions,
    quads)."""
    axis = np.asarray(axis, dtype=float) / np.linalg.norm(axis)
    u = np.cross(axis, (0.0, -1.0, 0.0) if abs(axis[1]) < 0.9 else (1.0, 0.0, 0.0))
    u /= np.linalg.norm(u)
    v = np.cross(axis, u)
    dirs = [math.cos(2.0 * math.pi * j / segments) * u + math.sin(2.0 * math.pi * j / segments) * v
            for j in range(segments)]
    rays = [math.cos(math.radians(tilt)) * d - math.sin(math.radians(tilt)) * axis for d in dirs]
    rows_pts = []
    for k, (t, out, cast) in enumerate(rows):
        row = []
        for j, d in enumerate(dirs):
            hit = tree.ray_cast(Vector(origin + t * axis), Vector(rays[j]), reach)[0] if cast else None
            if hit is not None:
                row.append(np.array(hit[:]) + d * out)
            elif k == 0:
                row.append(None)
            else:
                t0, out0, _ = rows[k - 1]
                row.append(rows_pts[k - 1][j] + (t - t0) * axis + d * (out - out0))
        if k == 0:
            row = _fill_gaps(row)
        rows_pts.append(row)
    pos = np.array([p for row in rows_pts for p in row])
    quads = []
    for k in range(len(rows) - 1):
        for j in range(segments):
            a, b = k * segments + j, k * segments + (j + 1) % segments
            quads.append([a, b, b + segments, a + segments])
    return pos, np.array(quads, dtype=np.int64)


def _fill_gaps(row):
    """The points of a ring's first row where the ray missed, filled in between the nearest hits on either side."""
    hits = [j for j, p in enumerate(row) if p is not None]
    if len(hits) < len(row) // 2:
        raise ValueError("the first row of a ring misses the surface at {:d} of {:d} angles".format(
            len(row) - len(hits), len(row)))
    n = len(row)
    out = list(row)
    for j, p in enumerate(row):
        if p is None:
            back = next(k for k in range(1, n) if row[(j - k) % n] is not None)
            ahead = next(k for k in range(1, n) if row[(j + k) % n] is not None)
            w = back / (back + ahead)
            out[j] = (1 - w) * row[(j - back) % n] + w * row[(j + ahead) % n]
    return out


def patch(tree, center, inward, up, size, out, n):
    """A rectangle of cloth of `size` (width, height) in meters, centered where a ray along `inward` through
    `center` meets the surface of `tree`, with its height along `up`, pushed out by `out`. Returns (positions,
    quads)."""
    inward = np.asarray(inward, dtype=float) / np.linalg.norm(inward)
    up = np.asarray(up, dtype=float)
    up = up - (up @ inward) * inward
    up /= np.linalg.norm(up)
    across = np.cross(up, inward)
    pts = []
    for b in np.linspace(-size[1] / 2, size[1] / 2, n + 1):
        for a in np.linspace(-size[0] / 2, size[0] / 2, n + 1):
            start = center + a * across + b * up - inward * 0.3
            hit = tree.ray_cast(Vector(start), Vector(inward), 0.6)[0]
            if hit is None:
                raise ValueError("a pocket ray misses the jumpsuit at {}".format(tuple(np.round(start, 3))))
            pts.append(np.array(hit[:]) - inward * out)
    quads = [[k * (n + 1) + j, k * (n + 1) + j + 1, (k + 1) * (n + 1) + j + 1, (k + 1) * (n + 1) + j]
             for k in range(n) for j in range(n)]
    return np.array(pts), np.array(quads, dtype=np.int64)


suit_tree = BVHTree.FromPolygons([tuple(p) for p in sp], [list(dict.fromkeys(q.tolist())) for q in sq])
seg = P["ring_segments"]
trims = []
# Collar: a band around the neck opening that rises past the cut edge and flares out a little.
c = P["collar"]
# The rays run level from the middle of the neck, so none leaves through the opening above the cut.
neck_axis = np.array((0.0, 0.0, 1.0))
rim = sp[np.abs(sp[:, 2] - (neck_cut - c["below"])) < 0.01]
rim = rim[np.abs(rim[:, 0]) < 0.1]
neck_origin = np.array((0.0, (rim[:, 1].min() + rim[:, 1].max()) / 2, neck_cut))
trims.append(ring(suit_tree, neck_origin, neck_axis,
                  [(-c["below"], c["out"], True), (-c["edge"], c["out"], True),
                   (c["above"], c["out"] + c["flare"], False)], seg, c["reach"], c["tilt"]))
# Cuffs: a band around the end of each sleeve, reaching a little past it.
cf = P["cuff"]
for s in "lr":
    fore = J[s + "-hand"] - J[s + "-elbow"]
    end = J[s + "-elbow"] + P["sleeve_end"] * fore
    unit = fore / np.linalg.norm(fore)
    trims.append(ring(suit_tree, end, unit,
                      [(-cf["length"], cf["out"], True), (-cf["edge"], cf["out"], True),
                       (cf["past"], cf["out"], False)],
                      seg, cf["reach"]))
# Pockets: a patch on each side of the chest, and one on the outside of each thigh. Each is a grid of rays that hit
# the cloth from outside, so its edges are straight and the patch follows the cloth.
pk = P["pockets"]
for sign in (1, -1):
    center = np.array((sign * pk["chest_x"], J["spine-1"][1], J["spine-1"][2] + pk["chest_dz"]))
    trims.append(patch(suit_tree, center, (0.0, 1.0, 0.0), (0.0, 0.0, 1.0), pk["chest_size"], pk["out"], 8))
for s, sign in (("l", 1), ("r", -1)):
    hip, knee = J[s + "-upper-leg"], J[s + "-knee"]
    center = hip + pk["thigh_t"] * (knee - hip)
    trims.append(patch(suit_tree, center, (-sign, 0.0, 0.0), hip - knee,
                       pk["thigh_size"], pk["out"], 8))
# One mesh for the collar, the cuffs and the pockets, made solid inward from its face.
tp, tq, count = [], [], 0
for pos, quads in trims:
    tp.append(pos)
    tq.append(quads + count)
    count += len(pos)
trim_ob = finish("Trims", np.concatenate(tp), np.concatenate(tq), "M_Trims", smooth_angle=60.0,
                 solidify=P["trim_thickness"])
# Belt: a band around the waist, with a buckle in front.
belt_z = J["pelvis"][2] + P["belt"]["above_pelvis"]
bh = P["belt"]["height"] / 2
belt_origin = np.array((J["pelvis"][0], J["pelvis"][1], belt_z))
bpos, bquads = ring(suit_tree, belt_origin, (0.0, 0.0, 1.0),
                    [(-bh, P["belt"]["out"], True), (0.0, P["belt"]["out"], True), (bh, P["belt"]["out"], True)], seg,
                    P["belt"]["reach"])
belt = finish("Belt", bpos, bquads, "M_Belt", smooth_angle=60.0, solidify=P["belt"]["thickness"])
front_belt = bpos[np.abs(bpos[:, 0]) < 0.02]
buckle_y = float(front_belt[:, 1].min()) if len(front_belt) else float(J["pelvis"][1] - 0.12)
buckle = MB()
buckle.box((0.0, buckle_y - 0.004, belt_z), (0.06, 0.012, P["belt"]["height"] + 0.008), bevel=0.003)
buckle.finish("Buckle", M["M_Buckle"], "Suit", smooth_angle=40.0)

# Zipper: down the front middle, from the collar to the crotch, just proud of the cloth.
zp = P["zipper"]
front = (np.abs(sp[:, 0]) < 0.012) & (sp[:, 1] < J["pelvis"][1]) & (sw_trunk > 0.5)
lo_z = J["pelvis"][2] + zp["below_pelvis"]
points = []
for zz in np.linspace(neck_cut - 0.01, lo_z, zp["samples"]):
    near = front & (np.abs(sp[:, 2] - zz) < 0.012)
    if near.any():
        k = np.argmin(sp[near][:, 1])
        p = sp[near][k] + n_suit[near][k] * zp["out"]
        points.append((0.0, float(p[1]), float(zz)))
zipper = MB()
zipper.tube(points, zp["radius"], segs=8, bend=0.02)
pull = points[1]
zipper.box((pull[0], pull[1] - 0.004, pull[2] - 0.012), (0.008, 0.004, 0.022), bevel=0.0015)
zipper.finish("Zipper", M["M_Zip"], "Suit", smooth_angle=50.0)

# --- Boots --------------------------------------------------------------------------------------------------------
# Each boot is a metaball, as the astronaut's: a heel ball, a long foot ellipsoid, a toe box and a shaft capsule up
# the shin, placed from the measured foot and fused into one smooth boot, so no toe shows. The bottom is cut flat
# onto the ground, a rubber rand runs around it, and a padded band closes the top over the jumpsuit leg.
B = P["boot"]
FIELD = 0.575  # Measured in Blender 5.2: a lone ball of radius 0.1 has a surface of diameter 0.115.
boot_report = {}
soles = MB()
boot_tops = []
for s in "lr":
    foot = X[(LEG[s] > 0.5) & (z < ankle_z[s])]
    heel_y, toe_y = float(foot[:, 1].max()), float(foot[:, 1].min())
    mid_x = float(foot[:, 0].mean())
    length = heel_y - toe_y
    ball = J[s + "-foot-1"]
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
        meters."""
        e = data.elements.new(type=kind)
        e.co = tuple(co)
        e.radius = radius / FIELD if semi is None else 1.0
        e.stiffness = B["stiffness"]
        if semi is not None:
            e.size_x, e.size_y, e.size_z = (v / FIELD for v in semi)
        return e

    sole = P["sole"]
    t = B["thickness"]
    element("BALL", (mid_x, heel_y - 0.045, sole + 0.045), 0.045 + t)
    element("ELLIPSOID", (mid_x, (heel_y + toe_y) / 2, sole + 0.04), None,
            (B["width"] / 2 + t, length / 2 + t * 0.5, 0.04 + t * 0.5))
    element("ELLIPSOID", (ball[0], toe_y + 0.06, sole + 0.03), None, (B["width"] / 2 + t * 0.8, 0.065, 0.033 + t * 0.4))
    top = np.array((shin_c[0], shin_c[1], ankle_z[s] + P["boot_top"]))
    low = np.array((shin_c[0], heel_y - 0.065, sole + 0.09))
    axis = top - low
    # The shaft holds the jumpsuit leg: its radius is the farthest point of the leg from the shaft's axis, so the
    # cloth never pokes out of the boot behind the calf.
    unit = axis / np.linalg.norm(axis)
    leg = sp[(sp[:, 2] < top[2]) & (sp[:, 2] > low[2]) & (np.abs(sp[:, 0] - shin_c[0]) < 0.12)]
    rel = leg - low
    leg_r = float(np.linalg.norm(rel - np.outer(rel @ unit, unit), axis=1).max())
    e = element("CAPSULE", (top + low) / 2, leg_r + t)
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
    keep = [f for f in polys if all(bp[i][2] < top[2] for i in f)]
    bq = np.array([(f + f[-1:])[:4] for f in keep], dtype=np.int64)
    bp[:, 2] = np.maximum(bp[:, 2], 0.0)
    boot = finish("Boot_" + s.upper(), bp, bq, "M_Boot", smooth_angle=60.0)
    # The rand: the faces of the boot near the ground, pushed out a little.
    nb = normals(bp, bq)
    low_faces = (bp[:, 2] < B["rand_height"])[bq].all(axis=1)
    index = {}
    for q in bq[low_faces]:
        vs = []
        for i in dict.fromkeys(q.tolist()):
            if i not in index:
                p = bp[i] + nb[i] * B["rand_out"]
                index[i] = soles.bm.verts.new((float(p[0]), float(p[1]), float(max(p[2], 0.0))))
            vs.append(index[i])
        soles.bm.faces.new(vs)
    # The padded band at the top of the shaft.
    tree = BVHTree.FromPolygons([tuple(p) for p in bp], [list(dict.fromkeys(q.tolist())) for q in bq])
    tb = B["top_band"]
    boot_tops.append(ring(tree, top - shin_axis * 0.005, shin_axis,
                          [(-tb["height"], tb["out"], True), (-0.012, tb["out"], True),
                           (0.004, tb["out"] + 0.002, False)], seg, reach=0.12))
    lo, hi = bp.min(axis=0), bp.max(axis=0)
    boot_report[s] = {"shin_r": round(shin_r, 3), "leg_r": round(leg_r, 3),
                      "length": round(float(hi[1] - lo[1]), 3),
                      "top": round(float(top[2]), 3)}
soles.finish("Soles", M["M_Sole"], "Suit", smooth_angle=60.0)
tp, tq, count = [], [], 0
for pos, quads in boot_tops:
    tp.append(pos)
    tq.append(quads + count)
    count += len(pos)
finish("Boot_Tops", np.concatenate(tp), np.concatenate(tq), "M_Boot", smooth_angle=60.0,
       solidify=B["top_band"]["thickness"])

# --- The body under the cloth goes away -------------------------------------------------------------------------
# A body face goes when every vertex lies under the jumpsuit or a boot and at least `keep_rings` rings of faces away
# from an opening, so the skin stays under the collar, the cuffs and the boot tops.
covered = ((z < neck_cut) & ~in_hand) | in_boot["l"] | in_boot["r"]
edge_ring = np.zeros(len(X), dtype=bool)
for f in F_ALL:
    vals = [covered[i] for i in f]
    if any(vals) and not all(vals):
        for i in f:
            edge_ring[i] = True
for _ in range(P["keep_rings"]):
    grow = edge_ring.copy()
    for f in F_ALL:
        if any(edge_ring[i] for i in f):
            for i in f:
                grow[i] = True
    edge_ring = grow
hidden = covered & ~edge_ring
bm = bmesh.new()
bm.from_mesh(body.data)
bm.faces.ensure_lookup_table()
gone = [bm.faces[f] for f, vs in enumerate(F_ALL) if all(hidden[i] for i in vs)]
bmesh.ops.delete(bm, geom=gone, context="FACES")
bm.to_mesh(body.data)
bm.free()

result = {
    "faces": {"jumpsuit": len(suit.data.polygons), "trims": len(trim_ob.data.polygons),
              "belt": len(belt.data.polygons), "body_left": len(body.data.polygons),
              "body_removed": len(gone)},
    "boots": boot_report, "zipper_points": len(points),
    "folds_max_m": round(float(np.abs(offset).max()), 4),
}
