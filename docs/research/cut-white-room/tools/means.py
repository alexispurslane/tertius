# Tool code of t58-rcd6: joins the cut log (apply-cut.py) with the whole image's mean of each captured PNG, and prints
# one JSON object per drawn frame. The image's mean is the mean over all pixels and the three channels of the 8-bit
# sRGB capture, times 257, so that white is 65,535, the scale of the ticket's series. The PNGs are put in the run's
# capture order, which is the log's order (one capture per drawn frame): by the films' order that the script gives, then
# by frame. The order is not the names' order: cut.json films a, then x (the one chase draw), then b.
# Usage: python3 -I means.py <run dir> [the films in capture order, default still,a,b; cut.json: still,a,x,b]
import json, re, sys
from pathlib import Path
import numpy as np
from PIL import Image

run = Path(sys.argv[1])
lines = [l.split() for l in (run / 'cut.log').read_text().splitlines() if l.strip()]
order = (sys.argv[2] if len(sys.argv) > 2 else 'still,a,b').split(',')
pngs = sorted(run.glob('*.png'), key=lambda p: (order.index(p.stem.split('-')[0]), p.name))
if len(pngs) != len(lines):
    sys.exit(f'{len(pngs)} PNGs against {len(lines)} log lines in {run}')
for parts, png in zip(lines, pngs):
    row = {'frame': int(parts[0]), 'capture': png.stem}
    for kv in parts[1:]:
        k, v = kv.split('=', 1)
        try:
            row[k] = float(v)
        except ValueError:
            row[k] = v
    pixels = np.asarray(Image.open(png).convert('RGB'), dtype=np.float64)
    row['image'] = round(float(pixels.mean()) * 257, 1)
    print(json.dumps(row))
