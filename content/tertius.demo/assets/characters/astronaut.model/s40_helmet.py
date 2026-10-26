# The head gear: the comm cap ("Snoopy cap") with its microphones, and the helmet. The
# helmet is a clear bubble that sits on the neck ring of s30_hardware.py, a hard outer shell over its top, back
# and sides (the extravehicular visor assembly), a gold sun visor raised to the brow, and two helmet lamps.
#
# The helmet frame: the center C of the bubble lies on the axis of the neck ring, so the bubble's lower rim meets
# the ring's lip. Elevation is the angle above the plane through C across that axis. Azimuth is the angle around
# the axis from the front (-Y).
import json
import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

from tertius_blender.geometry import MB, frame_matrix

P = model.params["helmet"]
HW = model.params["hardware"]["neck_ring"]
J = {k: np.array(v) for k, v in json.loads(bpy.context.scene["astronaut_joints"]).items()}
M = bpy.data.materials

# --- Helmet frame -------------------------------------------------------------------------------------------------
axis = np.array(HW["axis"], dtype=float)
axis /= np.linalg.norm(axis)
ring_top = J["neck"] + np.array(HW["offset"]) + axis * (HW["height"] + 0.008)
R = P["radius"]
r_open = HW["outer"] - P["seat"]
C = ring_top + axis * math.sqrt(R * R - r_open * r_open)
theta_open = math.pi - math.asin(r_open / R)
front = np.array((0.0, -1.0, 0.0))
front = front - (front @ axis) * axis
front /= np.linalg.norm(front)
side = np.cross(axis, front)  # toward -X, the person's right


def direction(theta, phi):
    """The unit vector at polar angle theta from the helmet axis and azimuth phi from the front."""
    return math.cos(theta) * axis + math.sin(theta) * (math.cos(phi) * front + math.sin(phi) * side)


def elevation_of(p):
    d = (np.asarray(p) - C)
    return math.asin((d @ axis) / np.linalg.norm(d))


def sphere_patch(mb, r_in, r_out, theta_max, keep, n_theta, n_phi):
    """A shell of thickness r_out - r_in on the helmet sphere from its top down to theta_max, with the cells for which
    keep(elevation, azimuth) is true. Open cell edges get a rim, so the patch is closed."""
    layers = []
    for r in (r_out, r_in):
        pole = mb.bm.verts.new(tuple(C + r * axis))
        rows = [[mb.bm.verts.new(tuple(C + r * direction(theta_max * i / n_theta, 2 * math.pi * j / n_phi - math.pi)))
                 for j in range(n_phi)] for i in range(1, n_theta + 1)]
        layers.append((pole, rows))

    def cell_kept(i, j):
        theta = theta_max * (i + 1.5) / n_theta
        phi = 2 * math.pi * (j + 0.5) / n_phi - math.pi
        return keep(math.pi / 2 - theta, phi)

    for k, (pole, rows) in enumerate(layers):
        flip = k == 1
        for j in range(n_phi):
            tri = (pole, rows[0][j], rows[0][(j + 1) % n_phi])
            mb.bm.faces.new(tri[::-1] if flip else tri)
        for i in range(n_theta - 1):
            for j in range(n_phi):
                if cell_kept(i, j):
                    q = (rows[i][j], rows[i + 1][j], rows[i + 1][(j + 1) % n_phi], rows[i][(j + 1) % n_phi])
                    mb.bm.faces.new(q[::-1] if flip else q)
    outer, inner = layers[0][1], layers[1][1]

    def rim(a, b):
        mb.bm.faces.new((outer[a[0]][a[1]], outer[b[0]][b[1]], inner[b[0]][b[1]], inner[a[0]][a[1]]))

    for i in range(n_theta - 1):
        for j in range(n_phi):
            if not cell_kept(i, j):
                continue
            j1 = (j + 1) % n_phi
            if i == n_theta - 2 or not cell_kept(i + 1, j):
                rim((i + 1, j1), (i + 1, j))
            if i == 0 or not cell_kept(i - 1, j):
                if i > 0:
                    rim((i, j), (i, j1))
            if not cell_kept(i, (j - 1) % n_phi):
                rim((i, j), (i + 1, j))
            if not cell_kept(i, j1):
                rim((i + 1, j1), (i, j1))
    # The grid points of the left-out cells carry no face.
    bmesh.ops.delete(mb.bm, geom=[v for v in mb.bm.verts if not v.link_faces], context="VERTS")


# --- The bubble: one clear surface from the ring to the top. --------------------------------------------------------
basis = Matrix(((*side, 0.0), (*front, 0.0), (*axis, 0.0), (0, 0, 0, 1))).transposed()
place = Matrix.Translation(Vector(C)) @ basis
bubble = MB()
steps = P["rows"]
profile = [(R * math.sin(theta_open * (1 - k / steps)), R * math.cos(theta_open * (1 - k / steps)))
           for k in range(steps + 1)]
bubble.lathe(profile, place, segs=P["segments"], closed=False)
bubble_ob = bubble.finish("Helmet_Bubble", M["M_Bubble"], "Helmet", smooth_angle=89.0)
M["M_Bubble"].surface_render_method = "BLENDED"
M["M_Bubble"].use_backface_culling = False
principled = M["M_Bubble"].node_tree.nodes["Principled BSDF"]
principled.inputs["Alpha"].default_value = P["bubble_alpha"]

# --- The hard shell: the top, back and sides, open in front from the ring up to the brow. ----------------------------
eyes = np.array([v.co[:] for v in bpy.data.objects["Eyes"].data.vertices])
eye_center = eyes.mean(axis=0)
e_eye = elevation_of(eye_center)
window_top = e_eye + P["window_above_eyes"]
half_width = math.radians(P["window_half_width_deg"])
shell = MB()
sphere_patch(shell, R + P["shell_gap"], R + P["shell_gap"] + P["shell_thickness"], theta_open - P["shell_lift"],
             lambda e, phi: not (abs(phi) < half_width and e < window_top), P["rows"], P["segments"])
shell.finish("Helmet_Shell", M["M_HardWhite"], "Helmet", smooth_angle=50.0)

# --- The gold sun visor, raised so its lower edge sits above the eyes, under the shell's front edge. --------------
visor = MB()
visor_low = e_eye + P["visor_above_eyes"]
sphere_patch(visor, R + 0.004, R + 0.009, theta_open - P["shell_lift"],
             lambda e, phi: abs(phi) < half_width + 0.06 and visor_low < e < window_top + 0.25,
             P["rows"] * 2, P["segments"])
visor.finish("Helmet_Visor", M["M_Gold"], "Helmet", smooth_angle=50.0)

# --- Lamps: a housing on each side of the shell, with a lens toward the front. -----------------------------------
lamps, lenses = MB(), MB()
for sign in (1, -1):
    phi = sign * math.radians(P["lamp_azimuth_deg"])
    theta = math.pi / 2 - math.radians(P["lamp_elevation_deg"])
    normal = direction(theta, phi)
    origin = C + normal * (R + P["shell_gap"] + P["shell_thickness"] - 0.006)
    frame = frame_matrix(origin, normal, tangent_hint=front)
    w, d, h = P["lamp_size"]
    lamps.box_local(frame, (0, 0, h / 2), (w, d, h), bevel=0.006)
    lens_center = frame @ Vector((0, d / 2 + 0.002, h / 2))
    lenses.cyl(tuple(lens_center), tuple(front), w * 0.36, 0.008, segs=20)
# A small camera on top of the right lamp.
phi = -math.radians(P["lamp_azimuth_deg"])
normal = direction(math.pi / 2 - math.radians(P["lamp_elevation_deg"] + P["camera_above_deg"]), phi)
origin = C + normal * (R + P["shell_gap"] + P["shell_thickness"] - 0.004)
frame = frame_matrix(origin, normal, tangent_hint=front)
lamps.box_local(frame, (0, 0.01, 0.018), (0.034, 0.07, 0.032), bevel=0.005)
lenses.cyl(tuple(frame @ Vector((0, 0.047, 0.018))), tuple(front), 0.011, 0.008, segs=16)
lamps.finish("Helmet_Lamps", M["M_HardWhite"], "Helmet", smooth_angle=40.0)
lenses.finish("Helmet_Lenses", M["M_Lamp"], "Helmet", smooth_angle=40.0)

# --- The comm cap over the head, and its microphones. ------------------------------------------------------------
head = bpy.data.objects["Head"].data
hv = np.array([v.co[:] for v in head.vertices])
lo, hi = hv.min(axis=0), hv.max(axis=0)
hc = (lo + hi) / 2
cap_lower = hc[2] + P["cap_lower"]


def in_face(p):
    """The face opening of the cap: an ellipse in front view, from the brow to under the chin, on the front half."""
    ex = p[0] / P["face_half_width"]
    ez = (p[2] - (eye_center[2] + P["face_center_dz"])) / P["face_half_height"]
    return p[1] < hc[1] + P["face_depth"] and ex * ex + ez * ez < 1.0


bm = bmesh.new()
bm.from_mesh(head)
bm.verts.ensure_lookup_table()
covered = [v.co.z > cap_lower and not in_face(v.co) for v in bm.verts]
keep_faces = [f for f in bm.faces if all(covered[v.index] for v in f.verts)]
cap = MB()
index = {}
for f in keep_faces:
    vs = []
    for v in f.verts:
        if v.index not in index:
            index[v.index] = cap.bm.verts.new(tuple(v.co + v.normal * P["cap_thickness"]))
        vs.append(index[v.index])
    cap.bm.faces.new(vs)
bm.free()
# Smooth the edge of the cap along itself, so the face opening and the lower edge are clean curves, not the steps of
# the head's faces.
edge = [v for v in cap.bm.verts if any(e.is_boundary for e in v.link_edges)]
for _ in range(P["cap_edge_smooth"]):
    moved = []
    for v in edge:
        ring = [e.other_vert(v).co for e in v.link_edges if e.is_boundary]
        moved.append(v.co * 0.5 + (ring[0] + ring[1]) * 0.25 if len(ring) == 2 else v.co.copy())
    for v, co in zip(edge, moved):
        v.co = co
cap.finish("Cap", M["M_Cap"], "Body", smooth_angle=60.0)

# The headset: an ear cup over each ear, and a microphone boom from each cup to the corner of the mouth.
headset = MB()
for sign in (1, -1):
    ear = Vector((sign * (hi[0] - lo[0]) / 2 + sign * (P["cap_thickness"] + 0.004), hc[1] + 0.012,
                  eye_center[2] - 0.02))
    headset.cyl(tuple(ear), (sign, 0, 0), P["ear_cup_radius"], 0.02, segs=20)
    mouth = Vector((sign * 0.022, lo[1] + 0.012, eye_center[2] - 0.085))
    headset.tube([tuple(ear + Vector((0, -0.01, -0.01))), tuple(mouth + Vector((sign * 0.03, 0.02, 0.0))),
                  tuple(mouth)], 0.0035, segs=8, bend=0.03)
headset.finish("Headset", M["M_Headset"], "Body", smooth_angle=60.0)

result = {"center": [round(float(c), 3) for c in C], "radius": R, "eye_elevation_deg": round(math.degrees(e_eye), 1),
          "head_top_clearance": round(float((C + R * axis)[2] - hi[2]), 3),
          "face_clearance": round(float(-(C[1] - R) + lo[1]), 3)}
