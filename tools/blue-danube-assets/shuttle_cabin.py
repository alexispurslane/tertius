"""Tool code. Fits the playable Lunar Shuttle cabin, real window openings, starboard boarding doorway, stowable hatch
door and textured PBR cabin interior directly to Pioneer's lunar_shuttle_hi.dae geometry (ter-tiei,
https://github.com/alexispurslane/tertius/wiki/blue-danube-fidelity).

Runs as the overlay of content/tertius.demo/assets/blue-danube/lunar-shuttle/lunarshuttle.model.import.json, inside
headless Blender in Pioneer's native source axes (+X starboard, +Y nose, +Z up, scale 1.0) before convert.py turns the
scene to engine axes. Every cabin dimension is authored in shuttle axes (ArtScale = 1.6, ArtPlace = (0, 0, 1)) and
mapped to source axes via to_src(), guaranteeing exact agreement with DemoScenes.Shuttle in C#."""

import math
from collections import defaultdict

import bmesh
import bpy
import numpy as np
from mathutils import Vector

import glow

# Must match DemoScenes.Shuttle.ArtScale and DemoScenes.Shuttle.ArtPlace.
ART_SCALE = 1.6
ART_PLACE = np.array([0.0, 0.0, 1.0], dtype=np.float64)

# Fitted cabin layout in shuttle axes (meters, +X starboard, +Y up, -Z nose).
AFT_FLOOR_Y = -1.50
COCKPIT_FLOOR_Y = -0.50
RAMP_START_Z = -3.00
RAMP_END_Z = -8.70
AFT_BULKHEAD_Z = 4.00
NOSE_CONSOLE_Z = -13.50
CEILING_Y = 3.10

# Starboard boarding doorway in shuttle axes (cuts cleanly inside the flat starboard wall panel).
HATCH_Y_MIN = -1.40
HATCH_Y_MAX = 0.80
HATCH_Z_MIN = 2.20
HATCH_Z_MAX = 3.40

# Exact starboard main side wall plane in shuttle axes: n . p = d (measured from fuselage_hi tri 1449/2711).
STBD_WALL_N = np.array([0.94478, 0.24727, -0.21504], dtype=np.float64)
STBD_WALL_D = 4.52231


def to_sh(pt):
    """Pioneer source axes (+X stbd, +Y nose, +Z up) -> shuttle axes (+X stbd, +Y up, -Z nose)."""
    x, y, z = float(pt[0]), float(pt[1]), float(pt[2])
    return np.array([x * ART_SCALE, z * ART_SCALE, -y * ART_SCALE + 1.0], dtype=np.float64)


def to_src(pt):
    """Shuttle axes -> Pioneer source axes."""
    x, y, z = float(pt[0]), float(pt[1]), float(pt[2])
    return (x / ART_SCALE, -(z - 1.0) / ART_SCALE, y / ART_SCALE)


def stbd_outer_x(y_sh, z_sh):
    """Outer starboard hull X (shuttle axes) on the main side panel for (y_sh, z_sh)."""
    return (STBD_WALL_D - STBD_WALL_N[1] * y_sh - STBD_WALL_N[2] * z_sh) / STBD_WALL_N[0]


def cabin_floor_y(z_sh):
    """Walkable deck floor height Y (shuttle axes) along the cabin length."""
    if z_sh >= RAMP_START_Z:
        return AFT_FLOOR_Y
    if z_sh <= RAMP_END_Z:
        return COCKPIT_FLOOR_Y
    t = (RAMP_START_Z - z_sh) / (RAMP_START_Z - RAMP_END_Z)
    return AFT_FLOOR_Y + t * (COCKPIT_FLOOR_Y - AFT_FLOOR_Y)


def cabin_half_width(z_sh, y_sh):
    """Safe interior half-width |X| (shuttle axes) strictly inside fuselage_hi (wall thickness >= 0.24 m)."""
    if z_sh >= -5.0:
        # Follows the linear fuselage taper from Z=-5 to Z=+4.1, inset 0.30 m inside the outer wall plane.
        outer = stbd_outer_x(y_sh, z_sh)
        if y_sh > 1.65:
            # Upper shoulder slopes inward at dX/dY = -0.88 above the crease at Y = 1.65.
            crease = stbd_outer_x(1.65, z_sh)
            outer = crease - 0.88 * (y_sh - 1.65)
        return max(0.35, outer - 0.30)
    if z_sh >= -8.7:
        # Forward transition from mid cabin (Z=-5) to cockpit (Z=-8.7).
        t = (-5.0 - z_sh) / 3.7
        base_at_m5 = stbd_outer_x(min(y_sh, 1.65), -5.0) - 0.30
        base_at_m87 = 2.60 - 0.30 * max(0.0, y_sh + 0.50)
        w = base_at_m5 * (1.0 - t) + base_at_m87 * t
        if y_sh > 1.55:
            w -= 0.72 * (y_sh - 1.55)
        return max(0.35, w)
    # Cockpit nose section (Z=-8.7 down to Z=-13.5).
    t = np.clip((-8.7 - z_sh) / 4.8, 0.0, 1.0)
    floor_w = 2.55 * (1.0 - t) + 1.35 * t
    if y_sh > 0.0:
        floor_w -= (0.45 + 0.25 * t) * y_sh
    return max(0.30, floor_w)


def build(pieces, convert):
    """Cuts real window and doorway openings in `pieces["shuttle"]`, adds opaque hull backs, builds the separate
    `hatch_door` piece and the fitted textured cabin interior, and returns `(added_objects, report)`."""
    shuttle = pieces["shuttle"]
    _make_hull_opaque_double_sided(shuttle)

    windows = _cut_windows(shuttle)
    if len(windows) != 21:
        raise ValueError(f"Expected 21 window openings in lunar_shuttle_hi.dae, found {len(windows)}")

    door_outer_quad, door_skin_tris = _cut_starboard_doorway(shuttle)
    hatch_door = _build_hatch_door(shuttle, door_skin_tris, door_outer_quad)

    mats = _build_cabin_materials()
    cabin_objs = _build_fitted_cabin(windows, door_outer_quad, mats)

    # Sanity-check that no interior cabin vertex protrudes outside the fuselage bounding envelope.
    _check_cabin_bounds(cabin_objs)
    glow_backs = _back_glow_faces(shuttle)

    report = {
        "windowsCut": len(windows),
        "windows": [
            {
                "id": w["id"],
                "normal": [round(float(v), 4) for v in w["normal"]],
                "center": [round(float(v), 4) for v in w["center"]],
                "outer": [[round(float(c), 4) for c in p] for p in w["outer"]],
            }
            for w in windows
        ],
        "doorway": {
            "yRange": [HATCH_Y_MIN, HATCH_Y_MAX],
            "zRange": [HATCH_Z_MIN, HATCH_Z_MAX],
            "outerCorners": [[round(float(c), 4) for c in p] for p in door_outer_quad],
        },
        "glowBacks": glow_backs,
        "cabinObjects": [o.name for o in cabin_objs],
        "hatchDoorPiece": hatch_door.name,
    }
    return [hatch_door, *cabin_objs], report


def _make_hull_opaque_double_sided(shuttle):
    """Ensures every opaque material on the exterior shuttle exports with doubleSided=true so GlbReader.WithBackFaces
    produces opaque wall backs blocking sightlines from inside. The glow bake's copies (`<material>.glowN`) stay
    single-sided: a reversed copy of a glow face would glow into the closed inside of the hull (#20, #47).
    `_back_glow_faces` gives them their opaque backs instead."""
    for mat in shuttle.data.materials:
        if mat is not None and not _is_glow(mat):
            mat.use_backface_culling = False


def _is_glow(material):
    return ".glow" in material.name


def _back_glow_faces(shuttle):
    """Gives every face of a glow-bake material an opaque back that does not glow: a reversed copy of the face on new
    vertices at the same places, with the same UVs, in a single-sided copy of the material that the bake split the
    glow from (`<material>.backing`). The copy is single-sided too, or the reader's reversed copy of it would lie on the
    glow face. Returns the number of backing faces."""
    mesh = shuttle.data
    backing_of_base, backing_of_slot = {}, {}
    for slot, material in enumerate(list(mesh.materials)):
        if material is None or not _is_glow(material):
            continue
        base = material.name.split(".glow")[0]
        if base not in backing_of_base:
            backing = bpy.data.materials[base].copy()
            backing.name = f"{base}.backing"
            backing.use_backface_culling = True
            mesh.materials.append(backing)
            backing_of_base[base] = len(mesh.materials) - 1
        backing_of_slot[slot] = backing_of_base[base]
    if not backing_of_slot:
        return 0
    bm = bmesh.new()
    bm.from_mesh(mesh)
    uv_layers = list(bm.loops.layers.uv.values())
    glow_faces = [f for f in bm.faces if f.material_index in backing_of_slot]
    for face in glow_faces:
        loops = list(reversed(face.loops))
        verts = [bm.verts.new(loop.vert.co) for loop in loops]
        back = bm.faces.new(verts)
        back.material_index = backing_of_slot[face.material_index]
        back.smooth = False
        for new_loop, old_loop in zip(back.loops, loops):
            for layer in uv_layers:
                new_loop[layer].uv = old_loop[layer].uv
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return len(glow_faces)


# ---------------------------------------------------------------------------
# 1. Window detection and cutout (measured directly from source geometry & UV)
# ---------------------------------------------------------------------------


def _face_shuttle_info(face, uv_layer):
    pts = np.array([to_sh(loop.vert.co) for loop in face.loops], dtype=np.float64)
    uvs = np.array([(loop[uv_layer].uv[0], loop[uv_layer].uv[1]) for loop in face.loops], dtype=np.float64)
    n = np.cross(pts[1] - pts[0], pts[2] - pts[0])
    area = 0.5 * np.linalg.norm(n)
    n = n / (2.0 * area) if area > 1e-10 else np.zeros(3)
    return pts, uvs, n, area


def _classify_window_face(pts, uvs, n, area):
    """Returns a window group key if this triangle is one of the 44 real Pioneer glass triangles, else None."""
    c = pts.mean(axis=0)
    uc = uvs.mean(axis=0)
    # 1. Sixteen passenger cabin shoulder windows: UV strip u in [0.725, 0.745], v in [0.06, 0.39], Y in [2.08, 2.48].
    if (
        0.725 <= uc[0] <= 0.745
        and 0.06 <= uc[1] <= 0.39
        and abs(n[0]) > 0.55
        and n[1] > 0.60
        and abs(n[2]) < 0.30
        and pts[:, 1].min() >= 2.08
        and pts[:, 1].max() <= 2.48
    ):
        side = "starboard" if c[0] > 0 else "port"
        return ("side", side)

    # 2. Front cockpit windshield (`pane-nose`): 4 triangles at Z in [-13.75, -11.05], normal ~(0, +0.78, -0.62).
    if (
        area > 0.45
        and abs(n[0]) < 0.08
        and n[1] > 0.70
        and n[2] < -0.55
        and pts[:, 2].min() > -13.80
        and pts[:, 2].max() < -11.00
        and pts[:, 1].min() > -0.15
        and pts[:, 1].max() < 2.05
    ):
        return ("cockpit", "pane-nose")

    # 3. Forward cockpit side windows: 2 triangles per side, Z in [-13.55, -11.05], normal ~(+-0.83, +0.54, -0.14).
    if (
        area > 0.40
        and abs(n[0]) > 0.78
        and 0.45 < n[1] < 0.62
        and -0.22 < n[2] < -0.05
        and pts[:, 2].min() > -13.60
        and pts[:, 2].max() < -11.05
        and pts[:, 1].min() > -0.02
    ):
        return ("cockpit", "pane-cockpit-starboard-fore" if c[0] > 0 else "pane-cockpit-port-fore")

    # 4. Aft cockpit side windows: 2 triangles per side, Z in [-11.20, -9.85], normal ~(+-0.85, +0.50, -0.14).
    if (
        area > 0.20
        and abs(n[0]) > 0.78
        and 0.35 < n[1] < 0.62
        and -0.22 < n[2] < -0.05
        and pts[:, 2].min() > -11.20
        and pts[:, 2].max() < -9.85
        and pts[:, 1].min() > 1.05
    ):
        return ("cockpit", "pane-cockpit-starboard-aft" if c[0] > 0 else "pane-cockpit-port-aft")

    return None


def _ordered_boundary_loop(tris_pts, normal):
    """Extracts the ordered counterclockwise outer polygon corners of a planar cluster of triangles (seen from +normal)."""
    # Collect directed edges by quantized 3D coordinates; interior shared edges cancel in opposite directions.
    def key(p):
        return (round(float(p[0]), 4), round(float(p[1]), 4), round(float(p[2]), 4))

    pos_of = {}
    edges = {}
    for tri in tris_pts:
        for i in range(3):
            a, b = tri[i], tri[(i + 1) % 3]
            ka, kb = key(a), key(b)
            pos_of[ka] = a
            pos_of[kb] = b
            if (kb, ka) in edges:
                del edges[(kb, ka)]
            else:
                edges[(ka, kb)] = True

    nxt = {}
    for ka, kb in edges:
        nxt[ka] = kb
    start = sorted(nxt.keys())[0]
    loop = []
    cur = start
    for _ in range(len(nxt) + 1):
        loop.append(pos_of[cur])
        cur = nxt[cur]
        if cur == start:
            break

    # Remove nearly collinear mid-edge vertices (e.g., center symmetry seam on pane-nose).
    cleaned = []
    m = len(loop)
    for i in range(m):
        prev_p = loop[(i - 1) % m]
        curr_p = loop[i]
        next_p = loop[(i + 1) % m]
        v1 = curr_p - prev_p
        v2 = next_p - curr_p
        l1, l2 = np.linalg.norm(v1), np.linalg.norm(v2)
        if l1 > 1e-6 and l2 > 1e-6:
            cos_a = np.dot(v1, v2) / (l1 * l2)
            if cos_a > 0.9995:
                continue
        cleaned.append(curr_p)

    # Ensure counterclockwise winding looking against `normal` (signed area along normal > 0).
    center = np.mean(cleaned, axis=0)
    signed = 0.0
    for i in range(len(cleaned)):
        a = cleaned[i] - center
        b = cleaned[(i + 1) % len(cleaned)] - center
        signed += float(np.dot(np.cross(a, b), normal))
    if signed < 0:
        cleaned.reverse()
    return cleaned


def _cut_windows(shuttle):
    """Detects and removes all 44 window glass triangles from `shuttle`, returning the 21 ordered opening descriptors."""
    mesh = shuttle.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    uv_layer = bm.loops.layers.uv.active

    cockpit_groups = defaultdict(list)
    side_faces = {"port": [], "starboard": []}
    to_delete = []

    for face in bm.faces:
        if len(face.verts) != 3:
            continue
        pts, uvs, n, area = _face_shuttle_info(face, uv_layer)
        tag = _classify_window_face(pts, uvs, n, area)
        if tag is None:
            continue
        to_delete.append(face)
        if tag[0] == "cockpit":
            cockpit_groups[tag[1]].append((pts, n, area))
        else:
            side_faces[tag[1]].append((pts, n, area))

    windows = []
    # 5 cockpit openings in deterministic order.
    for cid in (
        "pane-nose",
        "pane-cockpit-port-fore",
        "pane-cockpit-starboard-fore",
        "pane-cockpit-port-aft",
        "pane-cockpit-starboard-aft",
    ):
        members = cockpit_groups.get(cid, [])
        if not members:
            raise ValueError(f"Missing cockpit window opening {cid!r}")
        n_avg = np.sum([m[1] * m[2] for m in members], axis=0)
        n_avg /= np.linalg.norm(n_avg)
        loop = _ordered_boundary_loop([m[0] for m in members], n_avg)
        windows.append({"id": cid, "kind": "cockpit", "outer": loop, "normal": n_avg, "center": np.mean(loop, axis=0)})

    # 16 side openings (8 port, 8 starboard), grouped by Z bucket and numbered 1..8 front-to-back.
    for side in ("port", "starboard"):
        buckets = []
        for pts, n, area in side_faces[side]:
            zc = float(pts[:, 2].mean())
            placed = False
            for b in buckets:
                if abs(b["z"] - zc) < 0.5:
                    b["items"].append((pts, n, area))
                    placed = True
                    break
            if not placed:
                buckets.append({"z": zc, "items": [(pts, n, area)]})
        buckets.sort(key=lambda b: b["z"])
        if len(buckets) != 8:
            raise ValueError(f"Expected 8 {side} passenger windows, found {len(buckets)}")
        for idx, b in enumerate(buckets, start=1):
            members = b["items"]
            n_avg = np.sum([m[1] * m[2] for m in members], axis=0)
            n_avg /= np.linalg.norm(n_avg)
            loop = _ordered_boundary_loop([m[0] for m in members], n_avg)
            windows.append(
                {
                    "id": f"pane-{side}-{idx}",
                    "kind": "side",
                    "outer": loop,
                    "normal": n_avg,
                    "center": np.mean(loop, axis=0),
                }
            )

    bmesh.ops.delete(bm, geom=to_delete, context="FACES_ONLY")
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return windows


# ---------------------------------------------------------------------------
# 2. Starboard boarding doorway cutout and separate #hatch_door piece
# ---------------------------------------------------------------------------


def _clip_poly_halfplane(poly, axis_idx, value, keep_greater):
    """Clips a polygon of (pos_sh, uv) pairs against coord[axis_idx] >= value (or <= value)."""
    if not poly:
        return []
    out = []
    n = len(poly)
    for i in range(n):
        a_pos, a_uv = poly[i]
        b_pos, b_uv = poly[(i + 1) % n]
        da = (a_pos[axis_idx] - value) if keep_greater else (value - a_pos[axis_idx])
        db = (b_pos[axis_idx] - value) if keep_greater else (value - b_pos[axis_idx])
        if da >= -1e-7:
            out.append((a_pos, a_uv))
        if (da > 1e-7 and db < -1e-7) or (da < -1e-7 and db > 1e-7):
            t = da / (da - db)
            out.append((a_pos + t * (b_pos - a_pos), a_uv + t * (b_uv - a_uv)))
    return out


def _split_triangle_by_rect(tri_poly, z0, z1, y0, y1):
    """Splits a polygon of (pos_sh, uv) against the axis-aligned rectangle [z0,z1] x [y0,y1] in (Z, Y).
    Returns `(outside_polys, inside_poly)`."""
    outside = []
    rem = tri_poly
    # Strip 1: Z <= z0
    s = _clip_poly_halfplane(rem, 2, z0, False)
    if len(s) >= 3:
        outside.append(s)
    rem = _clip_poly_halfplane(rem, 2, z0, True)
    # Strip 2: Z >= z1
    s = _clip_poly_halfplane(rem, 2, z1, True)
    if len(s) >= 3:
        outside.append(s)
    rem = _clip_poly_halfplane(rem, 2, z1, False)
    # Strip 3: Y <= y0
    s = _clip_poly_halfplane(rem, 1, y0, False)
    if len(s) >= 3:
        outside.append(s)
    rem = _clip_poly_halfplane(rem, 1, y0, True)
    # Strip 4: Y >= y1
    s = _clip_poly_halfplane(rem, 1, y1, True)
    if len(s) >= 3:
        outside.append(s)
    inside = _clip_poly_halfplane(rem, 1, y1, False)
    return outside, inside if len(inside) >= 3 else []


def _cut_starboard_doorway(shuttle):
    """Cuts the exact vertical rectangular doorway [HATCH_Z_MIN..MAX] x [HATCH_Y_MIN..MAX] from the starboard side
    panel of `shuttle`, preserving UVs and normals on the surrounding triangles, and returning the 4 outer corners
    (counterclockwise seen from +X) plus the cut-out skin triangles for the stowable `#hatch_door` piece."""
    mesh = shuttle.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    uv_layer = bm.loops.layers.uv.active

    cut_faces = []
    replacement_tris = []
    door_skin_tris = []

    for face in bm.faces:
        if len(face.verts) != 3:
            continue
        pts, uvs, n, area = _face_shuttle_info(face, uv_layer)
        # Starboard main side panel faces only.
        if pts[:, 0].min() < 2.5 or n[0] < 0.70:
            continue
        if pts[:, 2].max() <= HATCH_Z_MIN + 1e-4 or pts[:, 2].min() >= HATCH_Z_MAX - 1e-4:
            continue
        if pts[:, 1].max() <= HATCH_Y_MIN + 1e-4 or pts[:, 1].min() >= HATCH_Y_MAX - 1e-4:
            continue

        poly = [(pts[i], uvs[i]) for i in range(3)]
        out_polys, in_poly = _split_triangle_by_rect(poly, HATCH_Z_MIN, HATCH_Z_MAX, HATCH_Y_MIN, HATCH_Y_MAX)
        if not in_poly:
            continue
        cut_faces.append(face)
        mat_idx = face.material_index
        for op in out_polys:
            for k in range(1, len(op) - 1):
                replacement_tris.append((mat_idx, [op[0], op[k], op[k + 1]]))
        for k in range(1, len(in_poly) - 1):
            door_skin_tris.append((mat_idx, [in_poly[0], in_poly[k], in_poly[k + 1]]))

    if not cut_faces or not door_skin_tris:
        raise ValueError("Starboard doorway cutout matched no hull triangles")

    bmesh.ops.delete(bm, geom=cut_faces, context="FACES_ONLY")
    for mat_idx, tri in replacement_tris:
        verts = [bm.verts.new(to_src(p)) for p, _ in tri]
        try:
            f = bm.faces.new(verts)
        except ValueError:
            continue
        f.material_index = mat_idx
        for loop, (_, uv) in zip(f.loops, tri):
            loop[uv_layer].uv = (float(uv[0]), float(uv[1]))

    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    # Exact 4 corners of the cut doorway on the outer starboard plane, CCW seen from +X (outside):
    # bottom-aft, bottom-fore, top-fore, top-aft.
    outer_quad = [
        np.array([stbd_outer_x(HATCH_Y_MIN, HATCH_Z_MAX), HATCH_Y_MIN, HATCH_Z_MAX]),
        np.array([stbd_outer_x(HATCH_Y_MIN, HATCH_Z_MIN), HATCH_Y_MIN, HATCH_Z_MIN]),
        np.array([stbd_outer_x(HATCH_Y_MAX, HATCH_Z_MIN), HATCH_Y_MAX, HATCH_Z_MIN]),
        np.array([stbd_outer_x(HATCH_Y_MAX, HATCH_Z_MAX), HATCH_Y_MAX, HATCH_Z_MAX]),
    ]
    return outer_quad, door_skin_tris


def _build_hatch_door(shuttle, door_skin_tris, outer_quad):
    """Builds the separate marked `hatch_door` piece (`tertius_piece = 1`) at the exact model origin so attaching it at
    ArtPlace seals the starboard boarding doorway flush with the hull in flight."""
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")

    # Slot 0: exterior hull material (same PBR hull finish as fuselage_hi), Slot 1: dark metallic inner door frame.
    hull_mat = shuttle.data.materials[door_skin_tris[0][0]]
    inner_mat = _solid_pbr_material("Hatch_Door_Inner", (0.20, 0.22, 0.25), metallic=0.65, roughness=0.42)

    # Outer flush skin triangles (exact cut-out hull triangles + UVs!).
    for _, tri in door_skin_tris:
        verts = [bm.verts.new(to_src(p)) for p, _ in tri]
        f = bm.faces.new(verts)
        f.material_index = 0
        for loop, (_, uv) in zip(f.loops, tri):
            loop[uv_layer].uv = (float(uv[0]), float(uv[1]))

    # Inner insulated backing face (inset 0.14 m along -STBD_WALL_N) + 4 perimeter edge strips.
    inset = -0.14 * STBD_WALL_N
    inner_quad = [p + inset for p in outer_quad]
    # Inner face faces inward (reverse winding of outer_quad).
    in_verts = [bm.verts.new(to_src(p)) for p in reversed(inner_quad)]
    f_in = bm.faces.new(in_verts)
    f_in.material_index = 1
    for loop, uv in zip(f_in.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
        loop[uv_layer].uv = uv

    # 4 perimeter edge faces.
    for i in range(4):
        a0, a1 = outer_quad[i], outer_quad[(i + 1) % 4]
        b0, b1 = inner_quad[i], inner_quad[(i + 1) % 4]
        v = [bm.verts.new(to_src(p)) for p in (a0, b0, b1, a1)]
        fe = bm.faces.new(v)
        fe.material_index = 1
        for loop, uv in zip(fe.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
            loop[uv_layer].uv = uv

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    data = bpy.data.meshes.new("hatch_door")
    bm.to_mesh(data)
    bm.free()
    data.materials.append(hull_mat)
    data.materials.append(inner_mat)
    data.update()
    obj = bpy.data.objects.new("hatch_door", data)
    obj["tertius_piece"] = 1
    bpy.context.scene.collection.objects.link(obj)
    return obj


# ---------------------------------------------------------------------------
# 3. Procedural PBR textures and materials for the fitted cabin interior
# ---------------------------------------------------------------------------


def _packed_image(name, rgba, color_space="sRGB"):
    h, w = rgba.shape[:2]
    img = bpy.data.images.new(name, width=w, height=h, alpha=True)
    img.colorspace_settings.name = color_space
    img.pixels.foreach_set(np.ascontiguousarray(rgba, dtype=np.float32).ravel())
    img.pack()
    return img


def _normal_from_height(height_map, scale=2.0):
    du = (np.roll(height_map, -1, axis=1) - np.roll(height_map, 1, axis=1)) * scale
    dv = (np.roll(height_map, -1, axis=0) - np.roll(height_map, 1, axis=0)) * scale
    nx, ny, nz = -du, -dv, np.ones_like(du, dtype=np.float32)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    out = np.ones((*height_map.shape, 4), dtype=np.float32)
    out[..., 0] = nx * inv * 0.5 + 0.5
    out[..., 1] = ny * inv * 0.5 + 0.5
    out[..., 2] = nz * inv * 0.5 + 0.5
    return out


def _smooth_noise(size, seed, cells):
    """Tileable smooth bilinear value noise on [0, 1] without single-texel Gaussian speckle spikes."""
    rng = np.random.default_rng(seed)
    grid = rng.random((cells, cells), dtype=np.float32)
    coords = np.arange(size, dtype=np.float32) * (cells / size)
    i0 = np.floor(coords).astype(np.int32) % cells
    i1 = (i0 + 1) % cells
    f = coords - np.floor(coords)
    f = f * f * (3.0 - 2.0 * f)
    row0 = grid[i0[:, None], i0[None, :]] * (1.0 - f[None, :]) + grid[i0[:, None], i1[None, :]] * f[None, :]
    row1 = grid[i1[:, None], i0[None, :]] * (1.0 - f[None, :]) + grid[i1[:, None], i1[None, :]] * f[None, :]
    return row0 * (1.0 - f[:, None]) + row1 * f[:, None]


def _wire_pbr_material(name, albedo_rgb, rough_map, metal_map, height_map, normal_strength=0.8, emissive=None):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.use_backface_culling = True
    tree = mat.node_tree
    bsdf = tree.nodes["Principled BSDF"]

    h, w = rough_map.shape
    a_rgba = np.ones((h, w, 4), dtype=np.float32)
    a_rgba[..., :3] = np.clip(albedo_rgb, 0.0, 1.0)
    a_node = tree.nodes.new("ShaderNodeTexImage")
    a_node.image = _packed_image(name + ".albedo", a_rgba, "sRGB")
    tree.links.new(a_node.outputs["Color"], bsdf.inputs["Base Color"])

    mr_rgba = np.ones((h, w, 4), dtype=np.float32)
    mr_rgba[..., 1] = np.clip(rough_map, 0.08, 0.96)
    mr_rgba[..., 2] = np.clip(metal_map, 0.0, 1.0)
    mr_node = tree.nodes.new("ShaderNodeTexImage")
    mr_node.image = _packed_image(name + ".mr", mr_rgba, "Non-Color")
    sep = tree.nodes.new("ShaderNodeSeparateColor")
    tree.links.new(mr_node.outputs["Color"], sep.inputs["Color"])
    tree.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    tree.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])

    n_rgba = _normal_from_height(height_map, scale=normal_strength * 2.5)
    n_node = tree.nodes.new("ShaderNodeTexImage")
    n_node.image = _packed_image(name + ".normal", n_rgba, "Non-Color")
    n_map = tree.nodes.new("ShaderNodeNormalMap")
    n_map.inputs["Strength"].default_value = normal_strength
    tree.links.new(n_node.outputs["Color"], n_map.inputs["Color"])
    tree.links.new(n_map.outputs["Normal"], bsdf.inputs["Normal"])

    if emissive is not None:
        bsdf.inputs["Emission Color"].default_value = (*emissive[0], 1.0)
        bsdf.inputs["Emission Strength"].default_value = float(emissive[1])
    return mat


def _solid_pbr_material(name, rgb, metallic=0.0, roughness=0.5, emissive=None):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.use_backface_culling = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Metallic"].default_value = float(metallic)
    bsdf.inputs["Roughness"].default_value = float(roughness)
    if emissive is not None:
        bsdf.inputs["Emission Color"].default_value = (*emissive[0], 1.0)
        bsdf.inputs["Emission Strength"].default_value = float(emissive[1])
    return mat


def _build_cabin_materials():
    """Creates physically scaled procedural PBR textures (albedo, metallic-roughness, tangent-space normal) for the
    cabin floor tread & fasteners, wall cladding seams & rivets, brushed anodized frames, stippled console, and woven
    stitched upholstery."""
    N_HI = 1024
    N = 512
    yy_h, xx_h = np.mgrid[0:N_HI, 0:N_HI].astype(np.float32) / N_HI
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32) / N
    rng = np.random.default_rng(2001)

    # 1. Cabin_Floor (1024x1024, 1 tile = ~2.0 m): 4x4 removable 50 cm aerospace deck plates with beveled joints,
    #    countersunk corner hex bolts, raised diamond anti-slip tread lugs (~3 cm real pitch), and gritty foot-traffic wear.
    tu = (xx_h * 4.0) % 1.0
    tv = (yy_h * 4.0) % 1.0
    edge_d = np.minimum(np.minimum(tu, 1.0 - tu), np.minimum(tv, 1.0 - tv))
    seam = np.clip(1.0 - edge_d / 0.024, 0.0, 1.0)
    # Smooth 7% perimeter margin around each 50 cm plate, with diamond anti-slip tread inside.
    tread_mask = np.clip((edge_d - 0.065) / 0.025, 0.0, 1.0)
    # Two perpendicular staggered diamond tread sets (alternating 45 deg and -45 deg lugs, ~3.5 cm spacing).
    gu = (tu * 14.0) % 1.0 - 0.5
    gv = (tv * 14.0) % 1.0 - 0.5
    cell_parity = ((np.floor(tu * 14.0) + np.floor(tv * 14.0)) % 2).astype(np.float32)
    lug_a = np.clip(1.0 - (np.abs(gu) / 0.36 + np.abs(gv) / 0.14), 0.0, 1.0) * cell_parity
    lug_b = np.clip(1.0 - (np.abs(gv) / 0.36 + np.abs(gu) / 0.14), 0.0, 1.0) * (1.0 - cell_parity)
    tread = np.maximum(lug_a, lug_b) * tread_mask
    # Countersunk corner bolts (4 per 50 cm deck plate, ~1.8 cm diameter).
    bolt = np.zeros_like(tu)
    for bx in (0.048, 0.952):
        for by in (0.048, 0.952):
            r = np.hypot(tu - bx, tv - by)
            bolt = np.maximum(bolt, np.clip(1.0 - r / 0.018, 0.0, 1.0) - 0.45 * np.clip(1.0 - r / 0.009, 0.0, 1.0))
    scuff = (_smooth_noise(N_HI, 2011, 8) - 0.5) * 0.06
    grain = (_smooth_noise(N_HI, 2001, 24) - 0.5) * 0.04 + (_smooth_noise(N_HI, 2002, 96) - 0.5) * 0.015
    floor_h = 0.55 - 0.38 * seam + 0.30 * tread - 0.20 * bolt + grain * 0.6
    floor_rgb = np.empty((N_HI, N_HI, 3), dtype=np.float32)
    base_f = np.clip(0.34 + 0.055 * tread - 0.12 * seam - 0.05 * bolt + scuff + grain, 0.14, 0.52)
    floor_rgb[..., 0] = base_f * 0.97
    floor_rgb[..., 1] = base_f * 0.99
    floor_rgb[..., 2] = base_f * 1.03
    # Keep non-slip grip coating strictly matte-satin (>= 0.58) above ScreenTrace.MaxRoughness (0.50) to avoid GGX firefly snow.
    floor_rough = np.clip(0.70 - 0.10 * tread + 0.16 * seam - 0.04 * bolt + scuff * 0.5 + grain * 0.7, 0.58, 0.92)
    floor_metal = np.clip(0.10 + 0.12 * tread - 0.08 * seam + 0.32 * bolt, 0.02, 0.45)
    mat_floor = _wire_pbr_material("Cabin_Floor", floor_rgb, floor_rough, floor_metal, floor_h, normal_strength=1.00)

    # 2. Cabin_Wall (1024x1024, 1 tile = ~2.2 m): 2x3 modular weathered composite cladding panels with recessed V-grooves,
    #    perimeter countersunk rivets (~18 cm spacing), subtle quilted contour, and gritty operational scuffing.
    wu = (xx_h * 2.0) % 1.0
    wv = (yy_h * 3.0) % 1.0
    w_edge = np.minimum(np.minimum(wu, 1.0 - wu), np.minimum(wv, 1.0 - wv))
    w_seam = np.clip(1.0 - w_edge / 0.018, 0.0, 1.0)
    quilt = 0.06 * np.sin(wu * math.pi) * np.sin(wv * math.pi)
    # Fastener rows along left/right and top/bottom edges of each composite wall panel.
    near_u = (np.abs(np.minimum(wu, 1.0 - wu) - 0.048) < 0.014) & (wv > 0.08) & (wv < 0.92)
    near_v = (np.abs(np.minimum(wv, 1.0 - wv) - 0.055) < 0.016) & (wu > 0.08) & (wu < 0.92)
    rv_u = np.clip(1.0 - np.abs(((wv * 4.0) % 1.0) - 0.5) / 0.16, 0.0, 1.0) * near_u.astype(np.float32)
    rv_v = np.clip(1.0 - np.abs(((wu * 6.0) % 1.0) - 0.5) / 0.14, 0.0, 1.0) * near_v.astype(np.float32)
    w_rivets = np.maximum(rv_u, rv_v)
    w_grime = (_smooth_noise(N_HI, 2013, 8) - 0.5) * 0.055
    w_grain = (_smooth_noise(N_HI, 2003, 20) - 0.5) * 0.032 + (_smooth_noise(N_HI, 2004, 80) - 0.5) * 0.012
    wall_h = 0.72 - 0.40 * w_seam - 0.16 * w_rivets + quilt + w_grain * 0.5
    wall_rgb = np.empty((N_HI, N_HI, 3), dtype=np.float32)
    base_w = np.clip(0.60 - 0.22 * w_seam - 0.10 * w_rivets + quilt * 0.35 + w_grime + w_grain, 0.24, 0.76)
    wall_rgb[..., 0] = base_w * 1.00
    wall_rgb[..., 1] = base_w * 0.985
    wall_rgb[..., 2] = base_w * 0.95
    wall_rough = np.clip(0.72 + 0.16 * w_seam - 0.12 * w_rivets + w_grime * 0.6 + w_grain * 0.6, 0.56, 0.92)
    wall_metal = np.clip(0.02 + 0.36 * w_rivets, 0.0, 0.40)
    mat_wall = _wire_pbr_material("Cabin_Wall", wall_rgb, wall_rough, wall_metal, wall_h, normal_strength=0.90)

    # 3. Cabin_Frame (512x512): bead-blasted gunmetal structural ribs & ceiling beams with splice seams and pan-head rivets.
    brush = (_smooth_noise(N, 2005, 64) - 0.5) * 0.02 + (_smooth_noise(N, 2006, 16) - 0.5) * 0.03
    fu = (xx * 2.0) % 1.0
    fv = (yy * 4.0) % 1.0
    f_seam = np.clip(1.0 - np.minimum(fu, 1.0 - fu) / 0.025, 0.0, 1.0)
    f_rivet = np.clip(1.0 - np.hypot(fu - 0.12, fv - 0.5) / 0.055, 0.0, 1.0) + np.clip(
        1.0 - np.hypot(fu - 0.88, fv - 0.5) / 0.055, 0.0, 1.0
    )
    frame_h = 0.50 - 0.30 * f_seam + 0.22 * f_rivet + brush * 1.2
    frame_rgb = np.empty((N, N, 3), dtype=np.float32)
    bf = np.clip(0.27 - 0.08 * f_seam + 0.05 * f_rivet + brush * 0.6, 0.12, 0.40)
    frame_rgb[..., 0] = bf * 0.96
    frame_rgb[..., 1] = bf * 0.99
    frame_rgb[..., 2] = bf * 1.04
    frame_rough = np.clip(0.62 + 0.14 * f_seam - 0.04 * f_rivet + brush * 0.8, 0.56, 0.84)
    frame_metal = np.clip(0.48 - 0.20 * f_seam + 0.10 * f_rivet, 0.20, 0.62)
    mat_frame = _wire_pbr_material("Cabin_Frame", frame_rgb, frame_rough, frame_metal, frame_h, normal_strength=0.75)

    # 4. Cabin_Console (512x512): stippled anti-glare charcoal instrument shroud with modular bezel seams & fasteners.
    cu = (xx * 3.0) % 1.0
    cv = (yy * 3.0) % 1.0
    c_seam = np.clip(1.0 - np.minimum(np.minimum(cu, 1.0 - cu), np.minimum(cv, 1.0 - cv)) / 0.022, 0.0, 1.0)
    c_grain = (_smooth_noise(N, 2007, 48) - 0.5) * 0.035
    cons_h = 0.55 - 0.32 * c_seam + c_grain * 0.8
    cons_rgb = np.clip((0.20 - 0.05 * c_seam + c_grain)[..., None] * np.array([1.0, 1.0, 1.04]), 0.10, 0.28)
    mat_console = _wire_pbr_material(
        "Cabin_Console",
        cons_rgb,
        np.clip(np.full((N, N), 0.82, dtype=np.float32) + 0.08 * c_seam + c_grain * 0.5, 0.72, 0.94),
        np.full((N, N), 0.12, dtype=np.float32),
        cons_h,
        normal_strength=0.65,
    )

    # 5. Cabin_Upholstery (512x512): worn navy-charcoal ballistic fabric with quilted bolster seams and stitch dimples.
    # Cross-hatched diagonal twill weave (~4 mm yarn scale) + 4 quilted horizontal ribs + stitched borders.
    twill = 0.5 * (
        np.sin((xx + yy) * 48.0 * math.tau) * np.sin((xx - yy) * 48.0 * math.tau)
        + 0.45 * np.sin(xx * 96.0 * math.tau) * np.sin(yy * 96.0 * math.tau)
    )
    bolster_v = (yy * 4.0) % 1.0
    b_seam = np.clip(1.0 - np.minimum(bolster_v, 1.0 - bolster_v) / 0.04, 0.0, 1.0)
    bolster_curve = 0.18 * np.sin(bolster_v * math.pi)
    stitch = (
        np.clip(1.0 - np.abs(((xx * 32.0) % 1.0) - 0.5) / 0.25, 0.0, 1.0)
        * (np.abs(np.minimum(bolster_v, 1.0 - bolster_v) - 0.065) < 0.015).astype(np.float32)
    )
    u_wear = (_smooth_noise(N, 2015, 10) - 0.5) * 0.04
    u_h = 0.55 + 0.16 * twill + bolster_curve - 0.32 * b_seam - 0.14 * stitch
    u_rgb = np.empty((N, N, 3), dtype=np.float32)
    u_rgb[..., 0] = np.clip(0.20 + 0.022 * twill - 0.06 * b_seam + 0.06 * stitch + u_wear, 0.08, 0.34)
    u_rgb[..., 1] = np.clip(0.24 + 0.026 * twill - 0.07 * b_seam + 0.06 * stitch + u_wear, 0.10, 0.38)
    u_rgb[..., 2] = np.clip(0.32 + 0.032 * twill - 0.09 * b_seam + 0.07 * stitch + u_wear, 0.14, 0.46)
    mat_seat = _wire_pbr_material(
        "Cabin_Upholstery",
        u_rgb,
        np.clip(np.full((N, N), 0.88, dtype=np.float32) + 0.06 * b_seam, 0.80, 0.96),
        np.zeros((N, N), dtype=np.float32),
        u_h,
        normal_strength=0.90,
    )

    # 6. Cabin_Trim: weathered industrial safety amber enamel for threshold & grab handles (matte-satin >= 0.62).
    mat_trim = _solid_pbr_material("Cabin_Trim", (0.68, 0.48, 0.10), metallic=0.10, roughness=0.62)

    # 7. Cabin_Display: backlit cockpit telemetry screen (calm SI-balanced glow).
    mat_display = _solid_pbr_material(
        "Cabin_Display",
        (0.05, 0.14, 0.20),
        metallic=0.05,
        roughness=0.56,
        emissive=((0.18, 0.62, 0.78), 0.25),
    )

    return {
        "floor": mat_floor,
        "wall": mat_wall,
        "frame": mat_frame,
        "console": mat_console,
        "seat": mat_seat,
        "trim": mat_trim,
        "display": mat_display,
    }


# ---------------------------------------------------------------------------
# 4. Mesh builder helper (in shuttle axes, outputs source-axis Blender objects)
# ---------------------------------------------------------------------------


class CabinBuilder:
    """Accumulates quad/tri geometry in shuttle axes with world-scale planar UVs and builds one Blender mesh object."""

    def __init__(self, name, material_list):
        self.name = name
        self.materials = material_list
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")

    def quad(self, pts, mat_idx, uv_scale=0.5, expected_normal=None):
        """Adds one quad (or n-gon) from shuttle-axis points, orienting winding to match `expected_normal` if given."""
        arr = [np.asarray(p, dtype=np.float64) for p in pts]
        n = np.cross(arr[1] - arr[0], arr[2] - arr[0])
        nl = np.linalg.norm(n)
        if nl < 1e-8:
            return
        n /= nl
        if expected_normal is not None and np.dot(n, expected_normal) < 0:
            arr.reverse()
            n = -n
        # Local tangent frame for consistent world-meter UV tiling.
        up_ref = np.array([0.0, 1.0, 0.0]) if abs(n[1]) < 0.9 else np.array([0.0, 0.0, 1.0])
        u_ax = np.cross(up_ref, n)
        u_ax /= np.linalg.norm(u_ax)
        v_ax = np.cross(n, u_ax)
        verts = [self.bm.verts.new(to_src(p)) for p in arr]
        try:
            f = self.bm.faces.new(verts)
        except ValueError:
            return
        f.material_index = mat_idx
        for loop, p in zip(f.loops, arr):
            loop[self.uv].uv = (float(np.dot(p, u_ax) * uv_scale), float(np.dot(p, v_ax) * uv_scale))

    def box(self, mn, mx, mat_idx, uv_scale=0.5):
        """Adds an outward-facing axis-aligned box in shuttle axes."""
        x0, y0, z0 = mn
        x1, y1, z1 = mx
        faces = [
            ([(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], (0, -1, 0)),
            ([(x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0)], (0, 1, 0)),
            ([(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)], (-1, 0, 0)),
            ([(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)], (1, 0, 0)),
            ([(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)], (0, 0, -1)),
            ([(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], (0, 0, 1)),
        ]
        for pts, n in faces:
            self.quad(pts, mat_idx, uv_scale=uv_scale, expected_normal=np.array(n, dtype=np.float64))

    def finish(self):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts[:], dist=1e-4)
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for mat in self.materials:
            mesh.materials.append(mat)
        mesh.update()
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj


# ---------------------------------------------------------------------------
# 5. Build the fitted playable cabin interior
# ---------------------------------------------------------------------------


def _build_fitted_cabin(windows, door_outer_quad, mats):
    """Builds the watertight inner cabin liner, beveled window/doorway reveals, structural ribs, flight deck console,
    command chair, passenger seating, handrails and aft bulkhead inside `fuselage_hi`."""
    mat_list = [
        mats["floor"],    # 0
        mats["wall"],     # 1
        mats["frame"],    # 2
        mats["console"],  # 3
        mats["seat"],     # 4
        mats["trim"],     # 5
        mats["display"],  # 6
    ]
    liner = CabinBuilder("Cabin_Liner", mat_list)
    details = CabinBuilder("Cabin_Details", mat_list)

    _add_deck_and_ramp(liner, details)
    _add_side_walls_and_doorway(liner, details, door_outer_quad)
    _add_upper_shoulder_and_ceiling(liner, windows)
    _add_window_reveals(details, windows)
    _add_cockpit_interior(liner, details)
    _add_structural_ribs_and_bulkhead(details)
    _add_passenger_furnishings(details)

    return [liner.finish(), details.finish()]


def _add_deck_and_ramp(liner, details):
    """Walkable aft cabin floor (Y=-1.50), gentle 10 deg center aisle ramp (Z=-3.0..-8.7), and cockpit deck (Y=-0.50)."""
    # Continuous floor panels from aft bulkhead (Z=+4.0) forward to cockpit nose (Z=-13.5).
    z_stops = [4.0, 2.6, 1.4, -0.5, -3.0, -5.0, -7.0, -8.7, -10.5, -12.0, -13.5]
    for i in range(len(z_stops) - 1):
        za, zb = z_stops[i], z_stops[i + 1]
        ya, yb = cabin_floor_y(za), cabin_floor_y(zb)
        wa, wb = cabin_half_width(za, ya), cabin_half_width(zb, yb)
        # Main walkable deck (facing +Y).
        liner.quad(
            [(-wa, ya, za), (wa, ya, za), (wb, yb, zb), (-wb, yb, zb)],
            mat_idx=0,
            uv_scale=0.6,
            expected_normal=np.array([0.0, 1.0, 0.0]),
        )
        # Safety trim strips along the center aisle edges (|X| = 1.15..1.25) on the ramp & aft cabin.
        if za <= 3.5 and zb >= -10.5:
            for s in (-1.0, 1.0):
                liner.quad(
                    [
                        (s * 1.12, ya + 0.004, za),
                        (s * 1.20, ya + 0.004, za),
                        (s * 1.20, yb + 0.004, zb),
                        (s * 1.12, yb + 0.004, zb),
                    ],
                    mat_idx=5,
                    uv_scale=1.0,
                    expected_normal=np.array([0.0, 1.0, 0.0]),
                )

    # Low anodized ramp handrails flanking the center aisle ramp (Z=-8.5..-3.1, |X|=1.55 m) — clear of walk path!
    for s in (-1.0, 1.0):
        x = s * 1.58
        y0 = cabin_floor_y(-3.1)
        y1 = cabin_floor_y(-8.5)
        # Top rail bar (0.85 m above ramp floor).
        r = 0.025
        for z_post in (-3.2, -5.0, -6.8, -8.4):
            yp = cabin_floor_y(z_post)
            details.box((x - r, yp, z_post - r), (x + r, yp + 0.85, z_post + r), mat_idx=2)
        # Sloped top handrail bar.
        details.quad(
            [(x - r, y0 + 0.85, -3.1), (x + r, y0 + 0.85, -3.1), (x + r, y1 + 0.85, -8.5), (x - r, y1 + 0.85, -8.5)],
            mat_idx=5,
            expected_normal=np.array([0.0, 1.0, 0.0]),
        )
        details.quad(
            [(x - r, y0 + 0.81, -3.1), (x - r, y0 + 0.85, -3.1), (x - r, y1 + 0.85, -8.5), (x - r, y1 + 0.81, -8.5)],
            mat_idx=5,
            expected_normal=np.array([-1.0, 0.0, 0.0]),
        )
        details.quad(
            [(x + r, y0 + 0.81, -3.1), (x + r, y1 + 0.81, -8.5), (x + r, y1 + 0.85, -8.5), (x + r, y0 + 0.85, -3.1)],
            mat_idx=5,
            expected_normal=np.array([1.0, 0.0, 0.0]),
        )


def _add_side_walls_and_doorway(liner, details, door_outer_quad):
    """Tapered inner lower cabin walls (floor to Y=+1.65) with the exact starboard doorway cutout and recessed airlock
    frame connecting inner wall cleanly to the cut outer hull skin."""
    # Main cabin lower side walls (Z=-5.0..+4.0, Y=floor..+1.65) follow the outer sloped wall inset by 0.28 m.
    inset_x = 0.28
    # Port side is continuous from Z=+4.0 to Z=-5.0.
    z_port = [4.0, 2.6, 1.4, -0.5, -3.0, -5.0]
    for i in range(len(z_port) - 1):
        za, zb = z_port[i], z_port[i + 1]
        ya0, yb0 = cabin_floor_y(za), cabin_floor_y(zb)
        y_top = 1.65
        xa0, xb0 = -(stbd_outer_x(ya0, za) - inset_x), -(stbd_outer_x(yb0, zb) - inset_x)
        xa1, xb1 = -(stbd_outer_x(y_top, za) - inset_x), -(stbd_outer_x(y_top, zb) - inset_x)
        liner.quad(
            [(xa0, ya0, za), (xb0, yb0, zb), (xb1, y_top, zb), (xa1, y_top, za)],
            mat_idx=1,
            uv_scale=0.45,
            expected_normal=np.array([1.0, 0.0, 0.0]),
        )

    # Starboard side: skip the doorway rectangle Z in [HATCH_Z_MIN, HATCH_Z_MAX], Y in [HATCH_Y_MIN, HATCH_Y_MAX]!
    stbd_strips = [
        (AFT_BULKHEAD_Z, HATCH_Z_MAX, AFT_FLOOR_Y, 1.65),
        (HATCH_Z_MIN, -0.5, AFT_FLOOR_Y, 1.65),
        (-0.5, -3.0, AFT_FLOOR_Y, 1.65),
        (-3.0, -5.0, None, 1.65),  # follows ramp floor
        # Above doorway lintel (Y=HATCH_Y_MAX..+1.65)
        (HATCH_Z_MAX, HATCH_Z_MIN, HATCH_Y_MAX, 1.65),
        # Below doorway sill (Y=AFT_FLOOR_Y..HATCH_Y_MIN)
        (HATCH_Z_MAX, HATCH_Z_MIN, AFT_FLOOR_Y, HATCH_Y_MIN),
    ]
    for za, zb, y_bot, y_top in stbd_strips:
        ya0 = cabin_floor_y(za) if y_bot is None else y_bot
        yb0 = cabin_floor_y(zb) if y_bot is None else y_bot
        xa0, xb0 = stbd_outer_x(ya0, za) - inset_x, stbd_outer_x(yb0, zb) - inset_x
        xa1, xb1 = stbd_outer_x(y_top, za) - inset_x, stbd_outer_x(y_top, zb) - inset_x
        liner.quad(
            [(xa0, ya0, za), (xa1, y_top, za), (xb1, y_top, zb), (xb0, yb0, zb)],
            mat_idx=1,
            uv_scale=0.45,
            expected_normal=np.array([-1.0, 0.0, 0.0]),
        )

    # Recessed metallic airlock doorframe bridging inner doorway corners seamlessly to `door_outer_quad`!
    inner_quad = [
        np.array([stbd_outer_x(HATCH_Y_MIN, HATCH_Z_MAX) - inset_x, HATCH_Y_MIN, HATCH_Z_MAX]),
        np.array([stbd_outer_x(HATCH_Y_MIN, HATCH_Z_MIN) - inset_x, HATCH_Y_MIN, HATCH_Z_MIN]),
        np.array([stbd_outer_x(HATCH_Y_MAX, HATCH_Z_MIN) - inset_x, HATCH_Y_MAX, HATCH_Z_MIN]),
        np.array([stbd_outer_x(HATCH_Y_MAX, HATCH_Z_MAX) - inset_x, HATCH_Y_MAX, HATCH_Z_MAX]),
    ]
    # 4 reveal faces facing INTO the doorway opening: bottom sill (+Y), fore jamb (+Z), top lintel (-Y), aft jamb (-Z).
    reveal_normals = [
        np.array([0.0, 1.0, 0.0]),
        np.array([0.0, 0.0, 1.0]),
        np.array([0.0, -1.0, 0.0]),
        np.array([0.0, 0.0, -1.0]),
    ]
    for i in range(4):
        o0, o1 = door_outer_quad[i], door_outer_quad[(i + 1) % 4]
        i0, i1 = inner_quad[i], inner_quad[(i + 1) % 4]
        mat_id = 5 if i == 0 else 2  # Amber-striped sill plate on bottom, dark anodized aluminum on jambs & header
        details.quad([o0, o1, i1, i0], mat_idx=mat_id, uv_scale=1.0, expected_normal=reveal_normals[i])

    # Crisp 3D exterior airlock surround collar on the starboard hull around the doorway (never mirrors to port!).
    bw, bh = 0.11, 0.11
    outer_rim = [
        np.array([stbd_outer_x(HATCH_Y_MIN - bh, HATCH_Z_MAX + bw), HATCH_Y_MIN - bh, HATCH_Z_MAX + bw]),
        np.array([stbd_outer_x(HATCH_Y_MIN - bh, HATCH_Z_MIN - bw), HATCH_Y_MIN - bh, HATCH_Z_MIN - bw]),
        np.array([stbd_outer_x(HATCH_Y_MAX + bh, HATCH_Z_MIN - bw), HATCH_Y_MAX + bh, HATCH_Z_MIN - bw]),
        np.array([stbd_outer_x(HATCH_Y_MAX + bh, HATCH_Z_MAX + bw), HATCH_Y_MAX + bh, HATCH_Z_MAX + bw]),
    ]
    collar_out = STBD_WALL_N * 0.018
    for i in range(4):
        q0, q1 = door_outer_quad[i] + collar_out, door_outer_quad[(i + 1) % 4] + collar_out
        r0, r1 = outer_rim[i] + collar_out, outer_rim[(i + 1) % 4] + collar_out
        mat_id = 5 if i == 0 else 2
        details.quad([q0, q1, r1, r0], mat_idx=mat_id, uv_scale=1.0, expected_normal=STBD_WALL_N)
        details.quad([door_outer_quad[i], door_outer_quad[(i + 1) % 4], q1, q0], mat_idx=mat_id, uv_scale=1.0, expected_normal=reveal_normals[i])
        details.quad([r0, r1, outer_rim[(i + 1) % 4], outer_rim[i]], mat_idx=2, uv_scale=1.0, expected_normal=-reveal_normals[i])

    # Interior doorway threshold ramp plate (matches Shuttle.Walkway inside the cabin, 7 deg ramp from X=4.45 to sill).
    sill_in_x = stbd_outer_x(HATCH_Y_MIN, HATCH_Z_MIN) - inset_x
    ramp_x0 = sill_in_x - 0.80
    details.quad(
        [
            (ramp_x0, AFT_FLOOR_Y + 0.002, HATCH_Z_MIN - 0.05),
            (sill_in_x, HATCH_Y_MIN, HATCH_Z_MIN - 0.05),
            (sill_in_x, HATCH_Y_MIN, HATCH_Z_MAX + 0.05),
            (ramp_x0, AFT_FLOOR_Y + 0.002, HATCH_Z_MAX + 0.05),
        ],
        mat_idx=0,
        uv_scale=0.8,
        expected_normal=np.array([0.0, 1.0, 0.0]),
    )
    # Two vertical yellow-accented boarding grab handles just inside the doorway jambs.
    for z_h in (HATCH_Z_MIN - 0.14, HATCH_Z_MAX + 0.14):
        xh = stbd_outer_x(0.0, z_h) - inset_x - 0.09
        details.box((xh - 0.02, -0.55, z_h - 0.02), (xh + 0.02, 0.45, z_h + 0.02), mat_idx=5)

    # Forward lower side walls from Z=-5.0 to Z=-8.7 (below upper cockpit taper).
    for s in (-1.0, 1.0):
        z_fwd = [-5.0, -6.5, -8.7]
        for i in range(len(z_fwd) - 1):
            za, zb = z_fwd[i], z_fwd[i + 1]
            ya0, yb0 = cabin_floor_y(za), cabin_floor_y(zb)
            ya1 = 1.65 if za >= -6.5 else 1.50
            yb1 = 1.65 if zb >= -6.5 else 1.50
            xa0, xb0 = s * cabin_half_width(za, ya0), s * cabin_half_width(zb, yb0)
            xa1, xb1 = s * cabin_half_width(za, ya1), s * cabin_half_width(zb, yb1)
            liner.quad(
                [(xa0, ya0, za), (xb0, yb0, zb), (xb1, yb1, zb), (xa1, ya1, za)],
                mat_idx=1,
                uv_scale=0.45,
                expected_normal=np.array([-s, 0.0, 0.0]),
            )


def _add_upper_shoulder_and_ceiling(liner, windows):
    """Adds the flat ceiling (Y=+3.10) and the upper sloped shoulder wall with exact openings for all 16 passenger
    windows (`pane-port-1..8`, `pane-starboard-1..8`)."""
    # Flat ceiling from Z=+4.0 forward to Z=-6.2 (strictly inside Pioneer's wide dorsal cabin section).
    z_ceil = [4.0, 1.5, -1.5, -4.5, -6.2]
    for i in range(len(z_ceil) - 1):
        za, zb = z_ceil[i], z_ceil[i + 1]
        wa = cabin_half_width(za, CEILING_Y)
        wb = cabin_half_width(zb, CEILING_Y)
        liner.quad(
            [(-wa, CEILING_Y, za), (-wb, CEILING_Y, zb), (wb, CEILING_Y, zb), (wa, CEILING_Y, za)],
            mat_idx=1,
            uv_scale=0.45,
            expected_normal=np.array([0.0, -1.0, 0.0]),
        )

    # Upper sloped shoulder wall (Y=1.65..3.10, Z=-6.5..+4.0) with exact cutouts around the 16 side windows!
    # We tile it cleanly in three horizontal bands: below windows (Y=1.65..2.06), window band (pillars between windows),
    # and above windows (Y=2.48..3.10), so every single passenger window opening stays 100% unobstructed!
    by_side = {"port": [], "starboard": []}
    for w in windows:
        if w["kind"] == "side":
            side = "starboard" if w["center"][0] > 0 else "port"
            by_side[side].append(w)

    for side, sign in (("port", -1.0), ("starboard", 1.0)):
        wins = sorted(by_side[side], key=lambda w: w["center"][2])
        # Band 1: lower shoulder apron Y=1.65..2.05 (continuous along Z=-6.5..+4.0)
        z_apron = [-6.5, -5.0, 4.0]
        for i in range(len(z_apron) - 1):
            za, zb = z_apron[i], z_apron[i + 1]
            p00 = (sign * cabin_half_width(za, 1.65), 1.65, za)
            p10 = (sign * cabin_half_width(zb, 1.65), 1.65, zb)
            p11 = (sign * cabin_half_width(zb, 2.05), 2.05, zb)
            p01 = (sign * cabin_half_width(za, 2.05), 2.05, za)
            liner.quad([p00, p10, p11, p01], mat_idx=1, uv_scale=0.5, expected_normal=np.array([-sign, -0.8, 0.0]))

        # Band 3: upper shoulder header Y=2.50..3.10 (continuous along Z=-6.5..+4.0)
        for i in range(len(z_apron) - 1):
            za, zb = z_apron[i], z_apron[i + 1]
            p00 = (sign * cabin_half_width(za, 2.50), 2.50, za)
            p10 = (sign * cabin_half_width(zb, 2.50), 2.50, zb)
            p11 = (sign * cabin_half_width(zb, CEILING_Y), CEILING_Y, zb)
            p01 = (sign * cabin_half_width(za, CEILING_Y), CEILING_Y, za)
            liner.quad([p00, p10, p11, p01], mat_idx=1, uv_scale=0.5, expected_normal=np.array([-sign, -0.8, 0.0]))

        # Band 2: solid pillars BETWEEN the 8 windows (Y=2.05..2.50), leaving exact clear holes at each window's Z span!
        z_prev = -6.5
        for w in wins:
            zs = [p[2] for p in w["outer"]]
            wz0, wz1 = min(zs) - 0.02, max(zs) + 0.02
            if wz0 > z_prev + 0.02:
                p00 = (sign * cabin_half_width(z_prev, 2.05), 2.05, z_prev)
                p10 = (sign * cabin_half_width(wz0, 2.05), 2.05, wz0)
                p11 = (sign * cabin_half_width(wz0, 2.50), 2.50, wz0)
                p01 = (sign * cabin_half_width(z_prev, 2.50), 2.50, z_prev)
                liner.quad([p00, p10, p11, p01], mat_idx=1, uv_scale=0.5, expected_normal=np.array([-sign, -0.8, 0.0]))
            z_prev = wz1
        if 4.0 > z_prev + 0.02:
            p00 = (sign * cabin_half_width(z_prev, 2.05), 2.05, z_prev)
            p10 = (sign * cabin_half_width(4.0, 2.05), 2.05, 4.0)
            p11 = (sign * cabin_half_width(4.0, 2.50), 2.50, 4.0)
            p01 = (sign * cabin_half_width(z_prev, 2.50), 2.50, z_prev)
            liner.quad([p00, p10, p11, p01], mat_idx=1, uv_scale=0.5, expected_normal=np.array([-sign, -0.8, 0.0]))


def _add_window_reveals(details, windows):
    """Adds deep dark anodized beveled window frame reveals around all 21 cut window openings so exterior hull skin and
    interior cabin liner meet seamlessly with zero leaks and zero occlusion of any opening."""
    for w in windows:
        outer = w["outer"]
        n = w["normal"]
        center = w["center"]
        depth = 0.14 if w["kind"] == "cockpit" else 0.24
        # Slightly expand inner reveal rim outward from window center (1-2 cm) so the reveal flares cleanly into the cabin.
        inner = []
        for p in outer:
            radial = p - center
            rl = np.linalg.norm(radial)
            flare = (radial / rl) * 0.015 if rl > 1e-6 else np.zeros(3)
            inner.append(p - n * depth + flare)
        m = len(outer)
        for i in range(m):
            o0, o1 = outer[i], outer[(i + 1) % m]
            i0, i1 = inner[i], inner[(i + 1) % m]
            # Face normal points INWARD toward the window center!
            mid = 0.25 * (o0 + o1 + i0 + i1)
            to_center = center - mid
            details.quad([o0, o1, i1, i0], mat_idx=2, uv_scale=1.2, expected_normal=to_center)


def _add_cockpit_interior(liner, details):
    """Builds the flight deck wrap-around instrument console, matte anti-glare glareshield, backlit displays, overhead
    cockpit ceiling panel, solid cockpit lower walls, and the pilot's contoured command chair."""
    # 1. Sloped cockpit ceiling behind the top of the front windshield, fitted strictly inside Pioneer's narrow dorsal
    #    nose ridge (which narrows to |X|=0.408 at Z=-8.0..-7.5 and widens to |X|=2.38 at Z=-6.2).
    z_roof = [-11.05, -9.8, -8.2, -7.2, -6.2]
    y_roof = [1.92, 2.24, 2.56, 2.82, CEILING_Y]
    w_roof = [0.32, 0.35, 0.34, 0.55, 1.25]
    for i in range(len(z_roof) - 1):
        liner.quad(
            [
                (-w_roof[i], y_roof[i], z_roof[i]),
                (w_roof[i], y_roof[i], z_roof[i]),
                (w_roof[i + 1], y_roof[i + 1], z_roof[i + 1]),
                (-w_roof[i + 1], y_roof[i + 1], z_roof[i + 1]),
            ],
            mat_idx=1,
            uv_scale=0.5,
            expected_normal=np.array([0.0, -1.0, 0.3]),
        )
        # Transition shoulder cheek panels behind the aft cockpit side windows (Z=-9.8..-6.2), hugging strictly inside hull.
        if i >= 1:
            for s in (-1.0, 1.0):
                z0, z1 = z_roof[i], z_roof[i + 1]
                y0_t, y1_t = y_roof[i], y_roof[i + 1]
                w0_t, w1_t = s * w_roof[i], s * w_roof[i + 1]
                y0_b = 1.25 if z0 < -8.0 else 1.50
                y1_b = 1.25 if z1 < -8.0 else 1.50
                w0_b = s * min(cabin_half_width(z0, y0_b), 0.78 if z0 < -9.0 else (1.75 if z0 < -7.5 else 2.25))
                w1_b = s * min(cabin_half_width(z1, y1_b), 0.78 if z1 < -9.0 else (1.75 if z1 < -7.5 else 2.25))
                liner.quad(
                    [(w0_b, y0_b, z0), (w1_b, y1_b, z1), (w1_t, y1_t, z1), (w0_t, y0_t, z0)],
                    mat_idx=1,
                    uv_scale=0.5,
                    expected_normal=np.array([-s, -0.5, 0.0]),
                )

    # 2. Cockpit lower side walls below the side cockpit windows (Z=-13.5..-8.7, Y=-0.50..+0.02).
    for s in (-1.0, 1.0):
        z_cw = [-13.5, -12.0, -10.5, -8.7]
        for i in range(len(z_cw) - 1):
            za, zb = z_cw[i], z_cw[i + 1]
            y_top_a = 0.02 if za < -11.2 else (1.05 if za < -9.9 else 1.50)
            y_top_b = 0.02 if zb < -11.2 else (1.05 if zb < -9.9 else 1.50)
            xa0, xb0 = s * cabin_half_width(za, COCKPIT_FLOOR_Y), s * cabin_half_width(zb, COCKPIT_FLOOR_Y)
            xa1, xb1 = s * cabin_half_width(za, y_top_a), s * cabin_half_width(zb, y_top_b)
            liner.quad(
                [(xa0, COCKPIT_FLOOR_Y, za), (xb0, COCKPIT_FLOOR_Y, zb), (xb1, y_top_b, zb), (xa1, y_top_a, za)],
                mat_idx=3,
                uv_scale=0.6,
                expected_normal=np.array([-s, 0.0, 0.0]),
            )

    # 3. Forward flight instrument console & anti-glare glareshield cleanly below pane-nose (Y_max = -0.10 < -0.07!).
    # Main console cabinet (Z=-13.55..-12.05, X=-1.20..+1.20, Y=-0.50..-0.10).
    details.box((-1.18, COCKPIT_FLOOR_Y, -13.55), (1.18, -0.11, -12.15), mat_idx=3)
    # Angled upper instrument panel facing back toward the pilot (+Z, +Y).
    details.quad(
        [(-1.12, -0.12, -12.30), (1.12, -0.12, -12.30), (1.12, -0.45, -11.95), (-1.12, -0.45, -11.95)],
        mat_idx=3,
        expected_normal=np.array([0.0, 0.6, 0.8]),
    )
    # Three crisp backlit flight telemetry MFDs on the angled panel.
    for cx in (-0.65, 0.0, 0.65):
        details.quad(
            [(cx - 0.24, -0.17, -12.22), (cx + 0.24, -0.17, -12.22), (cx + 0.24, -0.37, -12.02), (cx - 0.24, -0.37, -12.02)],
            mat_idx=6,
            expected_normal=np.array([0.0, 0.6, 0.8]),
        )
    # Center avionics pedestal between console and seat.
    details.box((-0.18, COCKPIT_FLOOR_Y, -12.15), (0.18, -0.18, -11.45), mat_idx=3)
    # Overhead cockpit switch panel (Z=-10.8..-9.8, hugging sloped cockpit ceiling, well above pilot's head).
    details.box((-0.34, 2.02, -10.65), (0.34, 2.15, -9.90), mat_idx=3)

    # 4. Pilot's contoured flight command chair (matches Shuttle.Seat: X in [-0.30, +0.30], Y in [-0.50, 0.00], Z in [-11.20, -10.60]).
    # Pedestal & metallic frame base.
    details.box((-0.18, COCKPIT_FLOOR_Y, -11.08), (0.18, COCKPIT_FLOOR_Y + 0.26, -10.72), mat_idx=2)
    # Upholstered seat pan.
    details.box((-0.29, COCKPIT_FLOOR_Y + 0.26, -11.18), (0.29, COCKPIT_FLOOR_Y + 0.42, -10.64), mat_idx=4)
    # Contoured backrest + headrest (at the aft edge of Seat, Z=-10.68..-10.58, cleanly ahead of SeatStand Z=-10.15!).
    details.box((-0.28, COCKPIT_FLOOR_Y + 0.42, -10.70), (0.28, COCKPIT_FLOOR_Y + 1.02, -10.60), mat_idx=4)
    details.box((-0.16, COCKPIT_FLOOR_Y + 1.02, -10.70), (0.16, COCKPIT_FLOOR_Y + 1.22, -10.61), mat_idx=4)
    # Armrests.
    for s in (-1.0, 1.0):
        details.box((s * 0.29 - 0.03, COCKPIT_FLOOR_Y + 0.44, -11.10), (s * 0.29 + 0.03, COCKPIT_FLOOR_Y + 0.52, -10.66), mat_idx=2)


def _add_structural_ribs_and_bulkhead(details):
    """Adds structural aerospace hull ribs between the windows, ceiling stringers, and the aft pressurized engine bulkhead."""
    # Structural wall/ceiling ribs placed strictly between the 8 side windows and clear of the doorway (Z in [2.2, 3.4]).
    rib_zs = [-5.00, -3.70, -2.40, -1.10, 0.20, 2.12, 3.48, 3.92]
    for z in rib_zs:
        yf = cabin_floor_y(z)
        w_bot = cabin_half_width(z, yf)
        w_mid = cabin_half_width(z, 1.65)
        w_top = cabin_half_width(z, CEILING_Y)
        thick = 0.08
        depth = 0.11
        # Overhead ceiling cross-beam.
        details.box((-w_top, CEILING_Y - depth, z - thick), (w_top, CEILING_Y, z + thick), mat_idx=2)
        # Port & starboard vertical wall ribs (2.12 and 3.48 cleanly frame the doorway).
        for s in (-1.0, 1.0):
            x_in = min(w_bot, w_mid) - depth
            details.box((s * x_in - 0.04, yf, z - thick), (s * x_in + 0.04, 1.65, z + thick), mat_idx=2)

    # Longitudinal overhead ceiling stringers + handrail grab bars for zero-G cabin movement!
    for s in (-1.0, 1.0):
        details.box((s * 0.85 - 0.03, CEILING_Y - 0.08, -6.0), (s * 0.85 + 0.03, CEILING_Y, AFT_BULKHEAD_Z), mat_idx=2)
        # Zero-G grab rail slightly below ceiling stringer.
        details.box((s * 0.85 - 0.015, CEILING_Y - 0.18, -5.8), (s * 0.85 + 0.015, CEILING_Y - 0.14, 3.8), mat_idx=5)

    # Aft pressurized engine bulkhead at Z = +4.00 m (closes the cabin completely against the engine bay!).
    w_fl = cabin_half_width(AFT_BULKHEAD_Z, AFT_FLOOR_Y)
    w_md = cabin_half_width(AFT_BULKHEAD_Z, 1.65)
    w_tp = cabin_half_width(AFT_BULKHEAD_Z, CEILING_Y)
    details.quad(
        [(-w_fl, AFT_FLOOR_Y, AFT_BULKHEAD_Z), (w_fl, AFT_FLOOR_Y, AFT_BULKHEAD_Z), (w_md, 1.65, AFT_BULKHEAD_Z), (-w_md, 1.65, AFT_BULKHEAD_Z)],
        mat_idx=1,
        uv_scale=0.45,
        expected_normal=np.array([0.0, 0.0, -1.0]),
    )
    details.quad(
        [(-w_md, 1.65, AFT_BULKHEAD_Z), (w_md, 1.65, AFT_BULKHEAD_Z), (w_tp, CEILING_Y, AFT_BULKHEAD_Z), (-w_tp, CEILING_Y, AFT_BULKHEAD_Z)],
        mat_idx=1,
        uv_scale=0.45,
        expected_normal=np.array([0.0, 0.0, -1.0]),
    )
    # Recessed engineering access hatch + structural stiffeners on the aft bulkhead.
    details.box((-0.78, AFT_FLOOR_Y + 0.15, AFT_BULKHEAD_Z - 0.10), (0.78, AFT_FLOOR_Y + 2.15, AFT_BULKHEAD_Z), mat_idx=2)
    details.box((-0.66, AFT_FLOOR_Y + 0.27, AFT_BULKHEAD_Z - 0.14), (0.66, AFT_FLOOR_Y + 2.03, AFT_BULKHEAD_Z - 0.06), mat_idx=3)
    # Vertical structural stanchions & coolant conduit pipes flanking the engineering hatch.
    for s in (-1.0, 1.0):
        details.box((s * 1.45 - 0.07, AFT_FLOOR_Y, AFT_BULKHEAD_Z - 0.12), (s * 1.45 + 0.07, CEILING_Y, AFT_BULKHEAD_Z), mat_idx=2)
        details.box((s * 2.10 - 0.04, AFT_FLOOR_Y + 0.2, AFT_BULKHEAD_Z - 0.10), (s * 2.10 + 0.04, 2.4, AFT_BULKHEAD_Z - 0.02), mat_idx=5)


def _add_passenger_furnishings(details):
    """Adds sleek passenger cabin benches/seats and equipment lockers along the outer aft cabin walls, leaving the
    entire center aisle (|X| < 2.2 m), the pen clip zone, and the starboard doorway completely unobstructed."""
    # Passenger seating rows along port wall (Z=-2.2..+3.2) and forward-starboard wall (Z=-2.2..+0.6, clear of doorway!).
    for z_seat in (-2.0, -0.8, 0.4):
        for s in (-1.0, 1.0):
            wx = cabin_half_width(z_seat, AFT_FLOOR_Y + 0.5) - 0.55
            x0, x1 = (wx - 0.35, wx + 0.35) if s > 0 else (-wx - 0.35, -wx + 0.35)
            details.box((x0, AFT_FLOOR_Y, z_seat - 0.32), (x1, AFT_FLOOR_Y + 0.38, z_seat + 0.32), mat_idx=2)
            details.box((x0, AFT_FLOOR_Y + 0.38, z_seat - 0.32), (x1, AFT_FLOOR_Y + 0.48, z_seat + 0.32), mat_idx=4)
            details.box((x0, AFT_FLOOR_Y + 0.48, z_seat + 0.22), (x1, AFT_FLOOR_Y + 1.02, z_seat + 0.32), mat_idx=4)

    # Port aft equipment & spacesuit locker cabinet (opposite the starboard boarding doorway, Z=1.5..3.3).
    wx_port = cabin_half_width(2.4, AFT_FLOOR_Y + 1.0) - 0.55
    details.box((-wx_port - 0.35, AFT_FLOOR_Y, 1.50), (-wx_port + 0.25, AFT_FLOOR_Y + 1.85, 3.30), mat_idx=2)
    details.box((-wx_port + 0.22, AFT_FLOOR_Y + 0.15, 1.65), (-wx_port + 0.28, AFT_FLOOR_Y + 1.70, 3.15), mat_idx=3)


def _check_cabin_bounds(cabin_objs):
    """Verifies no fitted cabin vertex escapes the Pioneer fuselage envelope."""
    for obj in cabin_objs:
        for v in obj.data.vertices:
            p = to_sh(v.co)
            if p[2] < -13.90 or p[2] > 4.25:
                raise ValueError(f"{obj.name} vertex {p} outside cabin Z bounds [-13.9, 4.25]")
            if p[1] < -1.65 or p[1] > 3.25:
                raise ValueError(f"{obj.name} vertex {p} outside cabin Y bounds [-1.65, 3.25]")
            if abs(p[0]) > 6.10:
                raise ValueError(f"{obj.name} vertex {p} outside cabin X bounds [-6.10, 6.10]")
