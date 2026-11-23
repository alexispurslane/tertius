"""Tool code of t1-af5w. widelamps.py <take folder>: per view, the lamps with an edge whose true width is at least twice
today's rendered width at a tenth or more of their edge pixels (gaps of 5 cm or more), with the share, the pixels, the
emitter's widths and the median gap and caster distance there."""
import os, sys
import numpy as np
take = sys.argv[1]
for v in sorted(os.listdir(take)):
    if not os.path.exists(f'{take}/{v}/edge.txt'):
        continue
    w, h, _ = map(int, open(f'{take}/{v}/edge.txt').read().split())
    a = np.fromfile(f'{take}/{v}/edge-a.f32', np.float32).reshape(-1, 4)
    b = np.fromfile(f'{take}/{v}/edge-b.f32', np.float32).reshape(-1, 4)
    rows = []
    for lid in np.unique(a[:, 3]):
        if lid <= 0:
            continue
        m = (a[:, 3] == lid) & (a[:, 0] >= 0.05)
        if m.sum() < 50:
            continue
        true = a[m, 0] * b[m, 2] / np.maximum(b[m, 1], 1e-6)
        wide = true >= 2 * a[m, 2]
        if wide.mean() >= 0.1:
            rows.append(f'lamp {int(lid - 1)}: {wide.mean():.2f} of {int(m.sum())} px, emitter {b[m, 3].max():.3f} x {b[m, 2].max():.3f} m,'
                        f' gap p50 {np.median(a[m, 0][wide]):.2f} m, caster p50 {np.median(b[m, 1][wide]):.2f} m,'
                        f' true p50 {np.median(true[wide]):.2f} m against rendered {np.median(a[m, 2][wide]):.3f} m')
    print(f'{v}: {len(rows)} lamps'); [print('   ', r) for r in rows]
