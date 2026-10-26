# Procedural materials. The build bakes the suit fabric and the backpack shell into texture maps (bake.objects of
# model.json). The small parts keep the constant materials of s00_scene.py, which glTF holds as they are.
#
# Each baked material has one Principled BSDF with a constant metallic input, and a Bump node into its normal
# input: the bake contract of the asset pipeline. The materials read object coordinates, and every part sits at the
# origin with no turn, so object coordinates are the coordinates of the person, in meters.
#
# The suit fabric: panels with sewn seams and stitching, box-projected so they wrap the limbs; a fine weave; the
# red commander stripes around both thighs; a round mission patch on the left shoulder; and dust that rises from the
# boots and gathers in the folds.
import json

import bpy
import numpy as np

from tertius_blender.nodes import NB

P = model.params["materials"]
J = {k: np.array(v) for k, v in json.loads(bpy.context.scene["astronaut_joints"]).items()}


def fresh(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.node_tree.nodes.clear()
    return m


def principled_out(b, base, rough, metallic, normal):
    p = b.n("ShaderNodeBsdfPrincipled", {"Base Color": base, "Roughness": rough, "Metallic": metallic})
    b.set(p.inputs["Normal"], normal)
    o = b.n("ShaderNodeOutputMaterial", {"Surface": p.outputs[0]})
    o.is_active_output = True
    return p


def projected(b, pos, nrm, fn):
    """Box projection: fn(u, v) in the plane of each face's dominant axis, blended by the axis masks. X faces use
    (y, z), Y faces use (x, z), Z faces use (x, y). fn returns a dict of float sockets."""
    px, py, pz = b.sep(pos)
    ax, ay, az = b.sep(b.vmath("ABSOLUTE", nrm))
    mx = b.math("MULTIPLY", b.math("GREATER_THAN", ax, ay), b.math("GREATER_THAN", ax, az))
    my = b.math("MULTIPLY", b.math("GREATER_THAN", ay, ax), b.math("GREATER_THAN", ay, az))
    mz = b.math("SUBTRACT", 1.0, b.math("ADD", mx, my), clamp=True)
    rx = fn(b.comb(py, pz, 0.0))
    ry = fn(b.comb(px, pz, 0.0))
    rz = fn(b.comb(px, py, 0.0))
    return {k: b.math("ADD", b.math("ADD", b.math("MULTIPLY", rx[k], mx), b.math("MULTIPLY", ry[k], my)),
                      b.math("MULTIPLY", rz[k], mz)) for k in rx}


def fabric_panels(b, vec):
    f = P["fabric"]
    seam, rnd = b.brick(vec, f["panel"][0], f["panel"][1], f["seam"], 0.6, offset=0.5)
    band, _ = b.brick(vec, f["panel"][0], f["panel"][1], f["seam"] * 5.0, 0.0, offset=0.5)
    dots, _ = b.brick(vec, f["stitch"], f["stitch"], 0.5, 0.2, offset=0.0)
    stitch = b.math("MULTIPLY", b.math("SUBTRACT", band, seam, clamp=True), b.math("SUBTRACT", 1.0, dots))
    return {"seam": seam, "stitch": stitch, "rnd": rnd}


def patch_center():
    """A point on the outside of the left upper arm, a little toward the front: the middle of the mission patch."""
    suit = np.array([v.co[:] for v in bpy.data.objects["Suit"].data.vertices])
    a, e = J["l-shoulder"], J["l-elbow"]
    axis = (e - a) / np.linalg.norm(e - a)
    point = a + P["patch"]["t"] * (e - a)
    near = suit[np.abs((suit - point) @ axis) < 0.02]
    out = np.array(P["patch"]["toward"], dtype=float)
    out = out - (out @ axis) * axis
    return near[np.argmax((near - point) @ out)]


def suit_material():
    m = fresh("M_Suit")
    b = NB(m.node_tree)
    tc = b.n("ShaderNodeTexCoord")
    pos, nrm = tc.outputs["Object"], tc.outputs["Normal"]
    px, py, pz = b.sep(pos)
    pp = projected(b, pos, nrm, lambda v: fabric_panels(b, v))
    seam, stitch, rnd = pp["seam"], pp["stitch"], pp["rnd"]
    weave = b.noise(pos, P["fabric"]["weave_scale"], 2.0, 0.5)

    c = b.mixc(b.math("MULTIPLY", rnd, 0.6), tuple(P["fabric"]["color"]), tuple(P["fabric"]["color_alt"]))
    # Commander stripes around both thighs.
    z0, z1 = P["stripe_z"]
    stripe = b.inrange(pz, z0, z1, soft=0.002)
    c = b.mixc(stripe, c, tuple(P["stripe_color"]))
    # The mission patch: a blue disc with a white rim and a gold star point in the middle.
    centre = tuple(float(v) for v in patch_center())
    dist = b.vmath("DISTANCE", pos, centre)
    r = P["patch"]["radius"]
    disc = b.maprange(dist, r, r - 0.002, 0.0, 1.0)
    inner = b.maprange(dist, r - 0.008, r - 0.010, 0.0, 1.0)
    core = b.maprange(dist, r * 0.3, r * 0.3 - 0.002, 0.0, 1.0)
    patch_col = b.mixc(inner, (0.9, 0.9, 0.88), tuple(P["patch"]["color"]))
    patch_col = b.mixc(core, patch_col, (0.85, 0.62, 0.15))
    c = b.mixc(disc, c, patch_col)
    # Dust: rises from the ground, and gathers in folds and seams.
    ao = b.n("ShaderNodeAmbientOcclusion", {"Distance": 0.08}, samples=16, inside=False, only_local=True)
    cav = b.maprange(ao.outputs["AO"], 0.95, 0.45)
    low = b.maprange(pz, P["dust_height"], 0.05)
    blotch = b.maprange(b.noise(pos, 6.0, 4.0, 0.6), 0.45, 0.7)
    dust = b.math("ADD", b.math("MULTIPLY", low, b.math("ADD", 0.35, b.math("MULTIPLY", blotch, 0.65))),
                  b.math("MULTIPLY", cav, 0.45))
    dust = b.math("ADD", dust, b.math("MULTIPLY", seam, 0.25), clamp=True)
    c = b.mixc(b.math("MULTIPLY", dust, 0.55), c, tuple(P["dust_color"]))
    c = b.mixc(b.math("MULTIPLY", weave, 0.08), c, (0.6, 0.6, 0.58))

    rough = b.mixf(seam, 0.82, 0.92)
    rough = b.mixf(disc, rough, 0.6)
    h = b.math("SUBTRACT", b.math("MULTIPLY", weave, 0.15), b.math("MULTIPLY", seam, 1.0))
    h = b.math("ADD", h, b.math("MULTIPLY", stitch, 0.35))
    h = b.math("ADD", h, b.math("MULTIPLY", disc, 0.3))
    bump = b.n("ShaderNodeBump", {"Height": h, "Strength": 0.8, "Distance": P["fabric"]["bump"]})
    principled_out(b, c, rough, 0.0, bump.outputs["Normal"])


def pack_material():
    m = fresh("M_Pack")
    b = NB(m.node_tree)
    tc = b.n("ShaderNodeTexCoord")
    pos, nrm = tc.outputs["Object"], tc.outputs["Normal"]
    f = P["pack"]

    def panels(v):
        seam, rnd = b.brick(v, f["panel"][0], f["panel"][1], f["seam"], 0.4, offset=0.0)
        band, _ = b.brick(v, f["panel"][0], f["panel"][1], f["seam"] * 6.0, 0.0, offset=0.0)
        dots, _ = b.brick(v, f["fastener"], f["fastener"], 0.55, 0.8, offset=0.0)
        fast = b.math("MULTIPLY", b.math("SUBTRACT", band, seam, clamp=True), b.math("SUBTRACT", 1.0, dots))
        return {"seam": seam, "rnd": rnd, "fast": fast}

    pp = projected(b, pos, nrm, panels)
    c = b.mixc(b.math("MULTIPLY", pp["rnd"], 0.5), tuple(f["color"]), tuple(f["color_alt"]))
    grime = b.maprange(b.noise(pos, 9.0, 5.0, 0.6), 0.5, 0.75)
    c = b.mixc(b.math("ADD", b.math("MULTIPLY", grime, 0.25), b.math("MULTIPLY", pp["seam"], 0.4), clamp=True), c,
               (0.35, 0.33, 0.3))
    c = b.mixc(pp["fast"], c, (0.55, 0.56, 0.58))
    rough = b.mixf(pp["seam"], 0.5, 0.75)
    h = b.math("SUBTRACT", b.math("MULTIPLY", pp["fast"], 0.4), pp["seam"])
    bump = b.n("ShaderNodeBump", {"Height": h, "Strength": 0.8, "Distance": f["bump"]})
    principled_out(b, c, rough, 0.0, bump.outputs["Normal"])


suit_material()
pack_material()
result = {name: len(bpy.data.materials[name].node_tree.nodes) for name in ("M_Suit", "M_Pack")}
