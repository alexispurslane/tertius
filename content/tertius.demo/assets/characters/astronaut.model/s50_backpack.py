# The life support: the backpack (the primary life support system, PLSS) with the secondary oxygen pack under it,
# the display and control module (DCM) on the chest, and the two hoses that join them around the waist.
#
# The backpack and the chest box sit on the measured back and chest of the suit of s20_shells.py, sunk a little into
# it, so neither floats.
import json

import bpy
import numpy as np
from mathutils import Vector

from tertius_blender.geometry import MB, beam, rod

P = model.params["backpack"]
J = {k: np.array(v) for k, v in json.loads(bpy.context.scene["astronaut_joints"]).items()}
M = bpy.data.materials
SUIT = np.array([v.co[:] for v in bpy.data.objects["Suit"].data.vertices])


def surface_y(z0, z1, half_width, back):
    """The farthest y of the suit within |x| < half_width and z0 < z < z1: the back (max y) or the chest (min y)."""
    sel = SUIT[(np.abs(SUIT[:, 0]) < half_width) & (SUIT[:, 2] > z0) & (SUIT[:, 2] < z1)]
    return float(sel[:, 1].max() if back else sel[:, 1].min())


# --- PLSS ---------------------------------------------------------------------------------------------------------
w, d, h = P["size"]
top = J["neck"][2] + P["top_above_neck"]
bottom = top - h
back_y = surface_y(bottom + 0.1, top - 0.1, w / 2 - 0.05, back=True)
y0 = back_y - P["sink"]
y1 = y0 + d
taper = P["taper"]
pack = MB()
pack.hexa([(-w / 2, y0, bottom), (w / 2, y0, bottom), (w / 2, y1, bottom), (-w / 2, y1, bottom),
           (-w / 2 + taper, y0, top), (w / 2 - taper, y0, top), (w / 2 - taper, y1 - taper, top),
           (-w / 2 + taper, y1 - taper, top)], bevel=P["bevel"])
pack_ob = pack.finish("Backpack", M["M_Pack"], "Backpack", smooth_angle=35.0)

# --- Details on the backpack: the oxygen pack, rails, vents, a handle and side panels. -----------------------------
gray, metal, accents = MB(), MB(), MB()
sop_h = P["sop_height"]
gray.box((0.0, (y0 + y1) / 2 - 0.01, bottom - sop_h / 2 + 0.01), (w - 0.04, d - 0.04, sop_h), bevel=0.018)
for sign in (1, -1):
    x = sign * (w / 2 + 0.008)
    # A rail on each side, from the bottom to near the top.
    beam(metal, (x, y0 + 0.05, bottom + 0.06), (x - sign * taper * 0.8, y0 + 0.05, top - 0.08), 0.02, 0.025,
         bevel=0.004)
    # A side panel with a valve.
    gray.box((sign * (w / 2 - 0.004), y0 + d * 0.62, bottom + h * 0.4), (0.03, d * 0.45, h * 0.35), bevel=0.008)
    accents.cyl((sign * (w / 2 + 0.03), y0 + d * 0.62, bottom + h * 0.25), (sign, 0, 0), 0.022, 0.03, segs=16)
# Vents on the back face.
for k in range(P["vents"]):
    z = bottom + h * 0.55 + k * 0.035
    gray.box((0.0, y1 + 0.004, z), (w * 0.55, 0.012, 0.016), bevel=0.004)
# A carry handle over the top.
rod(metal, (-0.1, y1 - 0.06, top - 0.01), (-0.1, y1 - 0.06, top + 0.04), 0.008)
rod(metal, (0.1, y1 - 0.06, top - 0.01), (0.1, y1 - 0.06, top + 0.04), 0.008)
rod(metal, (-0.1, y1 - 0.06, top + 0.04), (0.1, y1 - 0.06, top + 0.04), 0.008)
# An antenna on the right top corner.
rod(metal, (-w / 2 + taper + 0.04, y1 - 0.05, top - 0.01), (-w / 2 + taper + 0.04, y1 - 0.05, top + 0.09), 0.006)

# --- DCM: the chest box, its display and its controls. ---------------------------------------------------------------
cw, cd, ch = P["dcm_size"]
cz = J["neck"][2] - P["dcm_below_neck"]
front_y = surface_y(cz - ch / 2, cz + ch / 2, cw / 2, back=False)
cy = front_y - cd / 2 + P["dcm_sink"]
dcm = MB()
dcm.hexa([(-cw / 2, cy - cd / 2, cz - ch / 2), (cw / 2, cy - cd / 2, cz - ch / 2), (cw / 2, cy + cd / 2, cz - ch / 2),
          (-cw / 2, cy + cd / 2, cz - ch / 2), (-cw / 2, cy - cd / 2 + 0.03, cz + ch / 2),
          (cw / 2, cy - cd / 2 + 0.03, cz + ch / 2), (cw / 2, cy + cd / 2, cz + ch / 2),
          (-cw / 2, cy + cd / 2, cz + ch / 2)], bevel=0.012)
dcm.finish("DCM", M["M_HardWhite"], "Backpack", smooth_angle=35.0)
screen = MB()
screen.box((0.0, cy + 0.005, cz + ch / 2 + 0.002), (cw * 0.55, cd * 0.55, 0.008), bevel=0.002)
screen.finish("DCM_Display", M["M_Display"], "Backpack", smooth_angle=35.0)
for k, x in enumerate((-0.08, -0.03, 0.03, 0.08)):
    knob_mesh = accents if k % 2 == 0 else metal
    knob_mesh.cyl((x, cy - cd / 2 - 0.006, cz + 0.005), (0, -1, 0), 0.014, 0.02, segs=16)
metal.cyl((cw / 2 + 0.008, cy, cz), (1, 0, 0), 0.02, 0.025, segs=16)
accents.cyl((-cw / 2 - 0.008, cy, cz), (-1, 0, 0), 0.02, 0.025, segs=16)

# --- Hoses: from each side of the chest box, under the arm, back to the front edge of the backpack. ---------------
hoses = MB()
for sign in (1, -1):
    start = Vector((sign * (cw / 2 + 0.01), cy + 0.01, cz - 0.02))
    hz = cz - P["hose_drop"]
    rel = SUIT[(np.abs(SUIT[:, 2] - hz) < 0.02) & (np.abs(SUIT[:, 0]) < 0.32) & (np.abs(SUIT[:, 1]) < 0.06)]
    side_x = float(np.abs(rel[:, 0]).max())
    front_x = float(np.abs(SUIT[(np.abs(SUIT[:, 2] - hz) < 0.02) & (np.abs(SUIT[:, 0]) < 0.32)
                                & (SUIT[:, 1] < -0.1)][:, 0]).max())
    pts = [start, start + Vector((sign * 0.05, 0.0, -0.02)),
           Vector((sign * (front_x + P["hose_clearance"]), cy + 0.06, hz)),
           Vector((sign * (side_x + P["hose_clearance"]), 0.0, hz)),
           Vector((sign * (w / 2 - 0.05), y0 + 0.02, hz + 0.02)),
           Vector((sign * (w / 2 - 0.05), y0 + 0.06, hz + 0.02))]
    hoses.tube([tuple(p) for p in pts], P["hose_radius"], segs=10, bend=0.05)
    # Connector collars at both ends.
    metal.cyl(tuple(start), (sign, 0, 0), P["hose_radius"] + 0.006, 0.025, segs=16)
hoses.finish("Hoses", M["M_Hose"], "Backpack", smooth_angle=60.0)

gray.finish("Backpack_Gray", M["M_PackGray"], "Backpack", smooth_angle=35.0)
metal.finish("Backpack_Metal", M["M_Metal"], "Backpack", smooth_angle=35.0)
accents.finish("Accents_Red", M["M_RingRed"], "Backpack", smooth_angle=35.0)

result = {"back_y": round(back_y, 3), "front_y": round(front_y, 3), "pack_top": round(top, 3),
          "pack_bottom": round(bottom, 3)}
