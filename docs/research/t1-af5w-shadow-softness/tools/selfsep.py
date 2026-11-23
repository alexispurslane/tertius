"""Tool code of t1-af5w. selfsep.py <take 1> <take 2> [view...]: for the edge pixels with a gap under 5 cm (take 1), how
well three readings of probe v2 (take 2) tell a receiver that shadows itself from a contact edge. The label comes from
neither reading: a pixel is in an area when more than half of the 9 x 9 pixels around it are such pixels too (a
self-shadowed surface fills areas), and on a line otherwise (a contact edge is a thin line). The readings: the blocking
texels' mean distance from the receiver's own plane, in texels at the receiver; sin of the angle to the light; the
window's depth spread (nearest to farthest texel), in texels. For each, the counts in the area and line groups below
and above a split, at the split that best separates them."""
import os, sys
import numpy as np
t1, t2 = sys.argv[1], sys.argv[2]
views = sys.argv[3:] or sorted(v for v in os.listdir(t2) if os.path.exists(f'{t2}/{v}/edge.txt'))


def box(m, r=4):
    c = np.pad(m.astype(np.int64), ((r + 1, r), (r + 1, r))).cumsum(0).cumsum(1)
    k = 2 * r + 1
    return c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]


for v in views:
    w, h, _ = map(int, open(f'{t1}/{v}/edge.txt').read().split())
    a1 = np.fromfile(f'{t1}/{v}/edge-a.f32', np.float32).reshape(h, w, 4)
    a2 = np.fromfile(f'{t2}/{v}/edge-a.f32', np.float32).reshape(h, w, 4)
    edge = a1[..., 3] != 0
    small = edge & (a1[..., 0] < 0.05)
    if small.sum() < 50:
        print(f'{v}: {int(small.sum())} pixels with a gap under 5 cm'); continue
    area = small & (box(small) > 40)
    line = small & ~area
    sun = a1[..., 3] < 0
    texel = np.maximum(np.where(sun, a1[..., 2] / 1.5, a1[..., 2] / 4), 1e-6)
    readings = {'plane residual, texels': a2[..., 0] / texel, 'sin theta': a2[..., 1], 'depth spread, texels': a2[..., 2] / texel}
    print(f'== {v}: gap < 5 cm {int(small.sum())} px: area {int(area.sum())}, line {int(line.sum())}')
    if area.sum() < 20 or line.sum() < 20:
        continue
    for name, r in readings.items():
        ra, rl = r[area], r[line]
        ra, rl = ra[np.isfinite(ra)], rl[np.isfinite(rl)]
        best = None
        for s in np.quantile(np.concatenate([ra, rl]), np.linspace(0.02, 0.98, 49)):
            for lo_is_area in (True, False):
                good = (np.sum(ra < s) + np.sum(rl >= s)) if lo_is_area else (np.sum(ra >= s) + np.sum(rl < s))
                if best is None or good > best[0]:
                    best = (good, s, lo_is_area)
        good, s, lo = best
        side = 'below' if lo else 'above'
        print(f'  {name:24s} area p10/50/90 {np.percentile(ra, [10, 50, 90]).round(2)}, line {np.percentile(rl, [10, 50, 90]).round(2)};'
              f' split {s:.3g}: area {side} {int(np.sum(ra < s) if lo else np.sum(ra >= s))}/{ra.size},'
              f' line other side {int(np.sum(rl >= s) if lo else np.sum(rl < s))}/{rl.size} ({good / (ra.size + rl.size):.0%} right)')
