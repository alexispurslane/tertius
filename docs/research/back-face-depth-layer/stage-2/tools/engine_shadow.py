#!/usr/bin/env python3
"""Throwaway (t1-3j2j): the engine's sun shadow at the points of a line cast, from the probe's dump of the cascades.

  python3 -I engine_shadow.py <dump folder> <scene.json> <cast.npz> [<out.npz>]

The dump is ShadowProbe's (stage-2/probe-shadow-leak.patch): `cascades.json` and `cascade-<c>.f32`, the reversed depth of
the four cascades of set 0, row by row from the top, at the capture of frame 239. The cast is sun_cast.py's npz, with a
world position and a geometric normal per point.

For each point this repeats sampleCascade of shadows.glsl on the CPU, with the dumped bias settings: the cascade by view
depth along the camera's forward axis, the lookup moved along the normal by normalOffsetTexels x texel x sin(angle to the
sun), the reference depth plus constantBiasTexels of depth, and the filter of the dump (0: 16 bilinear comparisons one
texel apart, 1: one at the lookup, 2: the 16 with their reach scaled to the sun's penumbra by a blocker search). Each bilinear comparison weighs the 2 x 2 texels around its place, a texel counting
1 where reference >= stored (GREATER_OR_EQUAL), with texels past the edge clamped. It ignores the blend into the next
cascade, which starts at view depth 45.6 m, and the device's sub-texel rounding.

The out npz holds per point: `cascade`, `lit` (the engine's lit share, 0 to 1), `u` and `v` (the lookup in texels of
its cascade), `reference` (the receiver's compared depth) and `stored` (the depth of the texel that holds the lookup),
and `gap_m`, (stored - reference) in m along the sun. It prints the points' summary.
"""
import json
import os
import sys
from pathlib import Path

import numpy as np

dump, scene_path, cast_path = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
out = sys.argv[4] if len(sys.argv) > 4 else None
fit = json.loads((dump / "cascades.json").read_text())
# ENGINE_FILTER=<0, 1 or 2> evaluates another filter on the same dump than the one that the run used.
fit["filter"] = int(os.environ.get("ENGINE_FILTER", fit["filter"]))
scene = json.loads(scene_path.read_text())
size = fit["mapSize"]
maps = [np.fromfile(dump / f"cascade-{c}.f32", "<f4").reshape(size, size) for c in range(4)]
# ENGINE_DILATE=k, a candidate fix with no build: each texel takes the greatest reversed depth, the caster nearest the sun,
# of the (2k + 1)^2 texels around it, so every tap within k texels of a caster's edge sees that caster.
dilate = int(os.environ.get("ENGINE_DILATE", "0"))
if dilate:
    for c in range(4):
        padded = np.pad(maps[c], dilate, mode="edge")
        out_map = maps[c].copy()
        for di in range(2 * dilate + 1):
            for dj in range(2 * dilate + 1):
                out_map = np.maximum(out_map, padded[di:di + size, dj:dj + size])
        maps[c] = out_map
forward = np.array(scene["camera"]["forward"], dtype=np.float64)
sun = next(light for light in scene["lights"] if light["kind"] == "point" and light["distance"] > 1e9)
to_sun = np.array(sun["position"], dtype=np.float64)
to_sun /= np.linalg.norm(to_sun)
cast = np.load(cast_path)


def matrix(c):
    return np.array(fit["cascades"][c]["viewProjectionRows"], dtype=np.float64).reshape(4, 4)


def compare(depth, reference, s, t):
    """One bilinear comparison at texel coordinates (s, t), the place in texels from the map's top left corner."""
    s0, t0 = np.floor(s - 0.5), np.floor(t - 0.5)
    fs, ft = s - 0.5 - s0, t - 0.5 - t0
    total = 0.0
    for dt, wt in ((0, 1 - ft), (1, ft)):
        for ds, ws in ((0, 1 - fs), (1, fs)):
            i = int(np.clip(t0 + dt, 0, size - 1))
            j = int(np.clip(s0 + ds, 0, size - 1))
            total += ws * wt * (1.0 if reference >= depth[i, j] else 0.0)
    return total


def engine(position, normal):
    depth = float(position @ forward)
    cascade = next((c for c in range(4) if depth < fit["cascades"][c]["split"]), None)
    if cascade is None:
        return None
    info = fit["cascades"][cascade]
    texel = info["texel"]
    ndotl = float(normal @ to_sun)
    shadow_normal = normal if ndotl >= 0 else -normal
    sin_theta = np.sqrt(max(1 - ndotl * ndotl, 0.0))
    lookup = position + shadow_normal * (fit["normalOffsetTexels"] * texel * sin_theta)
    clip = np.append(lookup, 1.0) @ matrix(cascade)
    u, v = (clip[0] * 0.5 + 0.5) * size, (clip[1] * 0.5 + 0.5) * size
    depth_per_uv = info["halfExtent"] / info["depthHalfRange"]
    reference = clip[2] + fit["constantBiasTexels"] * depth_per_uv / size
    m = maps[cascade]
    reach = 1.0
    if fit["filter"] == 2:
        # The blocker search of shadows.glsl's mode 2: the mean depth of the 5 x 5 texels around the lookup's texel that lie
        # nearer the sun than the reference, and the taps' reach scaled to the sun's penumbra over that gap.
        ci, cj = int(np.floor(v)), int(np.floor(u))
        window = m[max(ci - 2, 0):ci + 3, max(cj - 2, 0):cj + 3]
        blockers = window[window > reference]
        if blockers.size == 0:
            return cascade, 1.0, u, v, reference, float(m[ci, cj]), np.nan
        reach = float(np.clip((blockers.mean() - reference) * 16.0 * texel * size * 0.00467 / texel / 2.5, 0.0, 1.0))
    if fit["filter"] == 1:
        lit = compare(m, reference, u, v)
    else:
        lit = sum(compare(m, reference, u + (x - 1.5) * reach, v + (y - 1.5) * reach) for y in range(4) for x in range(4)) / 16
    stored = float(m[int(np.clip(np.floor(v), 0, size - 1)), int(np.clip(np.floor(u), 0, size - 1))])
    metres_per_depth = 2 * info["depthHalfRange"]
    return cascade, lit, u, v, reference, stored, (stored - reference) * metres_per_depth


rows = []
for p, n in zip(cast["position"], cast["normal"]):
    r = engine(p, n) if np.all(np.isfinite(p)) else None
    rows.append(r if r is not None else (-1, np.nan, np.nan, np.nan, np.nan, np.nan, np.nan))
rows = np.array(rows, dtype=np.float64)
names = ("cascade", "lit", "u", "v", "reference", "stored", "gap_m")
result = {k: rows[:, i] for i, k in enumerate(names)}
if out:
    np.savez(out, **result)
lit = result["lit"]
print(f"{len(lit)} points, cascades {sorted(set(result['cascade'].astype(int)))}, engine lit > 0 at {int(np.nansum(lit > 0))},"
      f" truth lit > 0 at {int(np.nansum(cast['share'] > 0))}")
