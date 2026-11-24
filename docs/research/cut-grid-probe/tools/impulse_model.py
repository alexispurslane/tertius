#!/usr/bin/env python3
# Tool code of t1-3er4: a CPU replica of step 3 of accumulate_spatial.comp under the probe's stencils, on one surface with
# equal history lengths (a plain mean of 9 taps), on a periodic domain of 256 x 256 render pixels. Stencil A uses the
# shader's pcg3d hash of the 8 x 8 block; stencil B uses independent uniform bytes in place of the blue-noise mask (the
# GPU impulse test reads the real mask). For every source pixel q it counts the receivers p whose taps read q: the
# source's energy E(q) = receivers / 9, which is exactly 1 for a translation-invariant stencil.
# Usage: python3 -I impulse_model.py <out json>
import json, sys
import numpy as np

S4 = [[(4,0),(0,4),(4,4),(4,-4)],[(4,1),(-1,4),(3,5),(5,-3)],[(4,2),(-2,4),(2,5),(5,-2)],[(3,2),(-2,3),(1,6),(6,-1)],
      [(3,3),(-3,3),(0,6),(6,0)],[(2,3),(-3,2),(-1,6),(6,1)],[(2,4),(-4,2),(-2,5),(5,2)],[(1,4),(-4,1),(-3,5),(5,3)]]
S2 = [[(2,0),(0,2),(2,2),(2,-2)],[(2,1),(-1,2),(1,3),(3,-1)],[(1,1),(-1,1),(0,3),(3,0)],[(2,-1),(1,2),(3,1),(1,-3)]]
N = 256

def pcg3d(x, y, z):
    M = np.uint64(0xFFFFFFFF)
    v = [np.asarray(a, dtype=np.uint64) for a in (x, y, z)]
    v = [(a * 1664525 + 1013904223) & M for a in v]
    v[0] = (v[0] + v[1] * v[2]) & M; v[1] = (v[1] + v[2] * v[0]) & M; v[2] = (v[2] + v[0] * v[1]) & M
    v = [a ^ (a >> np.uint64(16)) for a in v]
    v[0] = (v[0] + v[1] * v[2]) & M; v[1] = (v[1] + v[2] * v[0]) & M; v[2] = (v[2] + v[0] * v[1]) & M
    return v[0]

def rows(mode, spacing, draw=0, seed=1):
    ys, xs = np.mgrid[0:N, 0:N]
    if mode == "baseline" or spacing == 1: return np.zeros((N, N), int)
    if mode == "a":
        pick = (pcg3d(xs >> 3, ys >> 3, np.full_like(xs, draw ^ 0x41)) >> np.uint64(30)).astype(int)
        return pick * 2 if spacing == 4 else pick
    byte = np.random.default_rng(seed).integers(0, 256, (N, N))
    return byte >> 5 if spacing == 4 else byte >> 6

def offsets(row, spacing):
    if spacing == 1: reps = [(1,0),(0,1),(1,1),(1,-1)]
    else: reps = (S4 if spacing == 4 else S2)[row]
    return [(0, 0)] + [p for d in reps for p in (d, (-d[0], -d[1]))]

def apply(x, mode, spacing, draw=0):
    r = rows(mode, spacing, draw)
    out = x.copy()
    ys, xs = np.mgrid[0:N, 0:N]
    for row in np.unique(r):
        m = r == row
        for dx, dy in offsets(row, spacing)[1:]:
            out[m] += x[(ys[m] + dy) % N, (xs[m] + dx) % N]
    return out / 9.0

def energy(mode, spacing):
    # E(q): scatter each receiver's 9 reads.
    r = rows(mode, spacing)
    E = np.zeros((N, N)); ys, xs = np.mgrid[0:N, 0:N]
    for row in np.unique(r):
        m = r == row
        for dx, dy in offsets(row, spacing):
            np.add.at(E, ((ys[m] + dy) % N, (xs[m] + dx) % N), 1 / 9)
    return E

def radius_and_isotropy(mode, spacing):
    r = rows(mode, spacing); w = {}
    for row, count in zip(*np.unique(r, return_counts=True)):
        for o in offsets(row, spacing)[1:]: w[o] = w.get(o, 0) + count
    o = np.array(list(w)); c = np.array(list(w.values()), float); c /= c.sum()
    r2 = (o ** 2).sum(1); ang = np.arctan2(o[:, 1], o[:, 0])
    return {"rms_radius_px": float(np.sqrt((c * r2).sum())), "distinct_offsets": len(w),
            "second_moment_anisotropy": float(abs((c * (o[:, 0] ** 2 - o[:, 1] ** 2)).sum()) / (c * r2).sum()),
            "fourth_order_anisotropy_c4": float(abs((c * r2 * np.exp(4j * ang)).sum()) / (c * r2).sum())}

def ac(img, lag):
    a = img - img.mean()
    return float(0.5 * ((a * np.roll(a, -lag, 1)).mean() + (a * np.roll(a, -lag, 0)).mean()) / (a * a).mean())

out = {}
noise = np.random.default_rng(7).standard_normal((N, N))
for spacing in (4, 2):
    for mode in ("baseline", "a", "b"):
        E = energy(mode, spacing)
        f = apply(noise, mode, spacing)
        out[f"{mode}-s{spacing}"] = {
            "energy_mean": float(E.mean()), "energy_std": float(E.std()), "energy_min": float(E.min()),
            "energy_max": float(E.max()), "sources_below_half_energy": float((E < 0.5).mean()),
            "sources_above_1p5_energy": float((E > 1.5).mean()),
            **radius_and_isotropy(mode, spacing),
            "white_noise_autocorrelation": {f"lag{l}": round(ac(f, l), 3) for l in (1, 2, 3, 4, 8)},
            "white_noise_variance_ratio": float(f.var() / noise.var()),
        }
        if mode == "a":
            # Energy of sources by distance from the block border, to find seams.
            ys, xs = np.mgrid[0:N, 0:N]
            dist = np.minimum(np.minimum(xs % 8, 7 - xs % 8), np.minimum(ys % 8, 7 - ys % 8))
            out[f"{mode}-s{spacing}"]["energy_std_by_border_distance"] = [float(E[dist == k].std()) for k in range(4)]
        print(mode, spacing, {k: v for k, v in out[f"{mode}-s{spacing}"].items() if k != "energy_std_by_border_distance"})
json.dump(out, open(sys.argv[1], "w"), indent=1)
