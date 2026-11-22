# Tool code of t0-9fo0, part B. gridsheet.py <whole.png> <crops.png> <M run> <X0 run> <X run>: the sheets of the anchors'
# grid from gridfilm.py's frames (images/grid/cabin-<frame>.png) of the switch "cabin", at film frames 180 and 220 (20
# and 60 draws after the lamps go off) and 380 and 420 (after they come on).
#   <whole.png>: one row per frame, M, X0 and X, the whole frame at a third of its size, and the difference map of X
#   against X0 over the whole frame, with the anchors' grid.
#   <crops.png>: one row per frame, M, X0 and X at full resolution, and the signed difference of X against X0, in the
#   crop where X differs most from X0 over the four frames, with the grid of the region's anchors drawn over the map.
# The frames are the probe's captures: log2 scene luminance in 24 bits, which survey.probe_luminance decodes. So the
# tiles show scene luminance in gray, through one fixed curve for every tile: the Hable curve of 2 y / y_ref over that of
# 11.2, sRGB-encoded, where y_ref is the median visible luminance of M's frame 159 (the draw before the lamps go off).
# The difference is (X - X0) / X0 of the scene luminance blurred by 2 output pixels, over the visible pixels away from the
# lamps (survey.visible and survey.near_emitters of X0's frame 420), and 0 elsewhere. A map shows that difference less
# its mean over the map's own measured pixels (the frame's for a whole map, the crop's for a crop), from -3 % (blue)
# through 0 (gray) to +3 % (red), so that a step along the grid shows beside the broad change, which the title gives.
# The crop is the 480 x 320 window, on a grid of 32 output pixels, with the largest mean
# magnitude of that difference over the four frames. The anchors' grid: a block's region evidence can change only where
# the set of anchors whose regions hold it changes (region_drift.glsl, regionAnchorMovedBefore). A block on an anchor's
# column (block 8 i) lies in 3 regions along x, and its neighbors in 2, so the set changes at both edges of that block:
# at render pixels 64 i and 64 i + 8, which are output pixels 96 i and 96 i + 12 at 1920 x 1080 output from 1280 x 720
# render. The same holds along y. The lines are drawn at x = 96 i and 96 i + 12 and at y = 96 j and 96 j + 12, thin and
# green, on the difference map only. It prints the crop and, per frame, the mean signed difference of X against X0 in the crop and
# over the measured pixels. Run it through m.sh.
import importlib.util
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, uniform_filter

spec = importlib.util.spec_from_file_location('survey', Path(__file__).resolve().parents[4] / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
FRAMES = (180, 220, 380, 420)
LABELS = {180: '20 draws after the lamps go off', 220: '60 after off', 380: '20 after the lamps come on', 420: '60 after on'}
CW, CH, SCALE, STEP, STRIP = 480, 320, 0.03, 96, 12
whole_out, crops_out, runs = sys.argv[1], sys.argv[2], dict(zip(('M', 'X0', 'X'), sys.argv[3:6]))


def png(run, k):
    return Path(f's/{run}/images/grid/cabin-{k:03d}.png')


def hable(x):
    a, b, c, d, e, f = 0.15, 0.50, 0.10, 0.20, 0.02, 0.30
    return (x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f) - e / f


lum = {(s, k): survey.probe_luminance(png(r, k)) for s, r in runs.items() for k in FRAMES + (159,)}
lamps = survey.near_emitters(lum[('X0', FRAMES[-1])])
measured = survey.visible(lum[('X0', FRAMES[-1])]) & ~lamps
y_ref = float(np.median(lum[('M', 159)][measured]))


def shown(y):
    v = np.clip(hable(2.0 * y / y_ref) / hable(11.2), 0, 1)
    v = np.where(v <= 0.0031308, 12.92 * v, 1.055 * v ** (1 / 2.4) - 0.055)
    return Image.fromarray((255 * v).astype(np.uint8)).convert('RGB')


def diff(k):
    x, x0 = (gaussian_filter(lum[(s, k)], 2) for s in ('X', 'X0'))
    return np.where(measured, (x - x0) / (x0 + survey.RELATIVE_FLOOR), 0.0)


diffs = {k: diff(k) for k in FRAMES}
score = sum(np.abs(d) for d in diffs.values()) / len(FRAMES)
mean = uniform_filter(score, size=(CH, CW))
best, at = -1, (0, 0)
for y in range(CH // 2, 1080 - CH // 2 + 1, 32):
    for x in range(CW // 2, 1920 - CW // 2 + 1, 32):
        if mean[y, x] > best:
            best, at = mean[y, x], (x - CW // 2, y - CH // 2)
x0, y0 = at
box = (x0, y0, x0 + CW, y0 + CH)
print(f'y_ref {y_ref:.4f}; crop x {x0} to {x0 + CW}, y {y0} to {y0 + CH}, mean |X - X0| / X0 there {100 * best:.2f} %')


def label(tile, text):
    out = Image.new('RGB', (tile.width, tile.height + 22), (0, 0, 0))
    out.paste(tile, (0, 22))
    ImageDraw.Draw(out).text((4, 5), text, fill=(255, 255, 255))
    return out


def row(tiles):
    sheet = Image.new('RGB', (sum(t.width for t in tiles) + 6 * (len(tiles) - 1), max(t.height for t in tiles)), (60, 60, 60))
    x = 0
    for t in tiles:
        sheet.paste(t, (x, 0))
        x += t.width + 6
    return sheet


def column(rows):
    sheet = Image.new('RGB', (max(r.width for r in rows), sum(r.height for r in rows) + 6 * (len(rows) - 1)), (60, 60, 60))
    y = 0
    for r in rows:
        sheet.paste(r, (0, y))
        y += r.height + 6
    return sheet


def diffmap(d, region):
    t = np.where(measured, np.clip((d - d[region][measured[region]].mean()) / SCALE, -1, 1), 0.0)[..., None]
    gray, red, blue = np.array([0.55] * 3), np.array([0.85, 0.15, 0.1]), np.array([0.1, 0.3, 0.9])
    rgb = np.where(t > 0, gray + t * (red - gray), gray - t * (blue - gray))
    return Image.fromarray((255 * rgb).astype(np.uint8))


whole_rows, crop_rows = [], []
for k in FRAMES:
    images = {s: shown(lum[(s, k)]) for s in runs}
    whole = (slice(0, 1080), slice(0, 1920))
    region = (slice(y0, y0 + CH), slice(x0, x0 + CW))
    maps = []
    for part in (whole, region):
        m = diffmap(diffs[k], part)
        d = ImageDraw.Draw(m)
        for gx in [g + e for g in range(0, 1920, STEP) for e in (0, STRIP)]:
            d.line([(gx, 0), (gx, 1079)], fill=(0, 200, 0), width=1)
        for gy in [g + e for g in range(0, 1080, STEP) for e in (0, STRIP)]:
            d.line([(0, gy), (1919, gy)], fill=(0, 200, 0), width=1)
        maps.append(m)
    broad = 100 * diffs[k][measured].mean()
    whole_rows.append(row([label(im.resize((640, 360), Image.LANCZOS), f'{s}, frame {k}: {LABELS[k]}')
                           for s, im in images.items()]
                          + [label(maps[0].resize((640, 360), Image.LANCZOS),
                                   f'(X - X0) / X0 less its mean {broad:+.1f} %, +-3 %, grid green')]))
    local = 100 * diffs[k][region][measured[region]].mean()
    crop_rows.append(row([label(im.crop(box), f'{s}, frame {k}, 1:1') for s, im in images.items()]
                         + [label(maps[1].crop(box), f'(X - X0) / X0 less its mean {local:+.1f} %, +-3 %, grid green')]))
    print(f'frame {k}: X against X0, mean signed difference in the crop {100 * diffs[k][region][measured[region]].mean():+.2f} %,'
          f' over the measured pixels {100 * diffs[k][measured].mean():+.2f} %')
column(whole_rows).save(whole_out)
column(crop_rows).save(crops_out)
print(whole_out, crops_out)
