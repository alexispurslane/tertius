# Tool code of t0-gumm. sweep.py <out prefix> [fair folder]: the sweep of catch-up shapes at the bay's start
# (queue-fair3.sh, and c3 (1, 3, 0) by queue-fair4.sh), from fairmeasure.py's fair.json files and the own-ray logs (scratch-ownray-log.patch, per draw
# "<frame> <draw index> <w> <h> <reflection own rays> <..2> <diffuse own rays> <..2>"). Per shape it prints:
#   - the boil of draws 1 to 8 (the mean of the broad crops), the grain of draws 3 to 8 and 9 to 15 (the mean of the floor
#     and the wall), and the same three on the narrow crop;
#   - the own rays per draw at the peak (diffuse and reflection, in thousands, and the diffuse peak's share of the render
#     size's pixels), and the catch-up rays per pixel: the sum over draws 0 to 59 of the diffuse own rays above the
#     settled mean (draws 150 to 199), over the render size's pixels.
# It draws <prefix>-chart.png (per draw: the grain of draws 0 to 15, the boil of draws 1 to 15, both broad and narrow, and
# the diffuse own rays' share of the pixels for draws 0 to 40; one line per shape) and <prefix>-strip.png (the floor and the
# right wall at draws 2 and 8, one column per shape, at 0.4 times size).
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

prefix = sys.argv[1]
root = Path(sys.argv[2] if len(sys.argv) > 2 else '/private/tmp/t0-gbgj/gumm-fair')
SHAPES = [('today', 'today (1, 8, 0)', 'bay-start-today', 'bay-own-today', (30, 30, 30)),
          ('none', 'none (0, 32, 0)', 'bay-start-none', 'bay-own-none', (220, 60, 60)),
          ('h16', 'h16 (0.5, 16, 0)', 'bay-start-h16', 'bay-start-h16', (40, 110, 200)),
          ('q32', 'q32 (0.25, 32, 0)', 'bay-start-q32', 'bay-start-q32', (40, 160, 80)),
          ('e32', 'e32 (0.125, 32, 0)', 'bay-start-e32', 'bay-start-e32', (200, 140, 20)),
          ('f32', 'f32 (0.5, 32, 1)', 'bay-start-f32', 'bay-start-f32', (140, 60, 180)),
          ('c3', 'c3 (1, 3, 0)', 'bay-start-c3', 'bay-start-c3', (0, 160, 160))]
GRAIN = ('floor', 'wall')
CROP = (500, 100, 1860, 1060)
rows = []
for code, label, folder, own, colour in SHAPES:
    f = root / folder / 'fair.json'
    if not f.exists():
        continue
    r = json.loads(f.read_text())
    broad = [c for c in r['boil'] if c != 'narrow']
    boil = np.mean([r['boil'][c] for c in broad], axis=0)
    grain = np.mean([r['grain'][c] for c in GRAIN], axis=0)
    nb, ng = np.array(r['boil']['narrow']), np.array(r['grain']['narrow'])
    logs = sorted((root / own).glob('ownrays-*.txt'))
    rays = np.loadtxt(logs[0], dtype=np.int64) if logs else None
    row = {'code': code, 'label': label, 'colour': colour, 'folder': root / folder, 'boil': boil, 'grain': grain,
           'nboil': nb, 'ngrain': ng, 'boil1to8': boil[:8].mean(), 'grain3to8': grain[3:9].mean(),
           'grain9to15': grain[9:16].mean(), 'nboil1to8': nb[:8].mean(), 'ngrain3to8': ng[3:9].mean(),
           'ngrain9to15': ng[9:16].mean()}
    if rays is not None:
        pixels = rays[0, 2] * rays[0, 3]
        diffuse, reflection = rays[:, 7].astype(float), rays[:, 4].astype(float)
        settled = diffuse[150:200].mean()
        row.update(peakDiffuse=diffuse[:60].max(), peakReflection=reflection[:60].max(), share=diffuse / pixels,
                   peakShare=diffuse[:60].max() / pixels, settledShare=settled / pixels,
                   catchUp=np.clip(diffuse[:60] - settled, 0, None).sum() / pixels)
    rows.append(row)

print('| Shape | Boil 1-8 | Grain 3-8 | Grain 9-15 | Narrow boil 1-8 | Narrow grain 3-8 | Narrow grain 9-15 | '
      'Peak diffuse own rays (k, share) | Peak reflection own rays (k) | Settled share | Catch-up rays per pixel |')
print('|' + '---|' * 11)
for r in rows:
    own = (f"{r['peakDiffuse'] / 1000:.0f} ({100 * r['peakShare']:.1f} %) | {r['peakReflection'] / 1000:.0f} | "
           f"{100 * r['settledShare']:.2f} % | {r['catchUp']:.2f}") if 'catchUp' in r else 'n/a | n/a | n/a | n/a'
    print(f"| {r['label']} | {r['boil1to8']:.3f} | {r['grain3to8']:.2f} | {r['grain9to15']:.2f} | {r['nboil1to8']:.3f} | "
          f"{r['ngrain3to8']:.2f} | {r['ngrain9to15']:.2f} | {own} |")

PW, PH, M = 330, 200, 50
im = Image.new('RGB', (5 * (PW + M) + M, PH + 120), 'white')
g = ImageDraw.Draw(im)
panes = [('grain %, draws 0-15', 'grain', 0, 16), ('boil %, draws 1-15', 'boil', 1, 16),
         ('narrow grain %, draws 0-15', 'ngrain', 0, 16), ('narrow boil %, draws 1-15', 'nboil', 1, 16),
         ('diffuse own rays, share of pixels, draws 0-40', 'share', 0, 41)]
for j, (title, key, first, end) in enumerate(panes):
    x0, y0 = M + j * (PW + M), 30
    series = [(r, r[key][:end - first] if key != 'share' else r[key][:end]) for r in rows if key in r]
    top = max(float(np.max(v)) for _, v in series) * 1.08
    g.rectangle([x0, y0, x0 + PW, y0 + PH], outline=(200, 200, 200))
    g.text((x0, y0 - 18), 'bay start: ' + title, fill='black')
    for frac in (0.5, 1.0):
        yy = y0 + PH * (1 - frac)
        g.line([(x0, yy), (x0 + PW, yy)], fill=(230, 230, 230))
        g.text((x0 - 44, yy - 6), f'{top * frac:.2f}', fill='black')
    for r, v in series:
        xs = range(first, first + len(v))
        g.line([(x0 + (d - first) / (end - 1 - first) * PW, y0 + PH * (1 - val / top)) for d, val in zip(xs, v)],
               fill=r['colour'], width=2)
    g.text((x0 + PW - 16, y0 + PH + 4), str(end - 1), fill='black')
for i, r in enumerate(rows):
    g.text((M + i * 250, PH + 70), r['label'], fill=r['colour'])
im.save(f'{prefix}-chart.png')

cw, ch = int((CROP[2] - CROP[0]) * 0.4), int((CROP[3] - CROP[1]) * 0.4)
strip = Image.new('RGB', (len(rows) * (cw + 8), 2 * (ch + 26)), 'white')
g = ImageDraw.Draw(strip)
for i, r in enumerate(rows):
    for k, d in enumerate((2, 8)):
        p = r['folder'] / f'f-{d:06d}.png'
        if not p.exists():
            continue
        x, y = i * (cw + 8), k * (ch + 26)
        strip.paste(Image.open(p).convert('RGB').crop(CROP).resize((cw, ch), Image.LANCZOS), (x, y + 22))
        g.text((x + 4, y + 5), f"{r['label']}, draw {d}, scale 2/3", fill='black')
strip.save(f'{prefix}-strip.png')
print(f'chart {prefix}-chart.png, strip {prefix}-strip.png')
