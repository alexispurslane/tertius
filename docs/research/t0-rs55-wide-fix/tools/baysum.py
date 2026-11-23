# Tool code of t0-rs55. baysum.py <out prefix> [fair folder]: the bay's start of queue-bay.sh, today (m0z) against the wide fix
# (m1z), each at the world's tick 420, 421 and 422 (+0, +1, +2), for the whole picture ("all") and the reflections alone
# (TERTIUS_PROBE_SIGNAL=3), from fairmeasure.py's fair.json (grain per crop of draws 0 to 15, boil per crop of draws 1 to 59).
# The measures, as t0-gumm's sweep table names them: the grain (the mean of the floor and the wall), the boil (the mean of
# the broad crops), the same on the narrow crop, over draws 1 to 4, the boil of draw 1, the boil of draws 1 to 8 and 1 to 59,
# the grain of draws 3 to 8 and 9 to 15. Per signal and measure it prints today's and the wide fix's mean over the three
# runs, the spread (the largest range of the three runs of one code), and the wide fix's change per run (same tick). It
# draws <prefix>-<signal>-chart.png (per draw, the mean of the three runs: grain 0-15, boil 1-15, narrow grain and boil;
# black today, blue the wide fix) and <prefix>-<signal>-strip.png (the floor and the right wall at draws 1, 2 and 4 of the
# run at +0, today | wide fix, at 0.4 times size).
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

prefix = sys.argv[1]
root = Path(sys.argv[2] if len(sys.argv) > 2 else '/private/tmp/t0-gbgj/gumm-fair')
CODES = [('today', (30, 30, 30)), ('wide', (40, 110, 200))]
CROP = (500, 100, 1860, 1060)


def load(signal, code, t):
    r = json.loads((root / f'rs-bay-{signal}-{code}+{t}' / 'fair.json').read_text())
    broad = [c for c in r['boil'] if c != 'narrow']
    return {'grain': np.mean([r['grain'][c] for c in ('floor', 'wall')], axis=0),
            'boil': np.concatenate([[np.nan], np.mean([r['boil'][c] for c in broad], axis=0)]),
            'ngrain': np.array(r['grain']['narrow']),
            'nboil': np.concatenate([[np.nan], np.array(r['boil']['narrow'])])}


MEASURES = [('grain 1-4', 'grain', 1, 5), ('boil 1-4', 'boil', 1, 5), ('narrow grain 1-4', 'ngrain', 1, 5),
            ('narrow boil 1-4', 'nboil', 1, 5), ('boil of draw 1', 'boil', 1, 2), ('narrow boil of draw 1', 'nboil', 1, 2),
            ('boil 1-8', 'boil', 1, 9), ('boil 1-59', 'boil', 1, 60), ('grain 0', 'grain', 0, 1),
            ('grain 3-8', 'grain', 3, 9), ('grain 9-15', 'grain', 9, 16), ('narrow boil 1-8', 'nboil', 1, 9),
            ('narrow grain 3-8', 'ngrain', 3, 9), ('narrow grain 9-15', 'ngrain', 9, 16)]
for signal in ('all', 'reflections'):
    try:
        runs = {(c, t): load(signal, c, t) for c, _ in CODES for t in (0, 1, 2)}
    except FileNotFoundError as missing:
        print(f'== {signal}: missing {missing.filename}')
        continue
    print(f'== {signal}: measure | today | wide | spread | wide - today per run (+0, +1, +2)')
    for name, key, a, b in MEASURES:
        v = {(c, t): float(np.mean(runs[c, t][key][a:b])) for c, _ in CODES for t in (0, 1, 2)}
        today = np.mean([v['today', t] for t in (0, 1, 2)]); wide = np.mean([v['wide', t] for t in (0, 1, 2)])
        spread = max(max(v[c, t] for t in (0, 1, 2)) - min(v[c, t] for t in (0, 1, 2)) for c, _ in CODES)
        d = [v['wide', t] - v['today', t] for t in (0, 1, 2)]
        print(f'  {name} | {today:.3f} | {wide:.3f} ({100 * (wide / today - 1):+.0f} %) | {spread:.3f} | '
              + ', '.join(f'{x:+.3f}' for x in d))
    for key, title in (('grain', 'grain per draw 0-15'), ('boil', 'boil per draw 1-15')):
        for c, _ in CODES:
            m = np.mean([runs[c, t][key][:16] for t in (0, 1, 2)], axis=0)
            print(f'  {title}, {c}: ' + ' '.join('-' if np.isnan(x) else f'{x:.2f}' for x in m))
    PW, PH, M = 330, 200, 50
    im = Image.new('RGB', (4 * (PW + M) + M, PH + 90), 'white'); g = ImageDraw.Draw(im)
    for j, (key, title, first) in enumerate((('grain', 'grain %, draws 0-15', 0), ('boil', 'boil %, draws 1-15', 1),
                                             ('ngrain', 'narrow grain %, draws 0-15', 0), ('nboil', 'narrow boil %, draws 1-15', 1))):
        x0, y0 = M + j * (PW + M), 30
        series = [(colour, np.mean([runs[c, t][key][first:16] for t in (0, 1, 2)], axis=0)) for c, colour in CODES]
        top = max(float(np.nanmax(s)) for _, s in series) * 1.08
        g.rectangle([x0, y0, x0 + PW, y0 + PH], outline=(200, 200, 200))
        g.text((x0, y0 - 18), f'bay start, {signal}: {title}', fill='black')
        for frac in (0.5, 1.0):
            yy = y0 + PH * (1 - frac)
            g.line([(x0, yy), (x0 + PW, yy)], fill=(230, 230, 230)); g.text((x0 - 44, yy - 6), f'{top * frac:.2f}', fill='black')
        for colour, s in series:
            g.line([(x0 + i / (len(s) - 1) * PW, y0 + PH * (1 - val / top)) for i, val in enumerate(s)], fill=colour, width=2)
        g.text((x0, y0 + PH + 4), str(first), fill='black'); g.text((x0 + PW - 12, y0 + PH + 4), '15', fill='black')
    g.text((M, PH + 60), "black today's fix (main 86ae0fbc8), blue the wide fix; the mean of 3 runs (ticks 420, 421, 422)", fill='black')
    im.save(f'{prefix}-{signal}-chart.png')
    cw, ch = int((CROP[2] - CROP[0]) * 0.4), int((CROP[3] - CROP[1]) * 0.4)
    strip = Image.new('RGB', (2 * (cw + 8), 3 * (ch + 26)), 'white'); g = ImageDraw.Draw(strip)
    for i, (c, _) in enumerate(CODES):
        for k, d in enumerate((1, 2, 4)):
            x, y = i * (cw + 8), k * (ch + 26)
            p = root / f'rs-bay-{signal}-{c}+0' / f'f-{d:06d}.png'
            if p.exists():
                strip.paste(Image.open(p).convert('RGB').crop(CROP).resize((cw, ch), Image.LANCZOS), (x, y + 22))
            g.text((x + 4, y + 5), f"{'today' if c == 'today' else 'wide fix'}, {signal}, draw {d}, scale 2/3", fill='black')
    strip.save(f'{prefix}-{signal}-strip.png')
print('charts and strips written')
