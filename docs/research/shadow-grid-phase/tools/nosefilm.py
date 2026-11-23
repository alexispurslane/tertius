#!/usr/bin/env python3
"""Tool code (t1-gout). nosefilm.py prepare <folder> | measure <folder> <out.png>: the 16:9 check at the nose window.
prepare writes a script: scene orbit, camera person, a 1920 x 1080 window, a film of 360 draws from the start, one tick
each. measure takes the display-linear mean of the crop round the nose window (x 880 to 1040, y 505 to 575) per draw,
beside the gap of the roof's front edge that nose.py's model gives for that tick, and draws both.
"""
import json
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

BOX = (880, 505, 1040, 575)
mode, folder = sys.argv[1], Path(sys.argv[2])
if mode == 'prepare':
    folder.mkdir(parents=True, exist_ok=True)
    json.dump({'scene': 'orbit', 'window': {'width': 1920, 'height': 1080},
               'steps': [{'do': 'camera', 'entity': 'person'}, {'do': 'film', 'name': 'f', 'frames': 360, 'every': 1}]},
              open(folder / 'script.json', 'w'), indent=1)
    sys.exit()

texel = 0.05846642702817917  # cascade 0 at 16:9 (phase.py)
n = math.sqrt(3.986004418e14 / (6.371e6 + 400e3) ** 3)


def strip(tick):
    """How far the map's edge lies past the true shadow's start on the sill at z = -30, in m (positive leaks)."""
    v = n * tick / 60
    s, c = math.sin(v), math.cos(v)
    q = (-4 * s - 30 * c) / texel
    gap = math.floor(q - 0.5) + 1 - q
    return gap * texel - 6.85 * s


def luminance(rgb):
    a = rgb.astype(float) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]


x0, y0, x1, y1 = BOX
levels = []
for k in range(360):
    y = luminance(np.asarray(Image.open(folder / 'frames' / f'f-{k:06d}.png').convert('RGB')))
    levels.append(float(y[y0:y1, x0:x1].mean()))
levels = np.array(levels)
# film frame k shows the state after k + 1 ticks
strips = np.array([strip(k + 1) for k in range(360)])
for a, b in ((0, 60), (60, 120), (120, 240), (240, 360)):
    print(f'draws {a}-{b - 1}: crop mean {levels[a:b].mean():.4f} (min {levels[a:b].min():.4f}, max {levels[a:b].max():.4f}); '
          f'model strip {strips[a:b].min() * 1000:+.1f} to {strips[a:b].max() * 1000:+.1f} mm')
print(f'draws 120-359: crop changes by at most {(levels[120:].max() / levels[120:].min() - 1):.2%}')
img = Image.new('RGB', (1200, 400), 'white')
d = ImageDraw.Draw(img)
top = levels.max() * 1.1
d.line([(40 + 1120 * k / 360, 180 - 160 * levels[k] / top) for k in range(360)], fill=(60, 120, 230), width=2)
d.line([(40 + 1120 * k / 360, 300 - 1e4 * strips[k]) for k in range(360)], fill=(230, 90, 60), width=2)
d.line((40, 300, 1160, 300), fill=(200, 200, 200))
d.text((44, 6), f'blue: nose window crop, display-linear mean per draw (top {top:.3f}); red: model strip at the nose sill, '
                f'1 px = 0.1 mm, grey line = 0', fill=(0, 0, 0))
img.save(sys.argv[3])
