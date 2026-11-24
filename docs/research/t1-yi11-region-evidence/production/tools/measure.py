# Tool code of t1-yi11's production check. measure.py <bay|walk|turned|switch|cut>: the numbers and pictures of the takes
# of takes.py, production (prod) against the baseline 3cfcfd2b3 (base), into /private/tmp/t1-yi11-prod/out/. Units:
#   - gray: mean 8-bit gray (0 to 255) of the kept PNG (sRGB, 1920 x 1080);
#   - linear: display-linear luminance (8-bit sRGB decoded, Rec. 709 weights);
#   - blob: the band from 3 to 36 px (Gaussian blur of sigma 3 minus that of sigma 36) of a still minus its take's own
#     late mean (draws 660 to 1,610, every 50th), as a standard deviation over a region, in units of the late mean's
#     mean there (ticket t1-yi11, "Blob");
#   - tile difference: the largest |difference| of a 96 x 96 tile's mean gray between prod and base at one draw (96
#     output pixels are the anchors' step); grid step: on D = prod - base, the mean over the tile boundaries of |mean of D
#     over the 4 px before the line - mean over the 4 px after it|, and "mid" the same 48 px from the boundaries;
#   - answers: from region-ring.log, the anchor-draws whose signed answer b is not 0, and the anchors with an open gate
#     (0 <= a < 128).
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

T = Path('/private/tmp/t1-yi11-prod/takes')
O = Path('/private/tmp/t1-yi11-prod/out')
O.mkdir(exist_ok=True)
W = np.array([0.2126, 0.7152, 0.0722])
TILE = 96
BOXES = {'sunlit patch': (100, 40, 420, 360), 'back wall': (560, 420, 980, 560), 'floor': (600, 650, 1200, 950),
         'right wall': (1300, 100, 1800, 500)}
try:
    font = ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf', 18)
except OSError:
    font = ImageFont.load_default()


def rgb(p):
    return np.asarray(Image.open(p).convert('RGB'), np.float64)


def linear(p):
    a = rgb(p) / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ W


def gray(p):
    return np.asarray(Image.open(p).convert('L'), np.float64)


def band(e):
    return ndimage.gaussian_filter(e, 3) - ndimage.gaussian_filter(e, 36)


def read_log(folder):
    raw, out, at = (folder / 'region-ring.log').read_bytes(), {}, 0
    while at < len(raw):
        draw, index, w, h = np.frombuffer(raw, np.int32, 4, at)
        at += 16
        out[int(draw)] = (int(index), np.frombuffer(raw, np.float32, w * h * 4, at).reshape(h, w, 4))
        at += w * h * 16
    return out


def answers(folder, first=0, last=10**9):
    log = read_log(folder)
    draws = [d for d in sorted(log) if first <= d <= last]
    b = np.array([log[d][1][..., 2] for d in draws])
    a = np.array([log[d][1][..., 3] for d in draws])
    g = np.array([log[d][1][..., 1] for d in draws])
    tested = g >= 36
    return {'draws': len(draws), 'anchors': int(b.shape[1] * b.shape[2]), 'testedAnchorDraws': int(tested.sum()),
            'answers': int((b != 0).sum()), 'answersUp': int((b > 0).sum()), 'answersDown': int((b < 0).sum()),
            'openGateAnchorDraws': int(((a >= 0) & (a < 128)).sum()),
            'unestablishedAnchorDraws': int((a < 0).sum())}


def label(img, text):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, img.width, 26), fill=(0, 0, 0))
    d.text((6, 3), text, fill=(255, 255, 255), font=font)
    return img


def sheet(rows, path):
    """rows: list of lists of PIL images of one size; writes them as a grid."""
    w, h = rows[0][0].size
    out = Image.new('RGB', (w * len(rows[0]) + 4 * (len(rows[0]) - 1), h * len(rows) + 4 * (len(rows) - 1)), (40, 40, 40))
    for r, row in enumerate(rows):
        for c, im in enumerate(row):
            out.paste(im, (c * (w + 4), r * (h + 4)))
    out.save(path)


def crop(p, box):
    return Image.open(p).convert('RGB').crop(box)


def tiles(d):
    h, w = d.shape
    return d[:h // TILE * TILE, :w // TILE * TILE].reshape(h // TILE, TILE, w // TILE, TILE).mean(axis=(1, 3))


def grid_step(d, offset=0):
    steps = []
    for x in range(TILE + offset, d.shape[1] - 4, TILE):
        steps.append(abs(d[:, x - 4:x].mean() - d[:, x:x + 4].mean()))
    for y in range(TILE + offset, d.shape[0] - 4, TILE):
        steps.append(abs(d[y - 4:y].mean() - d[y:y + 4].mean()))
    return float(np.mean(steps))


part = sys.argv[1]
result = {}
if part in ('bay', 'bayauto'):
    for kind in (('full', 'dif') if part == 'bay' else ('auto',)):
        for build in ('prod', 'base'):
            folder = T / ('-'.join(['bay', build] + ([kind] if kind != 'full' else [])))
            f = lambda n: folder / f'f-{n:06d}.png'
            ref = np.mean([linear(f(n)) for n in range(660, 1611, 50)], axis=0)
            rec = {}
            for n in [36, 40, 48, 64, 96, 127, 152, 200, 300, *range(640, 660, 2), 659, 1659]:
                e = linear(f(n)) - ref
                bd = band(e)
                rec[n] = {name: float(bd[y0:y1, x0:x1].std() / ref[y0:y1, x0:x1].mean())
                          for name, (x0, y0, x1, y1) in BOXES.items()}
            late = {name: float(np.mean([rec[n][name] for n in [*range(640, 660, 2), 659]])) for name in BOXES}
            level = {name: float(linear(f(659))[y0:y1, x0:x1].mean()) for name, (x0, y0, x1, y1) in BOXES.items()}
            result[f'{build} {kind}'] = {'blob by draw': rec, 'blob draws 640 to 659': late, 'linear mean at 659': level,
                                         'ring': answers(folder, 36), 'ring cold 0 to 300': answers(folder, 0, 300),
                                         'ring frozen 301 to 1659': answers(folder, 301)}
        # pictures: floor and back wall crops at 1:1, prod above base, draws across the start and settled
        for region, box in (('floor', (600, 650, 1100, 950)), ('back-wall', (520, 380, 1020, 600))):
            draws = [48, 96, 152, 300, 659]
            rows = []
            for build in ('prod', 'base'):
                folder = T / ('-'.join(['bay', build] + ([kind] if kind != 'full' else [])))
                rows.append([label(crop(folder / f'f-{n:06d}.png', box), f'{build} draw {n}') for n in draws])
            sheet(rows, O / f'bay-{region}-{kind}.png')
elif part == 'walk':
    boxes = {'whole': (0, 0, 1920, 1080), 'floor': (0, 600, 1920, 1080), 'walls and ceiling': (0, 0, 1920, 600)}
    names = [f'g-{k:06d}' for k in range(50, 120, 5)] + [f'h-{k:06d}' for k in range(0, 160, 4)]
    for build in ('prod', 'base'):
        for run in ('switch', 'off'):
            folder = T / f'walk-{build}-{run}'
            result[f'{build} {run}'] = {n: {b: float(gray(folder / f'{n}.png')[y0:y1, x0:x1].mean())
                                            for b, (x0, y0, x1, y1) in boxes.items()} for n in names}
            result[f'{build} {run} ring'] = answers(folder)
    for build in ('prod', 'base'):
        result[f'{build} switch minus off'] = {n: {b: result[f'{build} switch'][n][b] - result[f'{build} off'][n][b]
                                                   for b in boxes} for n in names}
    rows = []
    for build in ('prod', 'base'):
        rows.append([label(Image.open(T / f'walk-{build}-switch' / f'{n}.png').convert('RGB').resize((640, 360)),
                           f'{build} {n}') for n in ('g-000065', 'g-000115', 'h-000000', 'h-000040', 'h-000080', 'h-000156')])
    sheet(rows, O / 'walk-switch.png')
    rows = [[label(crop(T / f'walk-{b}-switch' / f'{n}.png', (660, 600, 1260, 940)), f'{b} {n}') for b in ('prod', 'base')]
            for n in ('h-000000', 'h-000040', 'h-000120')]
    sheet(rows, O / 'walk-floor.png')
elif part == 'turned':
    for build in ('prod', 'base'):
        result[f'{build} ring'] = answers(T / f'turned-{build}')
    per = {}
    for k in range(0, 180, 3):
        n = f'm{k:04d}-000000.png'
        d = gray(T / 'turned-prod' / n) - gray(T / 'turned-base' / n)
        per[k] = {'tile difference': float(np.abs(tiles(d)).max()), 'grid step': grid_step(d),
                  'mid': grid_step(d, TILE // 2), 'prod gray': float(gray(T / 'turned-prod' / n).mean()),
                  'base gray': float(gray(T / 'turned-base' / n).mean())}
    result['per frame'] = per
    result['largest tile difference'] = max(v['tile difference'] for v in per.values())
    result['frames above 8'] = [k for k, v in per.items() if v['tile difference'] > 8]
    rows = []
    for k in (60, 81, 99, 150):
        n = f'm{k:04d}-000000.png'
        rows.append([label(Image.open(T / f'turned-{b}' / n).convert('RGB').resize((960, 540)), f'{b} film draw {k}')
                     for b in ('prod', 'base')])
    sheet(rows, O / 'turned-rect-bay.png')
    rows = [[label(crop(T / f'turned-{b}' / f'm{k:04d}-000000.png', (480, 300, 1440, 840)), f'{b} draw {k}') for b in
             ('prod', 'base')] for k in (60, 81, 99)]
    sheet(rows, O / 'turned-rect-bay-1to1.png')
elif part == 'switch':
    for build in ('prod', 'base'):
        s = json.loads((T / f'switch-{build}' / 'settle.json').read_text())
        result[build] = {k: s[k] for k in ('offDraws', 'onDraws', 'offDrawsWhole', 'onDrawsWhole', 'failed')}
        result[f'{build} ring'] = answers(T / f'switch-{build}' / 'switch')
    frames = (165, 200, 260, 365, 400)
    rows = [[label(Image.open(T / f'switchfilm-{b}' / f'{survey_name}.png').convert('RGB').resize((640, 360)), f'{b} film {k}')
             for k, survey_name in ((k, f'f-{k:06d}') for k in frames)] for b in ('prod', 'base')]
    sheet(rows, O / 'cabin-switch.png')
elif part == 'cut':
    keep = [*range(16), *range(16, 160, 8)]
    for build in ('prod', 'base'):
        per = {}
        for k in keep:
            n = f'post-{k:06d}.png'
            a, b = gray(T / f'cut-{build}-a' / n), gray(T / f'cut-{build}-b' / n)
            per[k] = {'mean |a - b|': float(np.abs(a - b).mean()), 'largest tile |a - b|': float(np.abs(tiles(a - b)).max()),
                      'identical': bool((T / f'cut-{build}-a' / n).read_bytes() == (T / f'cut-{build}-b' / n).read_bytes())}
        result[build] = per
        result[f'{build} ring a'] = answers(T / f'cut-{build}-a')
    result['prod against base, cut a'] = {k: float(np.abs(tiles(gray(T / 'cut-prod-a' / f'post-{k:06d}.png')
                                                              - gray(T / 'cut-base-a' / f'post-{k:06d}.png'))).max())
                                          for k in keep}
    rows = [[label(Image.open(T / f'cut-{b}-{c}' / f'post-{k:06d}.png').convert('RGB').resize((480, 270)),
                   f'{b} {c} after cut {k}') for k in (0, 1, 2, 4, 8, 32, 152)] for b in ('prod', 'base') for c in ('a', 'b')]
    sheet(rows, O / 'camera-cut.png')
    rows = [[label(Image.open(T / f'cut-{b}-a' / 'pre-000398.png').convert('RGB').resize((480, 270)), f'{b} a before cut')]
            + [label(Image.open(T / f'cut-{b}-b' / 'pre-000398.png').convert('RGB').resize((480, 270)), f'{b} b before cut')]
            for b in ('prod',)]
    sheet(rows, O / 'camera-cut-before.png')
(O / f'{part}.json').write_text(json.dumps(result, indent=1))
print(json.dumps(result, indent=1)[:3000])
