# The jumpsuit fabric, a procedural material that the build bakes into maps (bake.objects of model.json). The
# trims (collar, cuffs and pockets) get the same fabric without the marks, as a bake object of their own. The person keeps
# MPFB's image materials, which glTF holds as they are, and the small parts keep the constants of s00_scene.py.
#
# Each baked material has one Principled BSDF with a constant metallic input, and a Bump node into its normal input:
# the bake contract of the asset pipeline. The materials read object coordinates, and every part sits at the origin
# with no turn, so object coordinates are the coordinates of the person, in meters.
#
# The fabric: panels with sewn seams and stitching, box-projected so they wrap the limbs; a fine twill; an orange
# trim band around each upper arm; a ship patch on the left shoulder; and a light wear in the folds.
import json

import bpy
import numpy as np

from tertius_blender.nodes import NB

P = model.params["materials"]
J = {k: np.array(v) for k, v in json.loads(bpy.context.scene["crew_joints"]).items()}


def principled_out(b, base, rough, metallic, normal):
    p = b.n("ShaderNodeBsdfPrincipled", {"Base Color": base, "Roughness": rough, "Metallic": metallic})
    b.set(p.inputs["Normal"], normal)
    o = b.n("ShaderNodeOutputMaterial", {"Surface": p.outputs[0]})
    o.is_active_output = True
    return p


def projected(b, pos, nrm, fn):
    """Box projection: fn(u, v) in the plane of each face's dominant axis, blended by the axis masks."""
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
    """A point on the outside of the left upper arm, a little toward the front: the middle of the ship patch."""
    suit = np.array([v.co[:] for v in bpy.data.objects["Jumpsuit"].data.vertices])
    a, e = J["l-shoulder"], J["l-elbow"]
    axis = (e - a) / np.linalg.norm(e - a)
    point = a + P["patch"]["t"] * (e - a)
    near = suit[np.abs((suit - point) @ axis) < 0.02]
    out = np.array(P["patch"]["toward"], dtype=float)
    out = out - (out @ axis) * axis
    return near[np.argmax((near - point) @ out)]


def fabric(name, with_marks):
    m = bpy.data.materials[name]
    m.node_tree.nodes.clear()
    b = NB(m.node_tree)
    tc = b.n("ShaderNodeTexCoord")
    pos, nrm = tc.outputs["Object"], tc.outputs["Normal"]
    pp = projected(b, pos, nrm, lambda v: fabric_panels(b, v))
    seam, stitch, rnd = pp["seam"], pp["stitch"], pp["rnd"]
    twill = b.noise(pos, P["fabric"]["weave_scale"], 2.0, 0.5)
    c = b.mixc(b.math("MULTIPLY", rnd, 0.5), tuple(P["fabric"]["color"]), tuple(P["fabric"]["color_alt"]))
    rough = b.mixf(seam, 0.78, 0.9)
    h = b.math("SUBTRACT", b.math("MULTIPLY", twill, 0.15), seam)
    h = b.math("ADD", h, b.math("MULTIPLY", stitch, 0.35))
    if with_marks:
        # The trim band around each upper arm, between two distances from the shoulder joint.
        for s in "lr":
            a, e = J[s + "-shoulder"], J[s + "-elbow"]
            axis = tuple(float(v) for v in (e - a) / np.linalg.norm(e - a))
            rel = b.vmath("SUBTRACT", pos, tuple(float(v) for v in a))
            d = b.vmath("DOT_PRODUCT", rel, axis)
            # Only the sleeve: the points near the line of the upper arm.
            off = b.vmath("LENGTH", b.vmath("SUBTRACT", rel, b.vmath("SCALE", axis, scale=d)))
            near = b.maprange(off, P["trim"]["radius"], P["trim"]["radius"] - 0.01)
            # And only outside the shoulder joint, so the band stays off the chest under the arm.
            beyond = b.maprange(b.math("MULTIPLY", b.sep(pos)[0], 1.0 if s == "l" else -1.0),
                                abs(float(a[0])) - 0.005, abs(float(a[0])) + 0.005)
            near = b.math("MULTIPLY", near, beyond)
            t0, t1 = P["trim"]["from_shoulder"]
            trim = b.math("MULTIPLY", b.inrange(d, t0, t1, soft=0.002), near)
            c = b.mixc(trim, c, tuple(P["trim"]["color"]))
        # The ship patch: an orange disc with a dark rim and a white star.
        centre = tuple(float(v) for v in patch_center())
        dist = b.vmath("DISTANCE", pos, centre)
        r = P["patch"]["radius"]
        disc = b.maprange(dist, r, r - 0.002, 0.0, 1.0)
        inner = b.maprange(dist, r - 0.006, r - 0.008, 0.0, 1.0)
        core = b.maprange(dist, r * 0.28, r * 0.28 - 0.002, 0.0, 1.0)
        patch_col = b.mixc(inner, (0.05, 0.05, 0.06), tuple(P["patch"]["color"]))
        patch_col = b.mixc(core, patch_col, (0.9, 0.9, 0.88))
        c = b.mixc(disc, c, patch_col)
        rough = b.mixf(disc, rough, 0.6)
        h = b.math("ADD", h, b.math("MULTIPLY", disc, 0.3))
    # Wear: the cloth lightens a little in its folds and seams.
    ao = b.n("ShaderNodeAmbientOcclusion", {"Distance": 0.06}, samples=16, inside=False, only_local=True)
    worn = b.math("ADD", b.math("MULTIPLY", b.maprange(ao.outputs["AO"], 0.95, 0.5), 0.5),
                  b.math("MULTIPLY", seam, 0.3), clamp=True)
    c = b.mixc(b.math("MULTIPLY", worn, P["wear"]), c, tuple(P["fabric"]["worn"]))
    c = b.mixc(b.math("MULTIPLY", twill, 0.06), c, (0.5, 0.55, 0.6))
    bump = b.n("ShaderNodeBump", {"Height": h, "Strength": 0.8, "Distance": P["fabric"]["bump"]})
    principled_out(b, c, rough, 0.0, bump.outputs["Normal"])


fabric("M_Suit", True)
fabric("M_Trims", False)
result = {name: len(bpy.data.materials[name].node_tree.nodes) for name in ("M_Suit", "M_Trims")}
