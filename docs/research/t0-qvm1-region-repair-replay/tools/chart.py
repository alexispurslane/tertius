# Tool code of the region repair's replay (t0-qvm1). chart.py <replay.json> <p.json> <out png>: the cap against the test's
# statistic z = |D| / a (the drift over the assumed spread), with the logged fires' distributions under it.
#   Top: the cap in draws of today's rule (the cliff: 32 under the bar of 3, 8 above it) and of the continuous cap with the
#        assumed spread, n = p^(2/3) * W / (2 (z - 3))^(2/3) between 8 and 32, for p = 1, 2 and 4 and the median p measured
#        over the stills (pmeasure.py).
#   Bottom: the share of the fires per bin of z (width 0.25) of today's rule: false fires (the four frozen stills
#        together) and true fires (the fair switch's 40 draws after off and after on, and rect-bay's film), each
#        normalized to its own count. Fires above z = 10 sit in the last bin.
import json
import sys

import numpy as np
from PIL import Image, ImageDraw

data = json.loads(open(sys.argv[1]).read())
pdata = json.loads(open(sys.argv[2]).read())
W = 18
Z0, Z1 = 2.5, 10.0
WIDTH, HEIGHT = 1200, 860
L, Rm, T1, B1, T2, B2 = 80, 30, 40, 440, 500, 800
img = Image.new('RGB', (WIDTH, HEIGHT), 'white')
d = ImageDraw.Draw(img)


def zx(z):
    return L + (min(max(z, Z0), Z1) - Z0) / (Z1 - Z0) * (WIDTH - L - Rm)


def cy(n):
    return B1 - (n - 0) / 34 * (B1 - T1)


# Axes of the top panel.
d.line([(L, T1), (L, B1), (WIDTH - Rm, B1)], fill='black')
for n in (8, 16, 24, 32):
    d.line([(L - 5, cy(n)), (WIDTH - Rm, cy(n))], fill=(225, 225, 225))
    d.text((L - 30, cy(n) - 6), str(n), fill='black')
for z in range(3, 11):
    d.line([(zx(z), B1), (zx(z), B1 + 5)], fill='black')
    d.text((zx(z) - 4, B1 + 8), str(z), fill='black')
d.text((L, 12), 'The cap in draws against the statistic z = |D| / a (the bar is z = 3)', fill='black')
pmed = float(np.median([v['p'][1] for v in pdata.values()])) if pdata else None
curves = [('today: the cliff', (200, 40, 40), None), ('continuous, p = 1', (40, 90, 200), 1.0),
          ('continuous, p = 2', (60, 160, 90), 2.0), ('continuous, p = 4', (150, 90, 190), 4.0)]
if pmed:
    curves.append((f'continuous, p = {pmed:.2f} (the stills\' median)', (230, 140, 20), pmed))
zs = np.linspace(Z0, Z1, 600)
for i, (name, colour, p) in enumerate(curves):
    if p is None:
        ns = np.where(zs > 3, 8.0, 32.0)
    else:
        with np.errstate(all='ignore'):
            ns = np.where(zs > 3, np.clip(p ** (2 / 3) * W / (2 * (zs - 3)) ** (2 / 3), 8, 32), 32)
    pts = [(zx(z), cy(n)) for z, n in zip(zs, ns)]
    d.line(pts, fill=colour, width=3 if p is None else 2)
    d.line([(WIDTH - 360, T1 + 20 + 18 * i), (WIDTH - 330, T1 + 20 + 18 * i)], fill=colour, width=3)
    d.text((WIDTH - 322, T1 + 14 + 18 * i), name, fill='black')

# The bottom panel: the fires' distributions.
false = []
for name, values in data['fires'].items():
    if name not in ('switch off and on', 'rect-bay'):
        false += values
groups = [('false fires: the four frozen stills', (120, 120, 120), np.array(false)),
          ('true fires: the switch, 40 draws after off and on', (200, 40, 40), np.array(data['fires'].get('switch off and on', []))),
          ('true fires: rect-bay\'s film', (40, 90, 200), np.array(data['fires'].get('rect-bay', [])))]
bins = np.arange(3.0, Z1 + 0.25, 0.25)
top = 0
hists = []
for name, colour, v in groups:
    if len(v) == 0:
        hists.append(None)
        continue
    h, _ = np.histogram(np.minimum(v, Z1 - 1e-6), bins=bins)
    h = h / len(v)
    hists.append(h)
    top = max(top, h.max())
d.line([(L, T2), (L, B2), (WIDTH - Rm, B2)], fill='black')
for z in range(3, 11):
    d.line([(zx(z), B2), (zx(z), B2 + 5)], fill='black')
    d.text((zx(z) - 4, B2 + 8), str(z), fill='black')
d.text((L, T2 - 28), 'Share of each group\'s fires under today\'s rule per bin of z (width 0.25; z above 10 in the last bin)', fill='black')
for share in np.arange(0, top + 0.05, 0.05):
    yv = B2 - share / top * (B2 - T2) * 0.95
    d.line([(L - 5, yv), (L, yv)], fill='black')
    d.text((L - 40, yv - 6), f'{share:.2f}', fill='black')
for i, ((name, colour, v), h) in enumerate(zip(groups, hists)):
    if h is None:
        continue
    pts = []
    for k, share in enumerate(h):
        yv = B2 - share / top * (B2 - T2) * 0.95
        pts += [(zx(bins[k]), yv), (zx(bins[k + 1]), yv)]
    d.line(pts, fill=colour, width=2)
    d.line([(WIDTH - 420, T2 + 10 + 18 * i), (WIDTH - 390, T2 + 10 + 18 * i)], fill=colour, width=3)
    d.text((WIDTH - 382, T2 + 4 + 18 * i), f'{name} ({len(v)} fires)', fill='black')
d.text((L + 300, HEIGHT - 30), 'z = |D| / a: the drift over the assumed spread', fill='black')
img.save(sys.argv[3])
print(sys.argv[3])
