# Tool code of t0-3vwx. measure5.py <walk|switch> <label|run folder>...: the per-frame measures of macdyn.sh's picture runs
# (queue-a.sh; PNGs at 1920 x 1080, ACES and bloom, 4.08 stops, the scale played back from a card's size log), each with
# its size log and history log in the run folder. It writes <run folder>/measure5.tsv (one row per kept frame) and prints
# per span the means, split by draws whose render size changed against the draw before ("changed") and the others
# ("one size"). Film frame k of the switch is draw 33 + k; the walk's g frame k is draw 213 + k and h frame k draw 333 + k.
# The measures, display-linear luminance:
#   - jambs (switch only): the mean of the right jamb crop (x 1770 to 1807, y 495 to 584) and of the left jamb crop
#     (x 113 to 145, y 500 to 579), the crops of the acceptance lead's sizeramp.py;
#   - grain: per crop, the rms of the luminance minus its 5 x 5 box mean, over the crop's mean, in %: the fine grain that
#     changes from pixel to pixel. Crops: the floor (x 150 to 1769, y 780 to 1049) and the left wall (x 90 to 449,
#     y 225 to 494) for the switch; for the walk, the bottom third and the middle third of the frame;
#   - history (scratch-history-log.patch): the slow diffuse history's mean length in draws and the share of pixels under
#     8 draws, as the draw before this frame's draw left them (the log's line of draw d - 1 is read at the start of draw d,
#     so a frame's own line is that of its draw).
# Spans: the switch's 20 draws before off, 0 to 60 and 60 to 200 draws after off, 0 to 100 draws after on; the walk's last
# 60 draws of the walk and 0 to 60 and 60 to 200 draws after the stop.
import os
import sys
import numpy as np
from PIL import Image

scene, specs = sys.argv[1], [s.split('|') for s in sys.argv[2:]]
if scene == 'switch':
    frames = [(f'f-{k:06d}', 33 + k) for k in range(140, 460)]
    spans = {'before off': range(0, 20), 'off 0-60': range(20, 80), 'off 60-200': range(80, 220), 'on 0-100': range(220, 320)}
    crops = {'floor': (150, 780, 1770, 1050), 'wall': (90, 225, 450, 495)}
    log = 'size-193-393.txt'
else:
    frames = [(f'g-{k:06d}', 213 + k) for k in range(60, 120)] + [(f'h-{k:06d}', 333 + k) for k in range(200)]
    spans = {'walk last 60': range(0, 60), 'stop 0-60': range(60, 120), 'stop 60-200': range(120, 260)}
    crops = {'bottom': (0, 720, 1920, 1080), 'middle': (0, 360, 1920, 720)}
    log = 'size-none.txt'
RIGHT, LEFT = (1770, 495, 1808, 585), (113, 500, 146, 580)


def luminance(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(float) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]


def box5(a):
    p = np.pad(a, 2, mode='edge')
    c = p.cumsum(0).cumsum(1)
    c = np.pad(c, ((1, 0), (1, 0)))
    return (c[5:, 5:] - c[:-5, 5:] - c[5:, :-5] + c[:-5, :-5]) / 25


def grain(y, box):
    x0, y0, x1, y1 = box
    a = y[y0:y1, x0:x1]
    return 100 * float(np.sqrt(np.mean((a - box5(a)) ** 2)) / a.mean())


for label, folder in specs:
    run_log = log
    if scene == 'switch' and not os.path.exists(f'{folder}/{log}'):
        run_log = 'size-none.txt'  # the still-light run l-X5: the lamps stay on, so its logs are named "none"
    sizes = [tuple(l.split()[1:3]) for l in open(f'{folder}/{run_log}')]
    history = {}
    hist_name = run_log.replace('size-', 'history-')
    try:
        for l in open(f'{folder}/{hist_name}'):
            f, w, h, mean, short, zero = l.split()
            history[int(f)] = (float(mean), float(short))
    except FileNotFoundError:
        pass
    rows = []
    for name, draw in frames:
        y = luminance(f'{folder}/{name}.png')
        row = {'frame': name, 'draw': draw, 'changed': int(sizes[draw] != sizes[draw - 1])}
        if scene == 'switch':
            row['right'] = float(y[RIGHT[1]:RIGHT[3], RIGHT[0]:RIGHT[2]].mean())
            row['left'] = float(y[LEFT[1]:LEFT[3], LEFT[0]:LEFT[2]].mean())
        for c, box in crops.items():
            row[f'grain {c}'] = grain(y, box)
        row['history mean'], row['history <8'] = history.get(draw, (float('nan'), float('nan')))
        rows.append(row)
    keys = list(rows[0])
    with open(f'{folder}/measure5.tsv', 'w') as stream:
        stream.write('\t'.join(keys) + '\n')
        for r in rows:
            stream.write('\t'.join(f'{r[k]:.6g}' if isinstance(r[k], float) else str(r[k]) for k in keys) + '\n')
    print(f'== {label} ({folder})')
    measures = [k for k in keys if k not in ('frame', 'draw', 'changed')]
    for span, idx in spans.items():
        part = [rows[i] for i in idx]
        cells = []
        for m in measures:
            ch = [r[m] for r in part if r['changed']]
            one = [r[m] for r in part if not r['changed']]
            fmt = lambda v: f'{np.nanmean(v):.4g}' if v else '-'
            cells.append(f'{m} {fmt(one)} / {fmt(ch)}')
        nch = sum(r['changed'] for r in part)
        print(f'  {span} ({len(part) - nch} one size / {nch} changed): ' + ', '.join(cells))
