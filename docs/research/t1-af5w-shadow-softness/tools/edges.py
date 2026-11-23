"""Tool code of t1-af5w. edges.py <take folder> [view...]: the shadow edges of each view of a probe take (probe.sh), per
light. An edge pixel is a pixel inside today's rendered ramp of a light (the probe's share strictly between 0 and 1); a
pixel on the ramps of two lights counts for the first in the light loop. Per light it prints the edge pixels, and the
10th, 50th and 90th percentiles of: the gap (m, from the mean of the blockers in the filter's 5 x 5 texels), the gap from
the window's nearest caster (m), the rendered width (m), the light's angular size (rad), the true width (m: the gap times
the angular size; for a lamp the emitter's largest width over the caster's distance), and the rendered width over the
true width. It writes <take>/edges.json with the same numbers and the per-pixel ratios' histogram."""
import json, os, sys
import numpy as np

take = sys.argv[1]
views = sys.argv[2:] or sorted(v for v in os.listdir(take) if os.path.exists(f'{take}/{v}/edge.txt'))
P = (10, 50, 90)
result = {}
for v in views:
    w, h, _ = map(int, open(f'{take}/{v}/edge.txt').read().split())
    a = np.fromfile(f'{take}/{v}/edge-a.f32', np.float32).reshape(h, w, 4)
    b = np.fromfile(f'{take}/{v}/edge-b.f32', np.float32).reshape(h, w, 4)
    ids = a[..., 3]
    result[v] = {'size': [w, h], 'lights': {}}
    print(f'== {v} ({w} x {h}): {np.count_nonzero(ids)} edge pixels')
    for lid in sorted(set(np.unique(ids)) - {0.0}):
        m = ids == lid
        sun = lid < 0
        gap, near, rendered = a[..., 0][m], a[..., 1][m], a[..., 2][m]
        if sun:
            angle = b[..., 2][m]
            extra = {'cascade': np.bincount(b[..., 1][m].astype(int), minlength=4).tolist()}
        else:
            caster = b[..., 1][m]
            angle = b[..., 2][m] / np.maximum(caster, 1e-6)
            extra = {'caster_m': np.percentile(caster, P).round(3).tolist(),
                     'emitter_m': [float(b[..., 3][m].max()), float(b[..., 2][m].max())]}
        true = gap * angle
        ratio = rendered / np.maximum(true, 1e-6)
        name = f'sun {int(-lid - 1)}' if sun else f'lamp {int(lid - 1)}'
        row = {'pixels': int(m.sum()), 'gap': np.percentile(gap, P).round(3).tolist(),
               'gap_nearest': np.percentile(near, P).round(3).tolist(),
               'rendered': np.percentile(rendered, P).round(4).tolist(),
               'angle': np.percentile(angle, P).round(5).tolist(),
               'true': np.percentile(true, P).round(4).tolist(),
               'ratio': np.percentile(ratio, P).round(2).tolist(),
               'self_share': float(np.mean(gap < 0.05)), **extra,
               'hist': np.histogram(np.log2(np.clip(ratio, 1 / 1024, 1024)), bins=40, range=(-10, 10))[0].tolist()}
        result[v]['lights'][name] = row
        if row['pixels'] >= 20:
            print(f'  {name:9s} px {row["pixels"]:6d} gap {row["gap"]} near {row["gap_nearest"]} rendered {row["rendered"]}'
                  f' angle {row["angle"]} true {row["true"]} ratio {row["ratio"]} gap<5cm {row["self_share"]:.2f} {extra}')
json.dump(result, open(f'{take}/edges.json', 'w'), indent=1)
