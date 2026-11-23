"""Tool code of t1-af5w. lamptable.py <take 1> <take 2> [view...]: per view, each lamp with an edge whose true width is at
least twice today's rendered width at a tenth or more of its edge pixels (gaps of 5 cm or more, take 1), with its
emitter's shape and size and its shadow tile views (take 2's lights.txt), and its shadow lookups in the view: the pixels
whose direct pass looked the lamp's shadow up (take 2's mask; a key light only)."""
import os, sys
import numpy as np
t1, t2 = sys.argv[1], sys.argv[2]
views = sys.argv[3:] or sorted(v for v in os.listdir(t2) if os.path.exists(f'{t2}/{v}/edge.txt'))
KIND = {0: 'point', 1: 'spot', 2: 'tube', 3: 'rect'}
for v in views:
    w, h, _ = map(int, open(f'{t1}/{v}/edge.txt').read().split())
    a = np.fromfile(f'{t1}/{v}/edge-a.f32', np.float32).reshape(-1, 4)
    b = np.fromfile(f'{t1}/{v}/edge-b.f32', np.float32).reshape(-1, 4)
    mask = np.fromfile(f'{t2}/{v}/edge-b.f32', np.float32).reshape(-1, 4).astype(np.int64)
    lights = {int(f[0]): f for f in (l.split() for l in open(f'{t2}/{v}/lights.txt'))}
    rows = []
    for lid in np.unique(a[:, 3]):
        if lid <= 0:
            continue
        i = int(lid - 1)
        m = (a[:, 3] == lid) & (a[:, 0] >= 0.05)
        if m.sum() < 50:
            continue
        true = a[m, 0] * b[m, 2] / np.maximum(b[m, 1], 1e-6)
        share = np.mean(true >= 2 * a[m, 2])
        if share < 0.1:
            continue
        f = lights[i]
        kind = KIND.get(int(float(f[1])), f[1])
        r, hw, hh = float(f[2]), float(f[3]), float(f[4])
        size = {'rect': f'{2 * hw:.2f} x {2 * hh:.2f} m', 'tube': f'{2 * hw:.2f} m long, r {r:.3f} m'}.get(kind, f'r {r:.3f} m')
        lookups = int(np.count_nonzero((mask[:, i // 11] >> (i % 11)) & 1)) if i < 44 else -1
        rows.append(f'lamp {i}: {kind} {size}, tile views {f[7]}, wide at {share:.0%} of {int(m.sum())} edge px,'
                    f' lookups {lookups} px ({lookups / (w * h):.1%} of the view)')
    print(f'{v} ({w} x {h}): {len(rows)} lamps'); [print('   ', r) for r in rows]
