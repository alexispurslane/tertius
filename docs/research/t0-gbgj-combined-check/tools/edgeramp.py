# Tool code of t0-gbgj. edgeramp.py <ramp folder> <film dump> <film size log> <chart.png>: the two outer window edges of
# the switch cabin at every render size, with the light held still, from an edge ramp (edgeramp.sh: the lamps on, 40
# draws per size, the last draw of each size and the draw 10 before it kept), against the wobble film's edges.
# The edges are those of wobble.py (t0-5tyj's wobble.py of b53c037f1): on rows 350 to 369 of the frame scaled to
# 1280 x 720 with Lanczos, the left edge of the left wall's window opening (near x 246) and the right edge of the right
# wall's window opening (near x 1033), each the peak of the brightness gradient refined by a parabola, searched within
# 6 px of the place in the size before; all places are in output px (x 1.5). The distance is right - left, the midpoint
# (left + right) / 2.
# It prints, per measure (left, right, distance, midpoint):
#   - the range and the rms around the mean of the per-size places (the last draw of each size) over the sizes;
#   - the floor: the rms of the difference between the two kept draws of one size;
#   - the correlation with the render width, and the correlation of the left edge with the right edge;
#   - against the film (wobble.py's WOBBLE_DUMP lines of one run, "<frame> <distance> <midpoint> <w> <h> <scale>", film
#     frame k is draw 33 + k): for the sizes that the film visits, the film's mean place per size on one-size draws
#     against the ramp's place, both taken around their mean over those sizes (correlation and rms of the difference);
#     then the film's value minus the ramp's place of its draw's size, around its mean, on one-size and on changed
#     draws, beside the film's value around its mean. What the ramp's place removes belongs to the render size.
# The chart: per measure, the ramp's place against the render width (red: last draw, orange: 10 draws before) and the
# film's mean per size on one-size draws (blue), shifted by the mean difference over the shared sizes.
# Twin: edge() and the read at 1280 x 720 with Lanczos are copied in wobble.py, and the README compares the two tools' outputs. Change both copies together.
import json
import sys
import numpy as np
from PIL import Image, ImageDraw

ramp, dump, size_log, chart = sys.argv[1:5]
SCALE, STILL = 1.5, 33


def edge(profile, near):
    g = np.abs(np.diff(profile))
    lo = int(round(near)) - 6
    i = lo + int(np.argmax(g[lo:lo + 13]))
    a, b, c = g[i - 1], g[i], g[i + 1]
    shift = 0.5 * (a - c) / (a - 2 * b + c) if a - 2 * b + c != 0 else 0.0
    return i + 0.5 + shift


def places(path, near):
    f = np.asarray(Image.open(path).convert('L').resize((1280, 720), Image.LANCZOS)).astype(float)
    row = f[350:370].mean(0)
    return edge(row, near[0]), edge(row, near[1])


meta = json.load(open(f'{ramp}/sizes.json'))
n, sizes = meta['n'], meta['sizes']
near = (246.0, 1033.0)
last, early = [], []
for i, (w, h, s) in enumerate(sizes):
    near = places(f'{ramp}/frames/f-{i * n + n - 1:06d}.png', near)
    last.append(near)
    early.append(places(f'{ramp}/frames/f-{i * n + n - 11:06d}.png', near))


def measures(lr):
    a = SCALE * np.array(lr)
    left, right = a[:, 0], a[:, 1]
    return {'left': left, 'right': right, 'distance': right - left, 'midpoint': (left + right) / 2}


M, E = measures(last), measures(early)
width = np.array([w for w, _, _ in sizes], float)
key = {(w, h): i for i, (w, h, _) in enumerate(sizes)}
c = lambda u, v: float(np.corrcoef(u, v)[0, 1])
print(f'edge ramp: {len(sizes)} sizes, {n} draws per size, lamps on; places in px of the 1920 x 1080 output')
for name, v in M.items():
    floor = np.sqrt(np.mean((v - E[name]) ** 2))
    print(f'  {name}: mean {v.mean():.2f}, range {v.max() - v.min():.2f} ({v.min():.2f} to {v.max():.2f}), rms around the mean '
          f'{v.std():.3f}; floor (rms of last minus 10 draws before) {floor:.3f}; correlation with render width {c(v, width):+.3f}')
print(f'  left against right over the sizes: correlation {c(M["left"], M["right"]):+.3f}')

film = np.loadtxt(dump)
log = [tuple(map(int, l.split()[1:3])) for l in open(size_log)]
frame, dist, mid = film[:, 0].astype(int), film[:, 1], film[:, 2]
F = {'left': mid - dist / 2, 'right': mid + dist / 2, 'distance': dist, 'midpoint': mid}
pairs = [(int(w), int(h)) for w, h in film[:, 3:5]]
changed = np.array([log[k + STILL] != log[k + STILL - 1] for k in frame])
missing = sorted({p for p in pairs if p not in key})
print(f'\nthe film: {len(frame)} draws, {len(set(pairs))} sizes, {int(changed.sum())} changed draws; sizes not in the ramp: {missing}')
idx = np.array([key.get(p, -1) for p in pairs])
ok = idx >= 0
shared = sorted({p for p, o, ch in zip(pairs, ok, changed) if o and not ch})
fits = {}
for name in M:
    fm = np.array([F[name][[p == q and not ch for q, ch in zip(pairs, changed)]].mean() for p in shared])
    rm = np.array([M[name][key[p]] for p in shared])
    a, b = fm - fm.mean(), rm - rm.mean()
    fits[name] = (shared, fm - (fm - rm).mean())
    v = F[name][ok]
    pred = M[name][idx[ok]]
    rest = v - pred
    rest = rest - rest[~changed[ok]].mean()
    raw = v - v[~changed[ok]].mean()
    ch = changed[ok]
    r = lambda x, sel: np.sqrt(np.mean(x[sel] ** 2))
    print(f'  {name}: over {len(shared)} shared sizes, the film\'s one-size mean per size against the ramp: correlation '
          f'{c(a, b):+.3f}, rms of the difference {np.sqrt(np.mean((a - b) ** 2)):.3f} (film rms {a.std():.3f}, ramp rms {b.std():.3f})')
    print(f'    the film around its one-size mean: one size {r(raw, ~ch):.3f}, changed {r(raw, ch):.3f}; minus the ramp\'s place of '
          f'its size: one size {r(rest, ~ch):.3f}, changed {r(rest, ch):.3f}')

W, PH, L = 1300, 250, 70
im = Image.new('RGB', (W, PH * 4 + 40), 'white')
d = ImageDraw.Draw(im)
x = lambda w: L + (w - 960) / 960 * (W - L - 20)
for p, name in enumerate(M):
    top = p * PH
    v, e = M[name], E[name]
    ws, fv = fits[name]
    allv = np.concatenate([v, e, fv])
    lo, hi = allv.min() - 0.3, allv.max() + 0.3
    y = lambda u: top + 26 + (hi - u) / (hi - lo) * (PH - 56)
    for u in np.arange(np.ceil(lo * 2) / 2, hi, 0.5):
        d.line([(L, y(u)), (W - 20, y(u))], fill=(232, 232, 232))
        d.text((6, y(u) - 6), f'{u:.1f}', fill='black')
    for wv, ev in zip(width, e):
        d.ellipse([x(wv) - 2, y(ev) - 2, x(wv) + 2, y(ev) + 2], fill=(240, 160, 40))
    for wv, vv in zip(width, v):
        d.ellipse([x(wv) - 3, y(vv) - 3, x(wv) + 3, y(vv) + 3], fill=(200, 60, 60))
    for (wv, _), fvv in zip(ws, fv):
        d.rectangle([x(wv) - 3, y(fvv) - 3, x(wv) + 3, y(fvv) + 3], outline=(40, 110, 200), width=2)
    d.text((L, top + 6), f'{name} of the two window edges (output px) against the render width: red last draw of each size, '
                         f'orange 10 draws before, blue the film\'s one-size mean per size (shifted)', fill='black')
for w in range(960, 1921, 64):
    d.text((x(w) - 14, PH * 4 + 10), str(w), fill='black')
d.text((L, PH * 4 + 24), 'render width (output 1920 x 1080), lamps on, 40 draws per size', fill='black')
im.save(chart)
print(chart, im.size)
