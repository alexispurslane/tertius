# The hard rings and cuffs of the suit, fitted to the soft shells of s20_shells.py: the neck ring that holds the
# helmet, the waist ring of the body seal, the shoulder and thigh bearings, the glove gauntlets with their wrist
# disconnect rings, the boot cuffs and the boot soles.
#
# A fitted ring follows the cross-section of a shell: section() measures, for each of N directions around an axis,
# how far the shell reaches from the axis near a plane. band() then sweeps a closed profile around the axis at that
# distance. So a ring hugs an oval waist or a flat forearm as closely as a round neck.
import json
import math

import bpy
import numpy as np

from tertius_blender import mpfb
from tertius_blender.geometry import MB

P = model.params["hardware"]
J = {k: np.array(v) for k, v in json.loads(bpy.context.scene["astronaut_joints"]).items()}
N = P["segments"]


def points_of(name):
    return np.array([v.co[:] for v in bpy.data.objects[name].data.vertices])


SUIT = points_of("Suit")


def basis(axis):
    a = np.asarray(axis, dtype=float)
    a = a / np.linalg.norm(a)
    ref = np.array((0.0, 0.0, 1.0)) if abs(a[2]) < 0.9 else np.array((0.0, -1.0, 0.0))
    u = np.cross(ref, a)
    u /= np.linalg.norm(u)
    return a, u, np.cross(a, u)


def section(points, center, axis, slab, reach, side=0):
    """The distance from the axis to the farthest shell point, in each of N directions around the axis, among the
    points within `slab` of the plane through `center` and within `reach` of the axis. `side` keeps only the points
    with x * side > 0 (one leg of two). Empty directions take the distance of their neighbors."""
    a, u, v = basis(axis)
    rel = points - center
    d = rel @ a
    rad = rel - d[:, None] * a
    r = np.linalg.norm(rad, axis=1)
    keep = (np.abs(d) < slab) & (r < reach)
    if side:
        keep &= points[:, 0] * side > 0.0
    ang = np.arctan2(rad[keep] @ v, rad[keep] @ u)
    bins = ((ang + math.pi) / (2 * math.pi) * N).astype(int) % N
    radii = np.full(N, np.nan)
    for b, rr in zip(bins, r[keep]):
        radii[b] = rr if np.isnan(radii[b]) else max(radii[b], rr)
    known = np.where(~np.isnan(radii))[0]
    if len(known) == 0:
        raise ValueError("no shell points near {}".format(center))
    for i in np.where(np.isnan(radii))[0]:
        dist = np.minimum(np.abs(known - i), N - np.abs(known - i))
        radii[i] = radii[known[np.argmin(dist)]]
    for _ in range(2):
        radii = (np.roll(radii, 1) + 2 * radii + np.roll(radii, -1)) / 4
    return radii


def centroid(points, center, axis, slab, reach, side=0):
    """The middle of the shell points near the plane: a better axis point than a joint inside an oval section."""
    a, _, _ = basis(axis)
    rel = points - center
    d = rel @ a
    r = np.linalg.norm(rel - d[:, None] * a, axis=1)
    keep = (np.abs(d) < slab) & (r < reach)
    if side:
        keep &= points[:, 0] * side > 0.0
    lo, hi = points[keep].min(axis=0), points[keep].max(axis=0)
    mid = (lo + hi) / 2
    return mid - ((mid - center) @ a) * a


def band(mb, center, axis, profile, radii):
    """A closed ring: the closed `profile` [(s, k, dr), ...] swept around `axis` through `center`. Each profile point
    lies s along the axis and radii[k][i] + dr from it in direction i."""
    a, u, v = basis(axis)
    rows = []
    for i in range(N):
        th = (i + 0.5) / N * 2 * math.pi - math.pi
        out = math.cos(th) * u + math.sin(th) * v
        rows.append([mb.bm.verts.new(tuple(center + s * a + (radii[k][i] + dr) * out)) for s, k, dr in profile])
    m = len(profile)
    for i in range(N):
        r0, r1 = rows[i], rows[(i + 1) % N]
        for k in range(m):
            k2 = (k + 1) % m
            mb.bm.faces.new((r0[k], r1[k], r1[k2], r0[k2]))


def rounded(s0, s1, k_in, d_in, k_out, d_out, r=0.004):
    """A closed profile: a rectangle from s0 to s1 along the axis and from base k_in + d_in to base k_out + d_out
    across it, with its outer corners cut by r."""
    return [(s0, k_in, d_in), (s0, k_out, d_out - r), (s0 + r, k_out, d_out), (s1 - r, k_out, d_out),
            (s1, k_out, d_out - r), (s1, k_in, d_in)]


def convex_hull(points):
    """The convex hull of 2D points, counterclockwise (Andrew's monotone chain)."""
    pts = sorted(set(map(tuple, np.round(points, 6))))

    def half(seq):
        out = []
        for p in seq:
            while len(out) >= 2 and ((out[-1][0] - out[-2][0]) * (p[1] - out[-2][1])
                                     - (out[-1][1] - out[-2][1]) * (p[0] - out[-2][0])) <= 0:
                out.pop()
            out.append(p)
        return out

    return half(pts)[:-1] + half(pts[::-1])[:-1]


metal, white, red, blue, rubber = MB(), MB(), MB(), MB(), MB()
report = {}

# --- Neck ring: a round ring that holds the helmet, with a fabric dam inside it. -----------------------------------
neck = P["neck_ring"]
nc = J["neck"] + np.array(neck["offset"])
naxis = np.array(neck["axis"])
const = [np.full(N, 0.0)]
band(metal, nc, naxis, [(0.0, 0, neck["inner"]), (-neck["skirt"], 0, neck["inner"] + 0.01),
                        (-neck["skirt"], 0, neck["outer"] - 0.012), (0.0, 0, neck["outer"]),
                        (neck["height"], 0, neck["outer"]),
                        (neck["height"], 0, neck["outer"] - 0.012), (neck["height"] + 0.008, 0, neck["outer"] - 0.016),
                        (neck["height"] + 0.008, 0, neck["inner"] + 0.004), (neck["height"], 0, neck["inner"])], const)
# The blue and red stripes of a real neck ring's latch.
band(blue, nc, naxis, rounded(0.012, 0.022, 0, neck["outer"] - 0.004, 0, neck["outer"] + 0.003, r=0.002), const)
band(metal, nc, naxis, [(neck["height"] - 0.004, 0, neck["dam"]), (neck["height"] - 0.004, 0, neck["inner"] + 0.002),
                        (neck["height"], 0, neck["inner"] + 0.002), (neck["height"], 0, neck["dam"])], const)
report["neck_center"] = [round(float(c), 3) for c in nc]

# --- Waist ring: the body seal closure, around the suit at waist height. -------------------------------------------
waist = P["waist_ring"]
wz = np.array((0.0, 0.0, J["pelvis"][2] + waist["above_pelvis"]))
up = np.array((0.0, 0.0, 1.0))
# Only the trunk: the arms hang beside the waist.
trunk = SUIT[np.abs(SUIT[:, 2] - wz[2]) < 0.06]
trunk = trunk[np.array(mpfb.bone_weights(bpy.data.objects["Body"], trunk, mpfb.ARM_BONES)) < 0.5]
wc = centroid(trunk, wz, up, 0.02, waist["reach"])
wr = section(trunk, wc, up, 0.03, waist["reach"])
band(metal, wc, up, rounded(-waist["height"] / 2, waist["height"] / 2, 0, -0.02, 0, waist["out"]), [wr])
band(red, wc, up, rounded(-0.006, 0.006, 0, 0.0, 0, waist["out"] + 0.004, r=0.002), [wr])
report["waist_radius"] = [round(float(wr.min()), 3), round(float(wr.max()), 3)]

for s, sign in (("l", 1), ("r", -1)):
    # --- Shoulder bearing: where the arm leaves the hard upper torso. ----------------------------------------------
    sh = P["shoulder_bearing"]
    arm = J[s + "-elbow"] - J[s + "-shoulder"]
    sc = J[s + "-shoulder"] + sh["t"] * arm
    sc = centroid(SUIT, sc, arm, 0.015, sh["reach"])
    sr = section(SUIT, sc, arm, 0.02, sh["reach"])
    band(white, sc, arm, rounded(-sh["width"] / 2, sh["width"] / 2, 0, -0.025, 0, sh["out"]), [sr])
    band(metal, sc, arm, rounded(sh["width"] / 2 - 0.002, sh["width"] / 2 + 0.008, 0, -0.02, 0, sh["out"] - 0.004,
                                 r=0.002), [sr])

    # --- Thigh bearing. ---------------------------------------------------------------------------------------------
    th = P["thigh_bearing"]
    leg = J[s + "-knee"] - J[s + "-upper-leg"]
    tc = J[s + "-upper-leg"] + th["t"] * leg
    tc = centroid(SUIT, tc, leg, 0.015, th["reach"], side=sign)
    tr = section(SUIT, tc, leg, 0.02, th["reach"], side=sign)
    band(metal, tc, leg, rounded(-th["width"] / 2, th["width"] / 2, 0, -0.02, 0, th["out"]), [tr])

    # --- Wrist disconnect and gauntlet: a colored ring over the end of the sleeve, a metal lip, and a short white
    # gauntlet that flares over the glove. Positions along the forearm are measured from the ring.
    g = P["gauntlet"]
    fore = J[s + "-hand"] - J[s + "-elbow"]
    unit = fore / np.linalg.norm(fore)
    glove = points_of("Glove_" + s.upper())
    cw = centroid(glove, J[s + "-elbow"] + g["ring"] * fore, fore, 0.01, 0.1)
    sleeve_at = J[s + "-elbow"] + (model.params["suit"]["sleeve_end"] - 0.03) * fore
    sleeve = section(SUIT, cw + ((sleeve_at - cw) @ unit) * unit, fore, 0.012, 0.13)
    wrist = section(glove, cw + g["cuff"] * unit, fore, 0.01, 0.1)
    outer = np.maximum(sleeve, wrist)
    radii = [wrist, outer]
    band(red if s == "l" else blue, cw, fore,
         [(-0.012, 0, -0.004), (-0.012, 1, g["ring_out"] - 0.004), (-0.009, 1, g["ring_out"]),
          (0.003, 1, g["ring_out"]), (0.006, 1, g["ring_out"] - 0.004), (0.006, 0, -0.004)], radii)
    band(metal, cw, fore,
         [(0.006, 0, -0.004), (0.006, 1, g["ring_out"] - 0.006), (0.013, 1, g["ring_out"] - 0.008),
          (0.013, 0, -0.004)], radii)
    band(white, cw, fore,
         [(0.013, 0, -0.004), (0.013, 1, g["ring_out"] - 0.01), (0.013 + g["length"], 0, g["flare"]),
          (0.013 + g["length"], 0, g["flare"] - 0.006), (0.02, 0, -0.004)], radii)

    # --- Boot cuff and sole. ---------------------------------------------------------------------------------------
    b = P["boot"]
    boot = points_of("Boot_" + s.upper())
    top = J[s + "-ankle"] + np.array((0.0, 0.0, model.params["suit"]["boot_top"]))
    cc = centroid(boot, top - np.array((0, 0, b["cuff_height"])), up, 0.01, 0.16)
    cuff = section(boot, cc, up, 0.015, 0.16)
    leg_r = section(SUIT, cc + np.array((0, 0, b["cuff_height"])), up, 0.02, 0.14, side=sign)
    band(white, cc, up, [(0.0, 0, -0.004), (0.0, 0, b["cuff_out"]), (b["cuff_height"] + 0.012, 0, b["cuff_out"]),
                         (b["cuff_height"] + 0.012, 1, 0.002), (b["cuff_height"], 1, -0.006)], [cuff, leg_r])
    # The rand: a rubber strip around the boot just above the sole, which hides the line where the boot meets it.
    rz = np.array((0.0, 0.0, model.params["suit"]["sole"] + b["rand_height"] / 2))
    foot_pts = boot[boot[:, 2] < model.params["suit"]["sole"] + b["rand_height"] + 0.02]
    rc = centroid(foot_pts, np.array((foot_pts[:, 0].mean(), foot_pts[:, 1].mean(), rz[2])), up, 0.01, 0.25)
    rand = section(boot, rc, up, b["rand_height"] / 2 + 0.004, 0.25)
    band(rubber, rc, up, rounded(-b["rand_height"] / 2 - 0.006, b["rand_height"] / 2, 0, -0.01, 0, b["rand_out"],
                                 r=0.003), [rand])
    # The sole: the outline of the foot, as a convex hull, pushed out a little and given the sole thickness.
    low = boot[boot[:, 2] < model.params["suit"]["sole"] + b["outline_height"]][:, :2]
    hull = convex_hull(low)
    centre = np.mean(hull, axis=0)
    outline = [centre + (np.array(p) - centre) * (1.0 + b["sole_grow"] / max(np.linalg.norm(np.array(p) - centre),
                                                                               1e-6)) for p in hull]
    h = model.params["suit"]["sole"] + b["sole_over"]
    bottom = [rubber.bm.verts.new((p[0], p[1], 0.0)) for p in outline]
    upper = [rubber.bm.verts.new((p[0], p[1], h)) for p in outline]
    rubber.bm.faces.new(bottom[::-1])
    rubber.bm.faces.new(upper)
    for i in range(len(outline)):
        j = (i + 1) % len(outline)
        rubber.bm.faces.new((bottom[i], bottom[j], upper[j], upper[i]))
    report["sole_" + s] = len(outline)

M = bpy.data.materials
metal.finish("Hard_Metal", M["M_Metal"], "Suit", smooth_angle=40.0)
white.finish("Hard_White", M["M_HardWhite"], "Suit", smooth_angle=40.0)
red.finish("Hard_Red", M["M_RingRed"], "Suit", smooth_angle=40.0)
blue.finish("Hard_Blue", M["M_RingBlue"], "Suit", smooth_angle=40.0)
rubber.finish("Soles", M["M_Rubber"], "Suit", smooth_angle=30.0)
result = report
