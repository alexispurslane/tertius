# Tool code of t1-yi11. bay.py [column...]: the frozen bay's numbers and crops for the columns (default a b c d), from the
# runs of bayrun.sh in /private/tmp/t1-yi11/runs:
#   - bay-<column> and bay-<column>-dif: 660 draws, kept draws 640 to 659, the full picture and the diffuse signal alone
#     (TERTIUS_PROBE_SIGNAL=2), with the dump of the slow diffuse history's length at draw 639;
#   - bay-<column>-long-dif: the diffuse signal alone, 1,660 draws (DRAWS=1660 EVERY=50), kept every 50th draw from 660.
# The regions are the back-face line's boxes of output pixels (t1-nyjl, guide/daub/boxes.json), so that the numbers
# compare with the ticket's.
#
# Per column and region it prints:
#   - length: the slow diffuse history's mean length at draw 639, in draws (the dump, 1280 x 720 render pixels, read at
#     the region's output pixels), the share of pixels at the column's cap (within 0.5 draws) and the share under 96 draws;
#   - blob: the ticket's measure, on the diffuse signal alone. The residual e = a still minus the late mean of its own
#     take, in display-linear luminance, and its band from 3 to 36 px (Gaussian blur of sigma 3 minus that of sigma 36),
#     as a standard deviation over the region in units of main's full mean there (the full picture's mean luminance over
#     column a's draws 640 to 659), as the ticket's table. Each of the long take's 20
#     kept draws is a still, against the mean of the other 19; the table gives the mean of the 20 values. The kept draws
#     lie 50 draws apart, so a blob of a history of 128 draws is partly shared by neighbours, and the late mean keeps a
#     part of every blob: the numbers are lower bounds, at the same bias for every column of the same cap;
#   - the 8-bit rounding's own blob: the same band of the late mean's rounding to sRGB's 8-bit steps, as the back-face
#     line's band.py measures it. A blob at or near it is the rounding, not noise;
#   - mottle: the standard deviation of the full picture's draw 659 minus its Gaussian blur of sigma 4 px, 8-bit gray.
#
# It writes into pictures/:
#   - bay-<floor|back-wall>-<full|dif>.png and bay-<right-wall|sunlit-patch>-dif.png: 1:1 crops of the region's box from
#     draw 659, the columns in a grid of two under label bands;
#   - bay-<region>-dif-residual.png: the long take's draw 660 minus its own late mean (the other 19 kept draws), in units of
#     main's full mean in the crop, times 1,280 on a gray of 128 (a residual of 0.05 of the mean shows as 64 steps lighter).
# Run it in the dev shell.
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

R = Path('/private/tmp/t1-yi11/runs')
OUT = Path(__file__).resolve().parent.parent / 'pictures'
OUT.mkdir(exist_ok=True)
columns = sys.argv[1:] or ['a', 'b', 'c', 'd']
LABELS = {'a': 'a: main, cap 32', 'b': 'b: cap 128, region evidence today', 'c': 'c: cap 128, repaired test',
          'd': 'd: cap 128, region evidence off', 'e': 'e: cap 64, region evidence off',
          'g': 'g: cap 128, cause gate 128', 'r': 'r: cap 128, signed rank test',
          'gr': 'g+r: cap 128, gate 36, rank test when closed',
          'g128r': 'g128+r: cap 128, gate 128, rank test when closed'}
CAPS = {'a': 32, 'b': 128, 'c': 128, 'd': 128, 'e': 64, 'g': 128, 'r': 128, 'gr': 128, 'g128r': 128}
# SP="c1,c2" reads those columns' stills and dump from their sparse takes bay-<column>-sp and bay-<column>-sp-dif
# (gtakes.sh sparse), whose last kept draw is 659 and whose dump is at draw 639 too.
SPARSE = set(filter(None, __import__('os').environ.get('SP', '').split(',')))
# TAG=<suffix> names the outputs bay-<region>-<kind><suffix>.png and bay-numbers<suffix>.json (the probe's columns).
TAG = __import__('os').environ.get('TAG', '')
BOXES = {'sunlit patch': (100, 40, 420, 360), 'back wall': (560, 420, 980, 560), 'floor': (600, 650, 1200, 950),
         'right wall': (1300, 100, 1800, 500)}
CROPS = {'floor': ('full', 'dif'), 'back wall': ('full', 'dif'), 'right wall': ('dif',), 'sunlit patch': ('dif',)}
W = np.array([0.2126, 0.7152, 0.0722])


def run_of(run):
    """bay-<column>[-dif] as its sparse take bay-<column>-sp[-dif] where SP names the column."""
    col = run[4:].removesuffix('-dif')
    return run.replace(f'bay-{col}', f'bay-{col}-sp') if col in SPARSE else run


def frames(run):
    return sorted((R / run_of(run)).glob('f-*.png'))


def rgb(path):
    a = np.asarray(Image.open(path).convert('RGB'), np.float64) / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)


def lengths(run):
    path = next((R / run_of(run)).glob('diffuse-length-*.f32'))
    w, h = map(int, path.stem.split('-')[-1].split('x'))
    small = np.fromfile(path, np.float32).reshape(h, w)
    ys, xs = np.mgrid[0:1080, 0:1920]
    return small[ys * h // 1080, xs * w // 1920]


def band(e):
    return ndimage.gaussian_filter(e, 3) - ndimage.gaussian_filter(e, 36)


def rounding_band(mean_rgb):
    """The blob band of the 8-bit rounding alone: a mean put through sRGB's 8-bit steps, minus itself."""
    enc = np.where(mean_rgb <= 0.0031308, mean_rgb * 12.92, 1.055 * np.clip(mean_rgb, 0, 1) ** (1 / 2.4) - 0.055)
    q = np.round(np.clip(enc, 0, 1) * 255) / 255
    q = np.where(q <= 0.04045, q / 12.92, ((q + 0.055) / 1.055) ** 2.4)
    return band((q - mean_rgb) @ W)


main_full = np.mean([rgb(p) @ W for p in frames('bay-a')], axis=0)
rows, residual = [], {}
print('| column | region | mean length | at the cap | under 96 | blob, diffuse alone | 8-bit rounding alone | mottle |')
print('|---|---|---:|---:|---:|---:|---:|---:|')
for col in columns:
    length = lengths(f'bay-{col}')
    gray = np.asarray(Image.open(frames(f'bay-{col}')[-1]).convert('L'), np.float64)
    mottle = gray - ndimage.gaussian_filter(gray, 4)
    long_rgb = [rgb(p) for p in frames(f'bay-{col}-long-dif')]
    long = [v @ W for v in long_rgb]
    total, total_rgb = np.sum(long, axis=0), np.sum(long_rgb, axis=0)
    late_rgb = total_rgb / len(long)
    bands = [band(v - (total - v) / (len(long) - 1)) for v in long]
    residual[col] = (long[0], (total - long[0]) / (len(long) - 1))
    rounding = rounding_band(late_rgb)
    for name, (x0, y0, x1, y1) in BOXES.items():
        cut = np.s_[y0:y1, x0:x1]
        mean = main_full[cut].mean()
        row = {'column': col, 'region': name, 'length': float(length[cut].mean()),
               'atCap': float((length[cut] > CAPS[col] - 0.5).mean()), 'under96': float((length[cut] < 96).mean()),
               'blobDiffuse': float(np.mean([b[cut].std() for b in bands]) / mean),
               'blobDiffusePerStill': [float(b[cut].std() / mean) for b in bands],
               'rounding': float(rounding[cut].std() / mean), 'mottle': float(mottle[cut].std())}
        rows.append(row)
        print(f"| {col} | {name} | {row['length']:.1f} | {100 * row['atCap']:.1f} % | {100 * row['under96']:.1f} % | "
              f"{row['blobDiffuse']:.4f} | {row['rounding']:.4f} | {row['mottle']:.3f} |")
(OUT.parent / f'bay-numbers{TAG}.json').write_text(json.dumps(rows, indent=1))

try:
    font = ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf', 20)
except OSError:
    font = ImageFont.load_default()


def grid(tiles, w, h):
    """The columns' tiles in a grid of two columns, each under its label band."""
    rows_n = (len(tiles) + 1) // 2
    sheet = Image.new('RGB', (2 * w + 8, rows_n * (h + 38) - 8), (16, 16, 16))
    draw = ImageDraw.Draw(sheet)
    for i, (label, tile) in enumerate(tiles):
        x, y = (i % 2) * (w + 8), (i // 2) * (h + 38)
        sheet.paste(tile, (x, y + 30))
        draw.text((x + 6, y + 4), label, fill=(255, 255, 255), font=font)
    return sheet


for region, kinds in CROPS.items():
    x0, y0, x1, y1 = BOXES[region]
    w, h = x1 - x0, y1 - y0
    slug = region.replace(' ', '-')
    for kind in kinds:
        suffix = '-dif' if kind == 'dif' else ''
        tiles = [(LABELS[col], Image.open(frames(f'bay-{col}{suffix}')[-1]).convert('RGB').crop((x0, y0, x1, y1)))
                 for col in columns]
        grid(tiles, w, h).save(OUT / f'bay-{slug}-{kind}{TAG}.png')
    tiles = []
    for col in columns:
        still, late = residual[col]
        e = (still - late)[y0:y1, x0:x1] / main_full[y0:y1, x0:x1].mean()
        tiles.append((LABELS[col] + ', residual x10',
                      Image.fromarray(np.clip(128 + 1280 * e, 0, 255).astype(np.uint8)).convert('RGB')))
    grid(tiles, w, h).save(OUT / f'bay-{slug}-dif-residual{TAG}.png')
