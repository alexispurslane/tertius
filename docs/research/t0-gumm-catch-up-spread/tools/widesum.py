# Tool code of t0-gumm. widesum.py <out prefix> [fair folder]: the 36-tap fix's runs (queue-fair7.sh) against their controls, the
# bay's start: today (bay-check-wide: xwz without the fix, equal to today's cliff bit for bit), wide-today (xwz with
# TERTIUS_X_WIDE_FIX=1), d3 (bay-check-wide-d3), wide-d3, and none (bay-start-none). It prints per draw 0 to 15 the grain (the
# mean of the floor and the wall) and per draw 1 to 15 the boil (the mean of the broad crops), both also on the narrow crop,
# and their means over draws 1 to 4; and draws <prefix>-chart.png (those four per draw, one line per run) and
# <prefix>-strip.png (the floor and the right wall at draws 1, 2 and 4, one column per run of the first four).
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

prefix = sys.argv[1]
root = Path(sys.argv[2] if len(sys.argv) > 2 else '/private/tmp/t0-gbgj/gumm-fair')
RUNS = [('today', 'bay-check-wide', (30, 30, 30)), ('today, 36-tap fix', 'bay-wide-today', (40, 110, 200)),
        ('d3', 'bay-check-wide-d3', (150, 110, 60)), ('d3, 36-tap fix', 'bay-wide-d3', (40, 160, 80)),
        ('none', 'bay-start-none', (220, 60, 60))]
CROP = (500, 100, 1860, 1060)
rows = []
for label, folder, colour in RUNS:
    r = json.loads((root / folder / 'fair.json').read_text())
    broad = [c for c in r['boil'] if c != 'narrow']
    rows.append({'label': label, 'folder': root / folder, 'colour': colour,
                 'grain': np.mean([r['grain'][c] for c in ('floor', 'wall')], axis=0),
                 'boil': np.mean([r['boil'][c] for c in broad], axis=0)[:15],
                 'ngrain': np.array(r['grain']['narrow']), 'nboil': np.array(r['boil']['narrow'][:15])})
for key, first in (('grain', 0), ('boil', 1), ('ngrain', 0), ('nboil', 1)):
    print(f'{key} per draw from {first}:')
    for r in rows:
        v = r[key]
        d14 = v[1 - first:5 - first].mean()
        print(f'  {r["label"]:20s} draws 1-4 {d14:.3f} | ' + ' '.join(f'{x:.2f}' for x in v))
PW, PH, M = 330, 200, 50
im = Image.new('RGB', (4 * (PW + M) + M, PH + 110), 'white')
g = ImageDraw.Draw(im)
for j, (key, title, first) in enumerate((('grain', 'grain %, draws 0-15', 0), ('boil', 'boil %, draws 1-15', 1),
                                         ('ngrain', 'narrow grain %, draws 0-15', 0), ('nboil', 'narrow boil %, draws 1-15', 1))):
    x0, y0 = M + j * (PW + M), 30
    top = max(float(r[key].max()) for r in rows) * 1.08
    g.rectangle([x0, y0, x0 + PW, y0 + PH], outline=(200, 200, 200))
    g.text((x0, y0 - 18), 'bay start: ' + title, fill='black')
    for frac in (0.5, 1.0):
        yy = y0 + PH * (1 - frac)
        g.line([(x0, yy), (x0 + PW, yy)], fill=(230, 230, 230))
        g.text((x0 - 44, yy - 6), f'{top * frac:.2f}', fill='black')
    for r in rows:
        v = r[key]
        g.line([(x0 + (first + i - first) / (len(v) - 1) * PW, y0 + PH * (1 - val / top)) for i, val in enumerate(v)],
               fill=r['colour'], width=2)
for i, r in enumerate(rows):
    g.text((M + i * 300, PH + 70), r['label'], fill=r['colour'])
im.save(f'{prefix}-chart.png')
cw, ch = int((CROP[2] - CROP[0]) * 0.4), int((CROP[3] - CROP[1]) * 0.4)
shown = rows[:4]
strip = Image.new('RGB', (len(shown) * (cw + 8), 3 * (ch + 26)), 'white')
g = ImageDraw.Draw(strip)
for i, r in enumerate(shown):
    for k, d in enumerate((1, 2, 4)):
        x, y = i * (cw + 8), k * (ch + 26)
        strip.paste(Image.open(r['folder'] / f'f-{d:06d}.png').convert('RGB').crop(CROP).resize((cw, ch), Image.LANCZOS), (x, y + 22))
        g.text((x + 4, y + 5), f"{r['label']}, draw {d}, scale 2/3", fill='black')
strip.save(f'{prefix}-strip.png')
print(f'chart {prefix}-chart.png, strip {prefix}-strip.png')
