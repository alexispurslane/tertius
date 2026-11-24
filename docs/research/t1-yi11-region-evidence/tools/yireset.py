# Tool code of t1-yi11. yireset.py <check|gate|walk|look>: the reset comparison of 2026-11-24 in the walk with the lamp
# switch (yifilm.py walk-switch and walk-off). Build yk (scratch-yk.patch) draws both arms in one binary:
#   hxc (folders ws-hxk, wo-hxk): the hyst rule with the closed reset, TERTIUS_X_REGION_CLOSED=hyst (yj's hx);
#   hxo (folders ws-hxo, wo-hxo): the same rule with yg's open reset, TERTIUS_X_REGION_CLOSED=hyst-open.
# b is today's test (ws-byi, wo-byi of build yi; ws-bk, wo-bk of build yk for the check). Parts:
#   check: byte equality of every kept PNG, both logs, exposure.json and script.json: ws-hxk against yj's ws-hx, ws-bk
#     against ws-byi and wo-bk against wo-byi.
#   gate: from the ring logs (region-ring.log: r light, g valid draws, b signed answer, a = causeAge + 8192 x causes):
#     per draw and arm the open gates, the answers by sign and by gate state, the anchors with no valid draw and the
#     counted causes; for the open arm, the event that opened each open gate (a cause, a draw that is not valid or a
#     draw that does not continue the draw before); the first draw where the two arms' logs differ, by channel.
#   walk: mean gray (0 to 255, sRGB PNG) of the whole frame, the floor and the walls and ceiling per kept frame and run;
#     the gap of hxc against b, the part that hxo removes, the paired on minus off differences.
#   look: the 1:1 pictures (seams, pane-swapped pairs, a blind pair with its key in the scratch folder only).
# Writes yi-reset-<part>.json beside the record and the pictures. Run it in the dev shell.
import json
import random
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

F = Path('/private/tmp/t1-yi11/films')
REC = Path(__file__).resolve().parent.parent
PIC = REC / 'pictures'
STOP = 33 + 160 + 120  # the log draw of stand frame h 0
NAMES = [f'g-{k:06d}' for k in range(50, 120)] + [f'h-{k:06d}' for k in range(160)]
BOXES = {'whole': (0, 0, 1920, 1080), 'floor': (0, 600, 1920, 1080), 'walls and ceiling': (0, 0, 1920, 600)}
RUNS = {'b on': 'ws-byi', 'b off': 'wo-byi', 'hxc on': 'ws-hxk', 'hxc off': 'wo-hxk', 'hxo on': 'ws-hxo',
        'hxo off': 'wo-hxo'}
part = sys.argv[1]


def read_log(folder, name='region-ring.log'):
    """{draw: (index, array (h, w, 4))} of a ring log."""
    raw, out, at = (folder / name).read_bytes(), {}, 0
    while at < len(raw):
        draw, index, w, h = np.frombuffer(raw, np.int32, 4, at)
        at += 16
        out[int(draw)] = (int(index), np.frombuffer(raw, np.float32, w * h * 4, at).reshape(h, w, 4))
        at += w * h * 16
    return out


def gray(path):
    return np.asarray(Image.open(path).convert('L'), np.float64)


if part == 'check':
    out = {}
    for new, old in (('ws-hxk', 'ws-hx'), ('ws-bk', 'ws-byi'), ('wo-bk', 'wo-byi')):
        names = sorted(p.name for p in (F / old).iterdir() if p.suffix == '.png' or p.name in
                       ('region-ring.log', 'region-t.log', 'exposure.json', 'script.json'))
        same = [n for n in names if (F / new / n).exists() and (F / new / n).read_bytes() == (F / old / n).read_bytes()]
        out[f'{new} = {old}'] = {'files': len(names), 'identical': len(same),
                                 'different': sorted(set(names) - set(same))[:20]}
        print(new, old, len(names), 'files,', len(same), 'identical')
    (REC / 'yi-reset-check.json').write_text(json.dumps(out, indent=1))

elif part == 'gate':
    out = {}
    for scene in ('ws', 'wo'):
        logs = {arm: read_log(F / f'{scene}-hx{k}') for arm, k in (('hxc', 'k'), ('hxo', 'o'))}
        draws = sorted(logs['hxc'])
        assert draws == sorted(logs['hxo']), 'the arms log different draws'
        rows, first = {}, {}
        events = {}  # draw -> per-anchor kind of a zero age in the open arm: 1 cause, 2 not valid, 3 no continuation
        previous = None
        for d in draws:
            row = {}
            for arm in ('hxc', 'hxo'):
                index, r = logs[arm][d]
                age, causes = np.mod(r[..., 3], 8192), np.floor(r[..., 3] / 8192)
                valid, answer = r[..., 1], r[..., 2]
                is_open = age < 128
                row[arm] = {'index': index, 'open': int(is_open.sum()), 'notValid': int((valid == 0).sum()),
                            'causes': int((causes > 0).sum()),
                            'answers+': int((answer > 0).sum()), 'answers-': int((answer < 0).sum()),
                            'openAnswers': int(((answer != 0) & is_open).sum()),
                            'closedAnswers': int(((answer != 0) & ~is_open).sum())}
            index, r = logs['hxo'][d]
            continues = previous is not None and previous == d - 1 and index > 0
            kind = np.where(np.floor(r[..., 3] / 8192) > 0, 1, np.where(r[..., 1] == 0, 2, np.where(continues, 0, 3)))
            events[d] = np.where(np.mod(r[..., 3], 8192) == 0, kind, 0)
            age = np.mod(r[..., 3], 8192).astype(int)
            opened = {'cause': 0, 'not valid': 0, 'no continuation': 0, 'unknown': 0}
            for y, x in zip(*np.nonzero(age < 128)):
                k = events[d - age[y, x]][y, x] if d - age[y, x] in events else 0
                opened[{1: 'cause', 2: 'not valid', 3: 'no continuation'}.get(int(k), 'unknown')] += 1
            row['hxo']['openedBy'] = opened
            row['continues'] = bool(continues)
            a, c = logs['hxc'][d][1], logs['hxo'][d][1]
            for ch, name in ((3, 'cause age'), (2, 'answer'), (0, 'light'), (1, 'valid draws')):
                if name not in first and (a[..., ch] != c[..., ch]).any():
                    ys, xs = np.nonzero(a[..., ch] != c[..., ch])
                    first[name] = {'draw': d, 'standFrame': d - STOP, 'anchors': int(len(ys)),
                                   'example': [int(xs[0]), int(ys[0])],
                                   'hxc': float(a[ys[0], xs[0], ch]), 'hxo': float(c[ys[0], xs[0], ch])}
            rows[d] = row
            previous = d
        out[scene] = {'firstDifference': first, 'rows': {d - STOP: v for d, v in rows.items()}}
        print(scene, 'first differences', json.dumps(first))
        for arm in ('hxc', 'hxo'):
            def total(key, lo, hi):
                return sum(rows[d][arm][key] for d in draws if lo <= d - STOP <= hi)
            print(f'  {arm}: draws {draws[0]}..{draws[-1]}; not-valid anchor-draws walking (walk 0..119) '
                  f'{total("notValid", -120, -1)}, standing {total("notValid", 0, 199)}; causes walking '
                  f'{total("causes", -120, -1)}, standing {total("causes", 0, 199)}; answers standing + '
                  f'{total("answers+", 0, 159)} - {total("answers-", 0, 159)} (open gate {total("openAnswers", 0, 159)},'
                  f' closed {total("closedAnswers", 0, 159)}); open gates at stand 0/40/80/100/128/159 '
                  f'{[rows[STOP + k][arm]["open"] for k in (0, 40, 80, 100, 128, 159)]}')
        print('  hxo opened by at stand 0/40/100/127/128:',
              [rows[STOP + k]['hxo']['openedBy'] for k in (0, 40, 100, 127, 128)])
        resets = [d for d in draws if not rows[d]['continues']]
        print('  draws that do not continue the draw before:', resets)
        out[scene]['noContinuation'] = resets
    (REC / 'yi-reset-gate.json').write_text(json.dumps(out))

elif part == 'walk':
    curves = {run: {box: [] for box in BOXES} for run in RUNS}
    frames = {}
    for run, folder in RUNS.items():
        for n in NAMES:
            g = gray(F / folder / f'{n}.png')
            if n in ('h-000040', 'h-000080', 'h-000100', 'h-000159'):
                frames[(run, n)] = g
            for box, (x0, y0, x1, y1) in BOXES.items():
                curves[run][box].append(float(g[y0:y1, x0:x1].mean()))
    c = {run: {box: np.array(v) for box, v in bs.items()} for run, bs in curves.items()}
    out = {'names': NAMES, 'curves': curves, 'boxes': BOXES}
    stand = [NAMES.index(f'h-{k:06d}') for k in range(160)]
    derived = {}
    for box in BOXES:
        on_b, on_c, on_o = c['b on'][box], c['hxc on'][box], c['hxo on'][box]
        off_b, off_c, off_o = c['b off'][box], c['hxc off'][box], c['hxo off'][box]
        gap = on_c - on_b
        removed = on_c - on_o
        derived[box] = {
            'hxc on - b off (the earlier measure)': on_c - off_b, 'hxo on - b off': on_o - off_b,
            'b on - b off': on_b - off_b,
            'gap: hxc on - b on': gap, 'removed: hxc on - hxo on': removed, 'left: hxo on - b on': on_o - on_b,
            'control change: hxc off - b off': off_c - off_b, 'control change: hxo off - b off': off_o - off_b,
            'paired b: on - off': on_b - off_b, 'paired hxc: on - off': on_c - off_c, 'paired hxo: on - off': on_o - off_o,
            'paired gap: hxc - b': (on_c - off_c) - (on_b - off_b), 'paired removed: hxc - hxo': (on_c - off_c) - (on_o - off_o)}
    out['derived'] = {box: {k: [round(float(x), 4) for x in v] for k, v in d.items()} for box, d in derived.items()}
    # the share of the gap that hxo removes, on the mean over stand frames 40 to 159 (the gap is small before 40)
    share = {}
    for box in BOXES:
        d = derived[box]
        sl = [NAMES.index(f'h-{k:06d}') for k in range(40, 160)]
        share[box] = {'gap mean': float(d['gap: hxc on - b on'][sl].mean()),
                      'removed mean': float(d['removed: hxc on - hxo on'][sl].mean()),
                      'paired gap mean': float(d['paired gap: hxc - b'][sl].mean()),
                      'paired removed mean': float(d['paired removed: hxc - hxo'][sl].mean())}
        share[box]['share'] = share[box]['removed mean'] / share[box]['gap mean']
        share[box]['paired share'] = share[box]['paired removed mean'] / share[box]['paired gap mean']
    out['share 40 to 159'] = share
    # pixel scale: Gaussian sigma 8 px, 99th percentile of the signed differences at four stand frames
    pix = {}
    for n in ('h-000040', 'h-000080', 'h-000100', 'h-000159'):
        pix[n] = {}
        for name, a, b in (('hxc - b', 'hxc on', 'b on'), ('hxo - b', 'hxo on', 'b on'), ('hxc - hxo', 'hxc on', 'hxo on')):
            dd = ndimage.gaussian_filter(frames[(a, n)] - frames[(b, n)], 8)
            pix[n][name] = {'p99': float(np.percentile(dd, 99)), 'p1': float(np.percentile(dd, 1))}
    out['pixels sigma 8'] = pix
    (REC / 'yi-reset-walk.json').write_text(json.dumps(out))
    at = [NAMES.index(n) for n in ('g-000055', 'g-000065', 'g-000100', 'h-000000', 'h-000020', 'h-000040', 'h-000060',
                                   'h-000080', 'h-000100', 'h-000130', 'h-000159')]
    print('frames:', [NAMES[i] for i in at])
    for box in BOXES:
        print(box)
        for k, v in derived[box].items():
            print(f'  {k:40}', ' '.join(f'{v[i]:+6.2f}' for i in at))
    print(json.dumps(share, indent=1))
    print(json.dumps(pix, indent=1))

elif part == 'look':
    # Seams at stand frames 40, 80 and 100: b's frame (output x 960 to 1920, y 0 to 540) with x 1440 onward from hxo
    # (row 1) and from hxc (row 2). Pane-swapped pairs: output x 960 to 1920, y 0 to 540 at stand frames 40, 80 and 100,
    # row by row hxo | hxc, then hxc | hxo. The blind pair: the same crops in an order drawn at random per row, unlabeled;
    # its key goes to the scratch folder named in argv[2], and the record gets it only after the reading.
    box = (960, 0, 1920, 540)
    for k in (40, 80, 100):
        rows = []
        for folder in ('ws-hxo', 'ws-hxk'):
            a = Image.open(F / 'ws-byi' / f'h-{k:06d}.png').convert('RGB')
            a.paste(Image.open(F / folder / f'h-{k:06d}.png').convert('RGB').crop((1440, 0, 1920, 1080)), (1440, 0))
            rows.append(a.crop(box))
        out = Image.new('RGB', (960, 2 * 560))
        for i, r in enumerate(rows):
            out.paste(r, (0, i * 560))
        out.save(PIC / f'walk-reset-seams-h{k:03d}.png')
    for order, name in (((('ws-hxo', 'ws-hxk')), 'hxo-hxc'), (('ws-hxk', 'ws-hxo'), 'hxc-hxo')):
        out = Image.new('RGB', (1926, 3 * 546))
        for i, k in enumerate((40, 80, 100)):
            for j, folder in enumerate(order):
                out.paste(Image.open(F / folder / f'h-{k:06d}.png').convert('RGB').crop(box), (j * 966, i * 546))
        out.save(PIC / f'walk-reset-1to1-{name}.png')
    # where the measured difference lies: hxc minus hxo (Gaussian sigma 8 px) times 20 on 128, whole frame at half size
    for k in (100, 159):
        d = ndimage.gaussian_filter(gray(F / 'ws-hxk' / f'h-{k:06d}.png') - gray(F / 'ws-hxo' / f'h-{k:06d}.png'), 8)
        Image.fromarray(np.clip(128 + 20 * d, 0, 255).astype(np.uint8)).resize((960, 540)).save(
            PIC / f'walk-reset-hxc-minus-hxo-x20-h{k:03d}.png')
    scratch = Path(sys.argv[2])
    rng = random.Random(20261124)
    key = []
    out = Image.new('RGB', (1926, 4 * 546))
    for i, k in enumerate((40, 80, 100, 130)):
        order = ['ws-hxo', 'ws-hxk']
        rng.shuffle(order)
        key.append({'standFrame': k, 'left': order[0], 'right': order[1]})
        for j, folder in enumerate(order):
            out.paste(Image.open(F / folder / f'h-{k:06d}.png').convert('RGB').crop(box), (j * 966, i * 546))
    out.save(PIC / 'walk-reset-blind.png')
    (scratch / 'walk-reset-blind-key.json').write_text(json.dumps(key))
    print('wrote the pictures; the blind key is in', scratch / 'walk-reset-blind-key.json')
else:
    sys.exit(f'no part {part}')
