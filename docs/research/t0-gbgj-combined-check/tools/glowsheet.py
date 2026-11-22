# Tool code of t0-gbgj. glowsheet.py <out.png> <scale tag>: the glow-stays switch beside the fair switch (glow off) in the
# kept frames of the constant-scale runs: rows are film frames 180, 220 and 380 (20 and 60 draws after the lamps go off,
# 20 after they come on), columns are M2 glow stays, M2 glow off, X2 glow stays and X2 glow off (runs s/sc-<side>-<tag>
# and s/scg-<side>-<tag>), each at 1/3 scale. Below the sheet it prints, per frame, how many pixels differ between the
# two switches, their bounding box, and the largest difference in the jamb crop (x 1770 to 1808, y 495 to 585) and in
# the floor crop (x 0 to 450, y 840 to 1050), in 8-bit sRGB steps. m.sh runs it in $W/w.
import sys
import numpy as np
from PIL import Image, ImageDraw
out, tag = sys.argv[1], sys.argv[2]
FRAMES = (180, 220, 380)
load = lambda run, k: Image.open(f's/{run}/images/scale/cabin-{k:03d}.png').convert('RGB')
rows = []
for k in FRAMES:
    row = Image.new('RGB', (4 * 646, 360), (40, 40, 40))
    for i, (side, kind, run) in enumerate([(s, g, f'{p}-{s}-{tag}') for s in ('M2', 'X2') for g, p in (('glow stays', 'sc'), ('glow off', 'scg'))]):
        im = load(run, k).resize((640, 360)); ImageDraw.Draw(im).text((6, 6), f'{side} {kind}, {k}', fill=(255, 80, 80))
        row.paste(im, (646 * i, 0))
    rows.append(row)
    for side in ('M2', 'X2'):
        a, b = (np.asarray(load(f'{p}-{side}-{tag}', k)).astype(int) for p in ('sc', 'scg'))
        d = np.abs(a - b).max(axis=2); ys, xs = np.nonzero(d)
        box = f'x {xs.min()} to {xs.max()}, y {ys.min()} to {ys.max()}' if len(xs) else 'none'
        print(f'{side} {k}: {len(xs)} pixels differ, box {box}, largest {d.max()}; jamb crop largest {d[495:585, 1770:1808].max()}, floor crop largest {d[840:1050, 0:450].max()}')
sheet = Image.new('RGB', (rows[0].width, 366 * len(rows) - 6), (40, 40, 40))
for i, r in enumerate(rows): sheet.paste(r, (0, 366 * i))
sheet.save(out); print(out)
