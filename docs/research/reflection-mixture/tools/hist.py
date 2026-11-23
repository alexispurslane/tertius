# Tool code of t1-nyjl: the slow diffuse history's length L in the frozen bay's four regions, from the census dump
# census.d<draw>.slow-diffuse (alpha = L in draws, render pixels at 2/3). Per take: each region's share of pixels by L in
# bins that show the shape at the take's cap, the mean L, and the share under three quarters of the cap. With a main take
# and takes with one writer off, the writer of each short pixel (L under cap - 0.5): a pixel is the writer's when it is
# short in main and not short with that writer off. Usage:
#   python3 -I hist.py <draw> <cap> <main take> [<label=take with a writer off>...]
import json, sys
from pathlib import Path
import numpy as np
draw, cap = int(sys.argv[1]), float(sys.argv[2])
regions = {'sunlit patch': (66, 26, 280, 240), 'back wall below the slot': (373, 280, 653, 373),
           'floor': (400, 433, 800, 633), 'right wall': (866, 66, 1200, 333)}
def lengths(take):
    take = Path(take)
    w, h, c = json.loads((take / f'census.d{draw}.dump.json').read_text())['slow-diffuse']
    return np.fromfile(take / f'census.d{draw}.slow-diffuse.f32', np.float32).reshape(h, w, c)[..., 3]
edges = [0, 8, 16, 24, 31.5, 1e9] if cap <= 32 else [0, 16, 32, 64, 96, cap - 0.5, 1e9] if cap > 64 else [0, 8, 16, 32, 48, cap - 0.5, 1e9]
main = lengths(sys.argv[3])
others = {a.split('=')[0]: lengths(a.split('=')[1]) for a in sys.argv[4:]}
out = {}
for name, (x0, y0, x1, y1) in regions.items():
    L = main[y0:y1, x0:x1]; L = L[L > 0]
    row = {'pixels': int(L.size), 'mean L': float(L.mean()), 'share under 3/4 of the cap': float((L < 0.75 * cap).mean()),
           'bins': {f'{lo:g}-{hi:g}' if hi < 1e8 else 'at the cap': float(((L >= lo) & (L < hi)).mean())
                    for lo, hi in zip(edges[:-1], edges[1:])}}
    short = (main[y0:y1, x0:x1] > 0) & (main[y0:y1, x0:x1] < cap - 0.5)
    row['short share'] = float(short.sum() / max((main[y0:y1, x0:x1] > 0).sum(), 1))
    for label, o in others.items():
        Lo = o[y0:y1, x0:x1]
        row[f'short and lengthened with {label} off'] = float((short & (Lo >= cap - 0.5)).sum() / max(short.sum(), 1))
        row[f'mean L with {label} off'] = float(Lo[Lo > 0].mean())
    out[name] = row
print(json.dumps(out, indent=1))
