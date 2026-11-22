#!/usr/bin/env python3
"""Tool code (t1-gout). gapchart.py <ramp table> <out.png>: each render size of the ramp (t0-gbgj's ramp-M3.txt) as a
point, its right jamb's light against the gap that phase.py's model gives at its aspect: how far the map's edge lies inside
the hull's outer edge at x = 10 m, in texels of cascade 0. The color is the render width, which is also the ramp's tick.
"""
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw

exec(open(Path(__file__).parent / 'phase.py').read().split("print('far'")[0])
rows = []
for line in open(sys.argv[1]):
    p = line.split()
    if len(p) >= 5 and p[0].isdigit() and p[1].isdigit():
        w, h, right = int(p[0]), int(p[1]), float(p[3])
        q = 10 / texel(w / h)
        rows.append((q - (math.floor(q - 0.5) + 1), right, w))
W, H, L, B = 900, 520, 70, 60
img = Image.new('RGB', (W, H), 'white')
d = ImageDraw.Draw(img)
gx = lambda g: L + (g + 0.5) * (W - L - 30)
gy = lambda v: H - B - v / 0.25 * (H - B - 40)
d.rectangle((L, 40, W - 30, H - B), outline=(180, 180, 180))
for g in (-0.5, -0.25, 0, 0.25, 0.5):
    d.line((gx(g), 40, gx(g), H - B), fill=(235, 235, 235)); d.text((gx(g) - 12, H - B + 6), f'{g:+.2f}', fill=(0, 0, 0))
for v in (0, 0.05, 0.08, 0.1, 0.15, 0.2, 0.25):
    d.line((L, gy(v), W - 30, gy(v)), fill=(120, 120, 120) if v == 0.08 else (235, 235, 235))
    d.text((L - 40, gy(v) - 6), f'{v:.2f}', fill=(0, 0, 0))
for g, right, w in rows:
    t = (w - 960) / 960
    c = (int(40 + 200 * t), int(120 - 60 * t), int(220 - 180 * t))
    x, y = gx(g), gy(min(right, 0.25))
    d.ellipse((x - 3, y - 3, x + 3, y + 3), fill=c)
d.text((L, 10), 'ramp M3, 181 render sizes: right jamb (display-linear mean) against the model\'s gap at x = 10 m', fill=(0, 0, 0))
d.text((L, 24), 'gap > 0: the map\'s edge lies inside the hull\'s edge by that many texels; the darker line at 0.08 is the leak threshold',
       fill=(0, 0, 0))
d.text((L, H - 24), 'gap (texels of cascade 0)      color: render width 960 (blue) to 1920 (orange), which is also a later tick',
       fill=(0, 0, 0))
img.save(sys.argv[2])
print(len(rows), 'sizes')
