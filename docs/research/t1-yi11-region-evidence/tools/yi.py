# Tool code of t1-yi11. yi.py <bay|turned|walk|look>: the numbers and pictures of the comparison of the two closed-gate tests
# (2026-11-24): w36 (build yi, Welch's t of two windows of 36 draws, |t| >= 4.1) and hyst (build yj, column hx: the
# replay's signed hysteresis of Welch's t of two windows of 18 draws, 5.0 and 2.6), each with the closed reset and a gate
# of 128 draws after a real cause. Column "pair" is build yi's first reading of hyst (a same-direction neighbor pair, its
# folders named *-hyst), which is not the specified rule; it stays for the record. All against against the controls b (today's test) and g128r (the rejected rule: gate 128, the rank
# test while closed, the old reset that opens every gate). Build yi draws b's and g128r's bytes (README), so the old takes
# of yg and yh serve as controls where they exist.
#
# Units: mean 8-bit gray of the kept PNGs (1920 x 1080 output) unless named otherwise. TILE = 96 output pixels is the
# anchors' spacing (8 blocks of 8 render pixels at the render scale 2/3), and every region of the test starts and ends on
# a multiple of it. The measures:
#   - tile difference: the largest |difference| of a 96 x 96 tile's mean gray between a column and column b at one draw,
#     over the 11 x 20 full tiles (as g128r.py);
#   - grid step: on the difference image D = column - b (gray), the mean over every vertical and horizontal tile boundary
#     of |mean of D over the 4 px before the line - mean of D over the 4 px after it|; "mid" is the same at the lines 48 px
#     from the boundaries. A patchwork whose edges follow the anchors' regions gives a grid step well above mid;
#   - flicker: the largest |m(k + 1) - 2 m(k) + m(k - 1)| of a box's or a tile's mean gray over consecutive kept draws;
#   - patch (bay): the band from 3 to 36 px of a still's display-linear luminance minus column d's late mean (bay-d-long,
#     draws 660 to 1,610), as a standard deviation over the region in units of main's full mean there (baycold.py's
#     measure); level: the region's mean over the reference's mean;
#   - answers: from the ring logs (region-ring.log: b the answer, g the valid draws, a the cause age plus 8192 x the count
#     of taps with a cause), anchors that answer yes per draw, anchors with an open gate (cause age under 128).
# It writes yi-<bay|turned|walk>.json beside the record, and the pictures named in each part. Run it in the dev shell.
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

F = Path('/private/tmp/t1-yi11/films')
R = Path('/private/tmp/t1-yi11/runs')
REC = Path(__file__).resolve().parent.parent
PIC = REC / 'pictures'
TILE = 96
W = np.array([0.2126, 0.7152, 0.0722])
LABELS = {'b': 'b: today\'s test', 'bl': 'b: today\'s test', 'byi': 'b: today\'s test', 'g128r': 'g128+r (rejected)',
          'g128ryi': 'g128+r (rejected)', 'w36': 'w36: Welch W36, closed reset', 'hyst': 'pair: earlier pair rule, not the spec',
          'hx': 'hyst: replay rule, closed reset',
          'gr': 'g36+r', 'control': 'control: lamps off from start'}
try:
    font = ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf', 18)
except OSError:
    font = ImageFont.load_default()


def gray(path):
    return np.asarray(Image.open(path).convert('L'), np.float64)


def tiles(g):
    h, w = g.shape
    return g[:h - h % TILE, :w - w % TILE].reshape(h // TILE, TILE, w // TILE, TILE).mean(axis=(1, 3))


def grid_step(d, offset):
    """Mean |step| of D across the lines at offset + k TILE, vertical and horizontal, with 4 px on each side."""
    h, w = d.shape
    steps = []
    for x in range(offset if offset else TILE, w - 4, TILE):
        steps.append(np.abs(d[:, x - 4:x].mean(axis=1) - d[:, x:x + 4].mean(axis=1)).mean())
    for y in range(offset if offset else TILE, h - 4, TILE):
        steps.append(np.abs(d[y - 4:y, :].mean(axis=0) - d[y:y + 4, :].mean(axis=0)).mean())
    return float(np.mean(steps))


def read_log(folder, name='region-ring.log'):
    """{draw: array (h, w, 4)} of a ring log."""
    raw = (folder / name).read_bytes()
    out, at = {}, 0
    while at < len(raw):
        draw, _, w, h = np.frombuffer(raw, np.int32, 4, at)
        at += 16
        out[int(draw)] = np.frombuffer(raw, np.float32, w * h * 4, at).reshape(h, w, 4)
        at += w * h * 16
    return out


def log_rows(folder):
    rows = {}
    for draw, ring in read_log(folder).items():
        age = np.mod(ring[..., 3], 8192)
        valid = ring[..., 1] > 0
        rows[draw] = {'answers': int((ring[..., 2] != 0).sum()), 'open': int(((age < 128) & valid).sum()),
                      'valid': int(valid.sum()), 'cause': int((ring[..., 3] >= 8192).sum())}
    return rows


def label_band(image, text):
    out = Image.new('RGB', (image.width, image.height + 26), (16, 16, 16))
    out.paste(image, (0, 26))
    ImageDraw.Draw(out).text((6, 4), text, fill=(255, 255, 255), font=font)
    return out


def sheet(rows, path, scale=1.0):
    """rows: list of lists of (label, PIL image); one picture per cell, cells in a grid with label bands."""
    cells = [[label_band(im.resize((int(im.width * scale), int(im.height * scale))) if scale != 1 else im, t)
              for t, im in row] for row in rows]
    cw = max(c.width for r in cells for c in r)
    ch = max(c.height for r in cells for c in r)
    out = Image.new('RGB', (len(cells[0]) * (cw + 6) - 6, len(cells) * (ch + 6) - 6), (16, 16, 16))
    for i, r in enumerate(cells):
        for j, c in enumerate(r):
            out.paste(c, (j * (cw + 6), i * (ch + 6)))
    out.save(path)
    print(f'wrote {path}')


def film_compare(folders, names, bkey, boxes, extra=None):
    """Curves, tile difference against b, grid step against b and flicker, for kept frames `names` of each folder."""
    out = {'curves': {}, 'tilesAgainstB': {}, 'gridStep': {}, 'flicker': {}}
    frames = {c: [gray(f / f'{n}.png') for n in names] for c, f in folders.items()}
    for c, gs in frames.items():
        out['curves'][c] = {box: [float(g[y0:y1, x0:x1].mean()) for g in gs] for box, (x0, y0, x1, y1) in boxes.items()}
        curves = {box: np.array(v) for box, v in out['curves'][c].items()}
        t = np.array([tiles(g) for g in gs])
        out['flicker'][c] = {box: float(np.abs(np.diff(v, 2)).max()) for box, v in curves.items()}
        out['flicker'][c]['largest tile'] = float(np.abs(np.diff(t, 2, axis=0)).max())
    tb = np.array([tiles(g) for g in frames[bkey]])
    for c in frames:
        if c == bkey:
            continue
        t = np.array([tiles(g) for g in frames[c]])
        diff = t - tb
        per = np.abs(diff).reshape(len(names), -1).max(axis=1)
        k = int(per.argmax())
        ty, tx = np.unravel_index(np.abs(diff[k]).argmax(), diff[k].shape)
        out['tilesAgainstB'][c] = {'perDraw': [round(float(v), 3) for v in per], 'largest': float(diff[k, ty, tx]),
                                   'at': names[k], 'tile': [int(tx) * TILE, int(ty) * TILE],
                                   'over4': int((per > 4).sum()), 'over8': int((per > 8).sum())}
        steps = [(grid_step(a - b, 0), grid_step(a - b, TILE // 2)) for a, b in zip(frames[c], frames[bkey])]
        out['gridStep'][c] = {'boundary': [round(s[0], 3) for s in steps], 'mid': [round(s[1], 3) for s in steps]}
    return out


def crops(folders, names, box, path, labels=None):
    x0, y0, x1, y1 = box
    rows = [[(f'{(labels or LABELS).get(c, c)}, {n}', Image.open(f / f'{n}.png').convert('RGB').crop(box))
             for c, f in folders.items()] for n in names]
    sheet(rows, path)


part = sys.argv[1]

if part == 'turned':
    # rect-bay-settled turned 90 degrees left (films.sh TURN=90): 200 frozen draws, then the 180-draw film in which the
    # sunbeam leaves the bay behind the camera. No ray of the view sees the cause, so every gate is closed in the film.
    cols = {'bl': F / 'rbst-bl', 'g128ryi': F / 'rbst-g128ryi', 'w36': F / 'rbst-w36', 'hyst': F / 'rbst-hyst',
            'hx': F / 'rbst-hx'}
    names = [f'm{k:04d}-000000' for k in range(180)]
    boxes = {'whole': (0, 0, 1920, 1080), 'wall ahead': (0, 0, 1920, 500), 'floor': (0, 700, 1920, 1080)}
    out = film_compare(cols, names, 'bl', boxes)
    out['log'] = {c: {d - 200: r for d, r in log_rows(f).items() if d >= 200} for c, f in cols.items()}
    for c in ('g128ryi', 'w36', 'hyst', 'hx'):
        rows = out['log'][c]
        out.setdefault('answersInFilm', {})[c] = sum(r['answers'] for r in rows.values())
        first = [k for k, r in sorted(rows.items()) if r['answers'] > 0]
        out.setdefault('firstAnswer', {})[c] = first[0] if first else None
        out.setdefault('openInFilm', {})[c] = max(r['open'] for r in rows.values())
    out['answersInFilm']['bl'] = sum(r['answers'] for r in out['log']['bl'].values())
    (REC / 'yi-turned.json').write_text(json.dumps(out))
    print('whole / wall ahead / floor mean gray at film draws 30 45 60 75 90 120 150 179')
    for c in cols:
        print(c, *(' '.join(f"{out['curves'][c][b][k]:.1f}" for k in (30, 45, 60, 75, 90, 120, 150, 179))
                   for b in boxes), sep='\n  ')
    for c, v in out['tilesAgainstB'].items():
        print(f"{c}: tile vs b largest {v['largest']:+.2f} at {v['at']} tile {v['tile']}, draws over 4: {v['over4']}, "
              f"over 8: {v['over8']}; grid step at 60/70/80/90: "
              + ', '.join(f"{out['gridStep'][c]['boundary'][k]:.2f}/{out['gridStep'][c]['mid'][k]:.2f}" for k in (60, 70, 80, 90)))
    print('answers in film', out['answersInFilm'], 'first answer', out['firstAnswer'], 'open gates', out['openInFilm'])
    print('flicker', json.dumps(out['flicker']))
    crops({'bl': cols['bl'], 'g128ryi': cols['g128ryi'], 'w36': cols['w36'], 'hx': cols['hx'], 'hyst': cols['hyst']},
          [f'm{k:04d}-000000' for k in (60, 70, 80, 90)], (960, 480, 1920, 1080), PIC / 'rect-bay-turned-yi-crops.png')
    sheet([[(f'{LABELS[c]}, film draw {k}', Image.open(cols[c] / f'm{k:04d}-000000.png').convert('RGB'))
            for c in cols] for k in (0, 60, 75, 90, 150)], PIC / 'rect-bay-turned-yi-sheet.png', scale=400 / 1920)

elif part == 'walk':
    # The cabin's lamps switch off at walk frame 60 while the person walks (yifilm.py walk-switch); the walk stops at
    # walk frame 120 = stand frame h 0 (log draw 33 + 160 + 120 = 313). The control: the same walk, lamps off from draw 0.
    cols = {'byi': F / 'ws-byi', 'g128ryi': F / 'ws-g128ryi', 'w36': F / 'ws-w36', 'hx': F / 'ws-hx', 'hyst': F / 'ws-hyst',
            'control': F / 'wo-byi'}
    names = [f'g-{k:06d}' for k in range(50, 120)] + [f'h-{k:06d}' for k in range(160)]
    boxes = {'whole': (0, 0, 1920, 1080), 'floor': (0, 600, 1920, 1080), 'walls and ceiling': (0, 0, 1920, 600)}
    out = film_compare(cols, names, 'byi', boxes)
    out['names'] = names
    stop = 33 + 160 + 120
    out['log'] = {c: {d - stop: r for d, r in log_rows(f).items() if d >= stop - 70} for c, f in cols.items()}
    # the lag: each column against the control after the stop, per box, at stand frames
    out['againstControl'] = {c: {box: [out['curves'][c][box][i] - out['curves']['control'][box][i] for i in range(len(names))]
                                 for box in boxes} for c in cols if c != 'control'}
    (REC / 'yi-walk.json').write_text(json.dumps(out))
    at = [names.index(n) for n in ('g-000055', 'g-000065', 'g-000100', 'h-000000', 'h-000020', 'h-000040', 'h-000060',
                                   'h-000080', 'h-000100', 'h-000130', 'h-000159')]
    print('frames:', [names[i] for i in at])
    for c in cols:
        for box in boxes:
            print(f'{c:8} {box:18}', ' '.join(f"{out['curves'][c][box][i]:6.2f}" for i in at))
    for c, v in out['tilesAgainstB'].items():
        print(f"{c}: tile vs b largest {v['largest']:+.2f} at {v['at']} tile {v['tile']}, over 4: {v['over4']}, "
              f"over 8: {v['over8']}; grid step max {max(out['gridStep'][c]['boundary']):.2f} "
              f"(mid max {max(out['gridStep'][c]['mid']):.2f})")
    for c in cols:
        rows = out['log'][c]
        print(c, 'answers after stop', sum(r['answers'] for k, r in rows.items() if k >= 0),
              'cause anchor-draws', sum(r['cause'] for r in rows.values()),
              'open at h0/h40/h100/h159', [rows.get(k, {}).get('open') for k in (0, 40, 100, 159)],
              'valid at h0/h10/h40', [rows.get(k, {}).get('valid') for k in (0, 10, 40)],
              'first answer after stop', next((k for k, r in sorted(rows.items()) if k >= 0 and r['answers']), None))
    print('flicker', json.dumps(out['flicker']))
    pick = ['g-000055', 'g-000065', 'h-000000', 'h-000040', 'h-000080', 'h-000130']
    sheet([[(f'{LABELS[c]}, {n}', Image.open(cols[c] / f'{n}.png').convert('RGB')) for c in cols] for n in pick],
          PIC / 'walk-switch-yi-sheet.png', scale=384 / 1920)
    crops(cols, ['h-000000', 'h-000040', 'h-000080', 'h-000130'], (480, 480, 1440, 1080), PIC / 'walk-switch-yi-crops.png')

elif part == 'bay':
    # The frozen bay from the start: the sparse takes (bay-<column>-sp and -sp-dif, draws in keep-yi.txt for w36 and
    # hyst, the cold list for b and g128r), the long takes (bay-<column>-long-dif, draws 660 to 1,659), and the full take
    # of d (bay-d-dif, every draw from 300) as the same-draw reference for the answer runs.
    BOXES = {'sunlit patch': (100, 40, 420, 360), 'back wall': (560, 420, 980, 560), 'floor': (600, 650, 1200, 950),
             'right wall': (1300, 100, 1800, 500)}

    def linear(path):
        a = np.asarray(Image.open(path).convert('RGB'), np.float64) / 255.0
        return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ W

    def band(e):
        return ndimage.gaussian_filter(e, 3) - ndimage.gaussian_filter(e, 36)

    main_full = np.mean([linear(p) for p in sorted((R / 'bay-a').glob('f-*.png'))], axis=0)
    ref = {k: np.mean([linear(p) for p in sorted((R / f'bay-d-long{s}').glob('f-*.png'))], axis=0)
           for k, s in (('dif', '-dif'), ('full', ''))}
    cols = ['b', 'g128r', 'w36', 'hx', 'hyst']
    out = {'answers': {}, 'runs': {}, 'patch': {}, 'level': {}, 'long': {}}
    for c in cols:
        log = read_log(R / f'bay-{c}-sp-dif')
        tested = {d: ring[..., 1] >= (72 if c == 'w36' else 36) for d, ring in log.items()}
        moved = {d: ring[..., 2] != 0 for d, ring in log.items()}
        ranges = {'0-127': (0, 127), '128-300': (128, 300), '301-659': (301, 659)}
        out['answers'][c] = {name: {'yes': int(sum(moved[d].sum() for d in range(a, b + 1))),
                                    'tested': int(sum(tested[d].sum() for d in range(a, b + 1)))}
                             for name, (a, b) in ranges.items()}
        runs = []
        for d in range(1, 660):
            for y, x in zip(*np.nonzero(moved[d] & ~moved[d - 1])):
                runs.append([d, int(x), int(y)])
        out['runs'][c] = runs
        longlog = read_log(R / f'bay-{c}-long-dif') if (R / f'bay-{c}-long-dif' / 'region-ring.log').exists() else {}
        if longlog:
            mv = {d: r[..., 2] != 0 for d, r in longlog.items()}
            tt = {d: r[..., 1] >= (72 if c == 'w36' else 36) for d, r in longlog.items()}
            out['long'][c] = {'yes': int(sum(mv[d].sum() for d in range(661, 1660))),
                              'tested': int(sum(tt[d].sum() for d in range(661, 1660))),
                              'runs': int(sum((mv[d] & ~mv[d - 1]).sum() for d in range(661, 1660)))}
    draws = [24, 32, 36, 40, 48, 56, 64, 72, 80, 88, 96, 104, 112, 120, 127, 136, 152, 168, 200, 240, 300, 639, 659]
    for kind, suffix in (('dif', '-dif'), ('full', '')):
        for c in cols:
            patch, level = {}, {}
            for d in draws:
                lum = linear(R / f'bay-{c}-sp{suffix}' / f'f-{d:06d}.png')
                e = band(lum - ref[kind])
                patch[d] = {n: float(e[y0:y1, x0:x1].std() / main_full[y0:y1, x0:x1].mean())
                            for n, (x0, y0, x1, y1) in BOXES.items()}
                level[d] = {n: float(lum[y0:y1, x0:x1].mean() / ref[kind][y0:y1, x0:x1].mean())
                            for n, (x0, y0, x1, y1) in BOXES.items()}
            out['patch'].setdefault(kind, {})[c] = patch
            out['level'].setdefault(kind, {})[c] = level
    # The runs after draw 300 of w36 and hyst against d's still of the same draw, in a 288 x 288 crop around the anchor.
    out['runPatch'] = {}
    rows_dif, rows_full = [], []
    for c in ('w36', 'hx', 'hyst'):
        for d, x, y in out['runs'][c]:
            if d < 301:
                continue
            cx, cy = 96 * x, 96 * y
            box = (max(cx - 144, 0), max(cy - 144, 0), min(cx + 144, 1920), min(cy + 144, 1080))
            vals, rd, rf = {}, [], []
            for k in (-1, 2, 6, 12, 24):
                n = d + k
                p = R / f'bay-{c}-sp-dif' / f'f-{n:06d}.png'
                q = R / 'bay-d-dif' / f'f-{n:06d}.png'
                if not (p.exists() and q.exists()):
                    continue
                e = (linear(p) - linear(q))
                x0, y0, x1, y1 = box
                vals[k] = float(band(e)[y0:y1, x0:x1].std() / main_full[y0:y1, x0:x1].mean())
                rd.append((f'{c} run {d} ({x},{y}) draw {k:+d}', Image.open(p).convert('RGB').crop(box)))
                res = e[y0:y1, x0:x1] / main_full[y0:y1, x0:x1].mean()
                rd.append((f'minus d, x10', Image.fromarray(np.clip(128 + 1280 * res, 0, 255).astype(np.uint8)).convert('RGB')))
                rf.append((f'{c} run {d} full, draw {k:+d}',
                           Image.open(R / f'bay-{c}-sp' / f'f-{n:06d}.png').convert('RGB').crop(box)))
            out['runPatch'][f'{c}:{d}:{x}:{y}'] = vals
            if rd:
                rows_dif.append(rd)
                rows_full.append(rf)
    if rows_dif:
        sheet(rows_dif, PIC / 'bay-runs-yi-dif.png')
        sheet(rows_full, PIC / 'bay-runs-yi-full.png')
    (REC / 'yi-bay.json').write_text(json.dumps(out))
    print('answers', json.dumps(out['answers']))
    print('long', json.dumps(out['long']))
    print('runs', {c: out['runs'][c] for c in ('w36', 'hx', 'hyst')})
    print('run patches', json.dumps(out['runPatch']))
    for kind in ('dif', 'full'):
        print(f'\n{kind}: patch floor / back wall; level floor / back wall')
        for d in draws:
            print(f'{d:4}', ' | '.join(f"{out['patch'][kind][c][d]['floor']:.4f}/{out['patch'][kind][c][d]['back wall']:.4f} "
                                      f"({out['level'][kind][c][d]['floor']:.3f}/{out['level'][kind][c][d]['back wall']:.3f})"
                                      for c in cols))
    for region in ('floor', 'back wall'):
        x0, y0, x1, y1 = BOXES[region]
        slug = region.replace(' ', '-')
        sheet([[(f'{LABELS[c]}, draw {d}', Image.open(R / f'bay-{c}-sp-dif' / f'f-{d:06d}.png').convert('RGB').crop((x0, y0, x1, y1)))
                for c in cols] for d in (40, 72, 96, 127, 168, 240, 300, 659)], PIC / f'bay-cold-yi-{slug}-dif.png')
        sheet([[(f'{LABELS[c]}, draw {d}', Image.open(R / f'bay-{c}-sp' / f'f-{d:06d}.png').convert('RGB').crop((x0, y0, x1, y1)))
                for c in cols] for d in (40, 72, 96, 127, 168, 240, 300, 659)], PIC / f'bay-cold-yi-{slug}-full.png')
elif part == 'look':
    # The unlabeled 1:1 pictures of the reading. The turned take's floor (output x 0 to 960, y 600 to 1080) at film draws
    # 57, 65 and 75, one row per draw: rect-bay-turned-yi-1to1-b-w36.png (b left, w36 right) and
    # rect-bay-turned-yi-1to1-pair-g128r.png (pair left, g128+r right) and rect-bay-turned-yi-1to1-b-hyst.png (b left,
    # hyst (hx) right). The walk's stand frame 100: seams (output x 960 to 1920, y 0 to 540 of b's frame with the part
    # from x 1440 replaced by g128+r, w36, hyst (hx) and pair, one row each), and
    # w36 minus b (Gaussian sigma 8 px) times 20 on 128 of the whole frame at half size.
    for pair, name in ((('rbst-bl', 'rbst-w36'), 'b-w36'), (('rbst-hyst', 'rbst-g128ryi'), 'pair-g128r'),
                       (('rbst-bl', 'rbst-hx'), 'b-hyst')):
        out = Image.new('RGB', (1926, 3 * 486))
        for i, k in enumerate((57, 65, 75)):
            for j, c in enumerate(pair):
                out.paste(Image.open(F / c / f'm{k:04d}-000000.png').convert('RGB').crop((0, 600, 960, 1080)),
                          (j * 966, i * 486))
        out.save(PIC / f'rect-bay-turned-yi-1to1-{name}.png')
    rows = []
    for c in ('ws-g128ryi', 'ws-w36', 'ws-hx', 'ws-hyst'):
        a = Image.open(F / 'ws-byi' / 'h-000100.png').convert('RGB')
        a.paste(Image.open(F / c / 'h-000100.png').convert('RGB').crop((1440, 0, 1920, 1080)), (1440, 0))
        rows.append(a.crop((960, 0, 1920, 540)))
    out = Image.new('RGB', (960, len(rows) * 560))
    for i, r in enumerate(rows):
        out.paste(r, (0, i * 560))
    out.save(PIC / 'walk-switch-yi-seams.png')
    d = ndimage.gaussian_filter(gray(F / 'ws-w36' / 'h-000100.png') - gray(F / 'ws-byi' / 'h-000100.png'), 8)
    Image.fromarray(np.clip(128 + 20 * d, 0, 255).astype(np.uint8)).resize((960, 540)).save(
        PIC / 'walk-switch-yi-w36-minus-b-x20.png')
else:
    sys.exit(f'no part {part}')
