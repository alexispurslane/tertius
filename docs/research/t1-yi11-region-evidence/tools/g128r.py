# Tool code of t1-yi11. g128r.py <film> [column...]: the numbers and 1:1 crops of the validation of the selected rule
# g128r (cap 128, the cause gate of 128 draws, the signed rank test while the gate is closed) against its comparison
# columns, from the films of films.sh in /private/tmp/t1-yi11/films. <film> is one of:
#   settled: rect-bay-settled (rbs-<column>, film draws 0 to 179 = log draws 200 to 379), columns a bl gr g128r g;
#   turned:  the same turned 90 degrees left (rbst-<column>), columns bl gr g128r g;
#   switch:  the viewable cabin switch (swf-<column>, film draws 150 to 459), columns b gr g128r g.
# The units: the mean 8-bit gray of the kept PNGs (1920 x 1080 output, drawn at 1280 x 720). The tiles are the anchors'
# spacing in output pixels: an anchor every 8 blocks of 8 render pixels, 64 render pixels, 96 output pixels.
# For each column it writes into <record>/g128r-<film>.json:
#   - curves: per draw, the mean gray of the film's boxes;
#   - tiles: per draw, the largest difference of a 96 x 96 tile's mean gray between g128r and each other column (a
#     staircase along the anchors' blocks shows as a large tile difference), with its tile;
#   - tilesAgainstB: the same for every column against column b (bl in the rect-bay takes), with the count of draws
#     whose largest tile difference is over 4 and over 8 gray;
#   - flicker: per column and box, the largest absolute second difference in time of a box's mean gray over the film,
#     |m(k + 1) - 2 m(k) + m(k - 1)|, with its draw (a smooth decay gives a small one, a flicker a large one), and the
#     same for the largest tile, and for the switch the same inside the windows after each cause leaves the rays (film
#     draws 205 to 295 and 405 to 459), where the switch's own steps do not count;
#   - log: per draw, from each column's region-summary.json (regionlog.py), the anchors that answer yes and the anchors
#     whose gate is open (cause age under 36 or under 128).
# With CROPS="d1,d2,..." it writes pictures/<name>-g128r-crops.png: 1:1 crops of the box CROPBOX="x0,y0,x1,y1" for each
# column and draw, the columns side by side, one row per draw. CROPTAG=<suffix> adds a suffix to that name, and
# CROPCOLS="c1,c2,..." picks its columns (default: all). Run it in the dev shell.
import json
import os
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

F = Path('/private/tmp/t1-yi11/films')
REC = Path(__file__).resolve().parent.parent
TILE = 96
FILMS = {
    'settled': {'prefix': 'rbs', 'glob': 'm*.png', 'first': 0, 'log0': 200, 'name': 'rect-bay-settled',
                'columns': ['a', 'bl', 'gr', 'g128r', 'g'],
                'boxes': {'whole': (0, 0, 1920, 1080), 'right wall': (1100, 100, 1800, 800),
                          'floor': (330, 520, 1100, 1000), 'left wall': (0, 150, 220, 1000)}},
    'turned': {'prefix': 'rbst', 'glob': 'm*.png', 'first': 0, 'log0': 200, 'name': 'rect-bay-turned',
               'columns': ['bl', 'gr', 'g128r', 'g'],
               'boxes': {'whole': (0, 0, 1920, 1080), 'wall ahead': (0, 0, 1920, 500), 'floor': (0, 700, 1920, 1080)}},
    'switch': {'prefix': 'swf', 'glob': 'f-*.png', 'first': 150, 'log0': None, 'name': 'cabin-switch',
               'columns': ['b', 'gr', 'g128r', 'g'],
               'boxes': {'whole': (0, 0, 1920, 1080), 'floor': (0, 540, 1920, 1080), 'upper half': (0, 0, 1920, 540)},
               'windows': [(205, 295), (405, 459)]},
}
LABELS = {'a': 'a: main, cap 32', 'b': 'b: cap 128, today\'s test', 'bl': 'b: cap 128, today\'s test',
          'gr': 'g36+r: gate 36, rank when closed', 'g128r': 'g128+r: gate 128, rank when closed',
          'g': 'g: gate 128, no test when closed'}

film = sys.argv[1]
spec = FILMS[film]
columns = sys.argv[2:] or spec['columns']


def grays(col):
    return [np.asarray(Image.open(p).convert('L'), np.float64)
            for p in sorted((F / f"{spec['prefix']}-{col}").glob(spec['glob']))]


def tiles(g):
    h, w = g.shape
    return g[:h - h % TILE, :w].reshape(h // TILE, TILE, w // TILE, TILE).mean(axis=(1, 3))


frames = {col: grays(col) for col in columns}
count = min(len(v) for v in frames.values())
first = spec['first']
out = {'film': spec['name'], 'firstDraw': first, 'columns': columns, 'curves': {}, 'tiles': {}, 'flicker': {}, 'log': {}}
for col in columns:
    out['curves'][col] = {name: [float(g[y0:y1, x0:x1].mean()) for g in frames[col][:count]]
                          for name, (x0, y0, x1, y1) in spec['boxes'].items()}
tile_series = {col: np.array([tiles(g) for g in frames[col][:count]]) for col in columns}
for col in columns:
    if col == 'g128r':
        continue
    diff = tile_series['g128r'] - tile_series[col]
    per_draw = np.abs(diff).reshape(count, -1).max(axis=1)
    k = int(per_draw.argmax())
    ty, tx = np.unravel_index(np.abs(diff[k]).argmax(), diff[k].shape)
    out['tiles'][col] = {'perDraw': [float(v) for v in per_draw], 'largest': float(diff[k, ty, tx]),
                         'draw': first + k, 'tile': [int(tx) * TILE, int(ty) * TILE]}
bcol = 'bl' if 'bl' in columns else 'b'
out['tilesAgainstB'] = {}
for col in columns:
    if col == bcol:
        continue
    diff = tile_series[col] - tile_series[bcol]
    per_draw = np.abs(diff).reshape(count, -1).max(axis=1)
    k = int(per_draw.argmax())
    ty, tx = np.unravel_index(np.abs(diff[k]).argmax(), diff[k].shape)
    out['tilesAgainstB'][col] = {'perDraw': [float(v) for v in per_draw], 'largest': float(diff[k, ty, tx]),
                                 'draw': first + k, 'tile': [int(tx) * TILE, int(ty) * TILE],
                                 'drawsOver4': int((per_draw > 4).sum()), 'drawsOver8': int((per_draw > 8).sum())}
for col in columns:
    entry = {}
    for name, series in out['curves'][col].items():
        m = np.array(series)
        second = np.abs(m[2:] - 2 * m[1:-1] + m[:-2])
        entry[name] = {'largest': float(second.max()), 'draw': first + 1 + int(second.argmax())}
    t = tile_series[col]
    second = np.abs(t[2:] - 2 * t[1:-1] + t[:-2]).reshape(count - 2, -1).max(axis=1)
    entry['tile'] = {'largest': float(second.max()), 'draw': first + 1 + int(second.argmax())}
    out['flicker'][col] = entry
    for lo, hi in spec.get('windows', []):
        a, b = lo - first, hi - first + 1
        w = t[a:b]
        second = np.abs(w[2:] - 2 * w[1:-1] + w[:-2]).reshape(len(w) - 2, -1).max(axis=1)
        whole = np.array(out['curves'][col]['whole'][a:b])
        second_whole = np.abs(whole[2:] - 2 * whole[1:-1] + whole[:-2])
        entry[f'tile {lo} to {hi}'] = {'largest': float(second.max()), 'draw': lo + 1 + int(second.argmax())}
        entry[f'whole {lo} to {hi}'] = {'largest': float(second_whole.max()), 'draw': lo + 1 + int(second_whole.argmax())}
for col in columns:
    path = F / f"{spec['prefix']}-{col}" / 'region-summary.json'
    if spec['log0'] is None or not path.exists():
        continue
    rows = json.loads(path.read_text())[spec['log0']:spec['log0'] + count]
    out['log'][col] = {'answers': [r['answers'] for r in rows], 'openAt36': [r['openAt36'] for r in rows],
                       'openAt128': [r['openAt128'] for r in rows], 'causeAnchors': [r['causeAnchors'] for r in rows]}
(REC / f'g128r-{film}.json').write_text(json.dumps(out))

print(f"{spec['name']}: {count} draws from {first}")
for col, t in out['tiles'].items():
    print(f"largest 96 px tile difference g128r - {col}: {t['largest']:+.2f} gray at draw {t['draw']}, tile x {t['tile'][0]}"
          f" y {t['tile'][1]}")
for col, t in out['tilesAgainstB'].items():
    print(f"largest 96 px tile difference {col} - {bcol}: {t['largest']:+.2f} gray at draw {t['draw']}, tile x {t['tile'][0]}"
          f" y {t['tile'][1]}; draws over 4 gray {t['drawsOver4']}, over 8 gray {t['drawsOver8']}")
for col, entry in out['flicker'].items():
    print(f'flicker {col}: ' + ', '.join(f"{name} {v['largest']:.2f} at {v['draw']}" for name, v in entry.items()))

if os.environ.get('CROPS'):
    draws = [int(d) for d in os.environ['CROPS'].split(',')]
    columns = os.environ['CROPCOLS'].split(',') if os.environ.get('CROPCOLS') else columns
    x0, y0, x1, y1 = (int(v) for v in os.environ['CROPBOX'].split(','))
    w, h, band = x1 - x0, y1 - y0, 30
    try:
        font = ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf', 18)
    except OSError:
        font = ImageFont.load_default()
    sheet = Image.new('RGB', (len(columns) * (w + 8) - 8, len(draws) * (h + band)), (16, 16, 16))
    draw = ImageDraw.Draw(sheet)
    for r, d in enumerate(draws):
        for i, col in enumerate(columns):
            path = sorted((F / f"{spec['prefix']}-{col}").glob(spec['glob']))[d - first]
            sheet.paste(Image.open(path).convert('RGB').crop((x0, y0, x1, y1)), (i * (w + 8), r * (h + band) + band))
            draw.text((i * (w + 8) + 6, r * (h + band) + 5), f'{LABELS[col]}, draw {d}', fill=(255, 255, 255), font=font)
    target = REC / 'pictures' / f"{spec['name']}-g128r-crops{os.environ.get('CROPTAG', '')}.png"
    sheet.save(target)
    print(f'{target}: {len(draws)} draws, {len(columns)} columns, box {x0},{y0} to {x1},{y1} at 1:1')
