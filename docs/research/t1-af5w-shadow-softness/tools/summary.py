"""Tool code of t1-af5w. summary.py <take folder> [view...]: the record's table of item 1. Per view it groups the edge
pixels (edges.py's definition) by light class: the sun, the 1.2 m panels (emitter width of 1 m or more), the small
panels (0.15 x 0.5 m) and the spheres (point lamps of 0.1 m). Per class it prints, as one Markdown row, the edge pixels,
the 10th / 50th / 90th percentiles of the gap (m), the median true and rendered widths (m), and the percentiles of the
rendered width over the true width, and the share of the class's pixels whose ratio is below 0.5 (too sharp) and
above 2 (too soft)."""
import os, sys
import numpy as np

take = sys.argv[1]
views = sys.argv[2:] or sorted(v for v in os.listdir(take) if os.path.exists(f'{take}/{v}/edge.txt'))
print('| View | Light | Edge px | Gap p10 / p50 / p90, m | True p50, m | Rendered p50, m | Rendered / true p10 / p50 / p90 | Too sharp (< 0.5) | Too soft (> 2) |')
print('|---|---|---:|---|---:|---:|---|---:|---:|')
for v in views:
    w, h, _ = map(int, open(f'{take}/{v}/edge.txt').read().split())
    a = np.fromfile(f'{take}/{v}/edge-a.f32', np.float32).reshape(-1, 4)
    b = np.fromfile(f'{take}/{v}/edge-b.f32', np.float32).reshape(-1, 4)
    ids = a[:, 3]
    sun = ids < 0
    lamp = ids > 0
    angle = np.where(sun, b[:, 2], b[:, 2] / np.maximum(b[:, 1], 1e-6))
    true = a[:, 0] * angle
    ratio = a[:, 2] / np.maximum(true, 1e-6)
    classes = [('sun', sun), ('1.2 m panels', lamp & (b[:, 2] >= 1.0)),
               ('0.15 x 0.5 m panels', lamp & (b[:, 2] >= 0.3) & (b[:, 2] < 1.0)),
               ('0.1 m spheres', lamp & (b[:, 2] < 0.3))]
    for name, m in classes:
        n = int(m.sum())
        if n < 50:
            continue
        g = np.percentile(a[m, 0], (10, 50, 90))
        r = np.percentile(ratio[m], (10, 50, 90))
        print(f'| {v} | {name} | {n} | {g[0]:.2g} / {g[1]:.2g} / {g[2]:.2g} | {np.median(true[m]):.3f} | '
              f'{np.median(a[m, 2]):.3f} | {r[0]:.2g} / {r[1]:.2g} / {r[2]:.2g} | {np.mean(ratio[m] < 0.5):.0%} | '
              f'{np.mean(ratio[m] > 2):.0%} |')
