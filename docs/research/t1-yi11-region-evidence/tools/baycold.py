# Tool code of t1-yi11. baycold.py: the frozen bay from the start (draw 0) for columns b, gr (g36+r) and g128r (g128+r),
# from the sparse takes of gtakes.sh in /private/tmp/t1-yi11/runs: bay-<column>-sp (the full picture) and
# bay-<column>-sp-dif (the diffuse signal alone), each 660 draws with a few kept draws (KEEP_LIST). Column gr's draws 300
# to 659 come from its full takes bay-gr and bay-gr-dif (every draw kept), which its sparse take matches byte for byte.
#
# The question: under g128+r the cause gate is open from the start (draw 0 resets every anchor's cause age) to draw 127,
# so today's test answers in draws 36 to 127, as in column b. g36+r's gate closes at draw 36. Do the cuts of draws 36 to
# 127 leave patches that a viewer sees, then or in draws 128 to 300, and does g128+r reach g36+r's settled picture?
#
# Units: display-linear luminance (8-bit sRGB decoded, Rec. 709 weights). The reference is column d's late mean: the
# mean of the 20 captures of bay-d-long-dif or bay-d-long (draws 660 to 1,610, cap 128, region evidence off). For each
# column, kind (full picture, diffuse alone), kept draw and region it gives:
#   - patch: the band from 3 to 36 px (Gaussian blur of sigma 3 minus that of sigma 36) of the still minus the reference,
#     as a standard deviation over the region, in units of main's full mean there (as bay.py's blob). The reference's own
#     leftover blob is the same for every column, so the columns compare at one draw;
#   - level: the region's mean over the reference's mean.
# It writes baycold.json beside the record and, into pictures/, 1:1 crops of the floor and the back wall at the draws of
# PICTURE_DRAWS for b, g36+r and g128+r: bay-cold-<floor|back-wall>-<dif|full>.png, and
# bay-cold-<floor|back-wall>-dif-residual.png (the still minus the reference, times 10 on a gray of 128).
#
# WALL=1 adds the check of g128+r's answer runs in draws 301 to 659 (RUNS="draw:x:y,..."): for each run, 288 x 288 crops
# around the run's anchor (its region spans output x 96 x - 96 to 96 x + 108, the same in y) at draws run - 1, + 2, + 6,
# + 12 and + 24, of the diffuse signal, of the diffuse signal minus column d's still of the same draw (times 10 on 128)
# and of the full picture, into pictures/bay-wall-runs-g128r-dif.png (the first two) and bay-wall-runs-g128r-full.png, and per crop the patch measure of g128+r's diffuse
# still minus d's still of the same draw. Run it in the dev shell.
import json
import os
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

R = Path('/private/tmp/t1-yi11/runs')
REC = Path(__file__).resolve().parent.parent
OUT = REC / 'pictures'
W = np.array([0.2126, 0.7152, 0.0722])
BOXES = {'sunlit patch': (100, 40, 420, 360), 'back wall': (560, 420, 980, 560), 'floor': (600, 650, 1200, 950),
         'right wall': (1300, 100, 1800, 500)}
COLUMNS = ['b', 'gr', 'g128r']
LABELS = {'b': 'b: today\'s test', 'gr': 'g36+r', 'g128r': 'g128+r'}
PICTURE_DRAWS = [int(d) for d in os.environ.get('PICTURE_DRAWS', '40,64,96,127,152,200,300,659').split(',')]
try:
    font = ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf', 16)
except OSError:
    font = ImageFont.load_default()


def linear(path):
    a = np.asarray(Image.open(path).convert('RGB'), np.float64) / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ W


def band(e):
    return ndimage.gaussian_filter(e, 3) - ndimage.gaussian_filter(e, 36)


def still(col, kind, draw):
    """The kept still of a column at a draw: its sparse take, or for gr from draw 300 its full take."""
    suffix = '-dif' if kind == 'dif' else ''
    path = R / f'bay-{col}-sp{suffix}' / f'f-{draw:06d}.png'
    if not path.exists() and col == 'gr':
        path = R / f'bay-gr{suffix}' / f'f-{draw:06d}.png'
    return path if path.exists() else None


main_full = np.mean([linear(p) for p in sorted((R / 'bay-a').glob('f-*.png'))], axis=0)
reference = {kind: np.mean([linear(p) for p in sorted((R / f'bay-d-long{suffix}').glob('f-*.png'))], axis=0)
             for kind, suffix in (('dif', '-dif'), ('full', ''))}
draws = sorted(int(p.stem[2:]) for p in (R / 'bay-b-sp-dif').glob('f-*.png'))
out = {'draws': draws, 'columns': COLUMNS, 'regions': list(BOXES), 'patch': {}, 'level': {}}
cache = {}
for kind in ('dif', 'full'):
    for col in COLUMNS:
        patch, level = {}, {}
        for d in draws:
            path = still(col, kind, d)
            if path is None:
                continue
            lum = linear(path)
            cache[(col, kind, d)] = lum
            e = band(lum - reference[kind])
            patch[d] = {name: float(e[y0:y1, x0:x1].std() / main_full[y0:y1, x0:x1].mean())
                        for name, (x0, y0, x1, y1) in BOXES.items()}
            level[d] = {name: float(lum[y0:y1, x0:x1].mean() / reference[kind][y0:y1, x0:x1].mean())
                        for name, (x0, y0, x1, y1) in BOXES.items()}
        out['patch'].setdefault(kind, {})[col] = patch
        out['level'].setdefault(kind, {})[col] = level
for kind in ('dif', 'full'):
    print(f'\n{kind}: patch (band 3 to 36 px of the still minus d\'s late mean, in main\'s full mean), floor / back wall')
    print('| draw | ' + ' | '.join(LABELS[c] for c in COLUMNS) + ' |')
    for d in draws:
        cells = [f"{out['patch'][kind][c][d]['floor']:.4f} / {out['patch'][kind][c][d]['back wall']:.4f}"
                 if d in out['patch'][kind][c] else '' for c in COLUMNS]
        print(f'| {d} | ' + ' | '.join(cells) + ' |')


def sheet(tiles, w, h, ncols, path):
    rows = (len(tiles) + ncols - 1) // ncols
    image = Image.new('RGB', (ncols * (w + 6) - 6, rows * (h + 24)), (16, 16, 16))
    draw = ImageDraw.Draw(image)
    for i, (label, tile) in enumerate(tiles):
        x, y = (i % ncols) * (w + 6), (i // ncols) * (h + 24)
        image.paste(tile, (x, y + 22))
        draw.text((x + 4, y + 3), label, fill=(255, 255, 255), font=font)
    image.save(path)


for region in ('floor', 'back wall'):
    x0, y0, x1, y1 = BOXES[region]
    slug = region.replace(' ', '-')
    for kind in ('dif', 'full'):
        tiles = []
        for d in PICTURE_DRAWS:
            for col in COLUMNS:
                path = still(col, kind, d)
                tiles.append((f'{LABELS[col]}, draw {d}', Image.open(path).convert('RGB').crop((x0, y0, x1, y1))))
        sheet(tiles, x1 - x0, y1 - y0, len(COLUMNS), OUT / f'bay-cold-{slug}-{kind}.png')
    tiles = []
    for d in PICTURE_DRAWS:
        for col in COLUMNS:
            e = (cache[(col, 'dif', d)] - reference['dif'])[y0:y1, x0:x1] / main_full[y0:y1, x0:x1].mean()
            tiles.append((f'{LABELS[col]}, draw {d}, minus d late, x10',
                          Image.fromarray(np.clip(128 + 1280 * e, 0, 255).astype(np.uint8)).convert('RGB')))
    sheet(tiles, x1 - x0, y1 - y0, len(COLUMNS), OUT / f'bay-cold-{slug}-dif-residual.png')

if os.environ.get('WALL'):
    runs = [tuple(int(v) for v in r.split(':')) for r in os.environ['RUNS'].split(',')]
    offsets = [-1, 2, 6, 12, 24]
    size = 288
    tiles, full_tiles, wall = [], [], []
    for draw, ax, ay in runs:
        cx = min(max(96 * ax + 6 - size // 2, 0), 1920 - size)
        cy = min(max(96 * ay + 6 - size // 2, 0), 1080 - size)
        box = (cx, cy, cx + size, cy + size)
        row = {'draw': draw, 'anchor': [ax, ay], 'box': list(box), 'patch': {}}
        for kind, label in (('dif', 'diffuse'), ('res', 'diffuse minus d, x10'), ('full', 'full')):
            for k in offsets:
                d = draw + k
                if kind == 'res':
                    mine = linear(R / 'bay-g128r-sp-dif' / f'f-{d:06d}.png')
                    theirs = linear(R / 'bay-d-dif' / f'f-{d:06d}.png')
                    e = mine - theirs
                    cut = np.s_[box[1]:box[3], box[0]:box[2]]
                    row['patch'][d] = float(band(e)[cut].std() / main_full[cut].mean())
                    tile = Image.fromarray(np.clip(128 + 1280 * e[cut] / main_full[cut].mean(), 0, 255)
                                           .astype(np.uint8)).convert('RGB')
                else:
                    folder = 'bay-g128r-sp-dif' if kind == 'dif' else 'bay-g128r-sp'
                    tile = Image.open(R / folder / f'f-{d:06d}.png').convert('RGB').crop(box)
                (full_tiles if kind == 'full' else tiles).append((f'run {draw} ({ax},{ay}) {label} {d}', tile))
        wall.append(row)
        print(f"run from draw {draw} at anchor ({ax}, {ay}), box {box}: patch of g128+r minus d by draw "
              + ', '.join(f'{d} {v:.4f}' for d, v in row['patch'].items()))
    sheet(tiles, size, size, len(offsets), OUT / 'bay-wall-runs-g128r-dif.png')
    sheet(full_tiles, size, size, len(offsets), OUT / 'bay-wall-runs-g128r-full.png')
    out['wallRuns'] = wall
(REC / 'baycold.json').write_text(json.dumps(out, indent=1))
