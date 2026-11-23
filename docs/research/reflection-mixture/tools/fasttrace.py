# Tool code of ter-fe63: the specular signal's fast history and clamp in a region, from the accumulation trace's layers
# 3 (z: the fast history's luminance) and 6 (w: lag, the clamp's move over the box mean) that TERTIUS_PROBE_TRACE_DUMP
# wrote for draws [first, end) at the render size. Per take and region: the fast history's relative temporal deviation
# (per pixel, std over the draws over the mean, then the mean over the region's pixels), the share of pixel-draws whose
# clamp moved the slow history (lag above 1e-6), and the mean lag. Usage:
#   python3 -I fasttrace.py <first-end> <width>x<height> <label=take dir>...
import json, sys
from pathlib import Path
import numpy as np
a, b = map(int, sys.argv[1].split('-')); w, h = map(int, sys.argv[2].split('x'))
regions = {'sunlit patch': (66, 26, 280, 240), 'right wall': (866, 66, 1200, 333)}
out = {}
for arg in sys.argv[3:]:
    label, d = arg.split('='); d = Path(d)
    load = lambda k, layer: np.fromfile(d / f'census.t{k}.trace-{layer}.f32', np.float32).reshape(h, w, 4)
    fast = np.stack([load(k, 3)[..., 2] for k in range(a, b)]); lag = np.stack([load(k, 6)[..., 3] for k in range(a, b)])
    row = {}
    for name, (x0, y0, x1, y1) in regions.items():
        f = fast[:, y0:y1, x0:x1].astype(np.float64); g = lag[:, y0:y1, x0:x1]
        m = f.mean(0); ok = m > 0
        row[name] = {'fast history relative deviation': float((f.std(0)[ok] / m[ok]).mean()),
                     'clamp moves, share of pixel-draws': float((g > 1e-6).mean()), 'mean lag': float(g.mean())}
    out[label] = row
print(json.dumps(out, indent=1))
