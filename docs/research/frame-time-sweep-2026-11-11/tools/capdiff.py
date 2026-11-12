#!/usr/bin/env python3
"""Compares the captures of a run at a lower step cap with the control run's, capture by capture.

Usage: capdiff.py <out folder> <scene>... A scene has the run folders cap-<scene>-ctl, -r128, -r96 and -r64. The
values are linear light, decoded from the sRGB stills. A pixel counts for the shares when its control value is above
1 % of full range in linear light, so that the dark pixels' rounding does not count.
"""
import sys, glob, os, json
import numpy as np
from PIL import Image

def linear(path):
    a = np.asarray(Image.open(path).convert('RGB'), dtype=np.float64) / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)

def luma(a):
    return a @ np.array([0.2126, 0.7152, 0.0722])

out = sys.argv[1]
rows = []
for scene in sys.argv[2:]:
    ctl = f'{out}/cap-{scene}-ctl'
    for png in sorted(glob.glob(f'{ctl}/*.png')):
        name = os.path.basename(png)
        ref8 = np.asarray(Image.open(png).convert('RGB'), dtype=np.int32)
        ref = luma(linear(png))
        lit = ref > 0.01
        row = {'scene': scene, 'capture': name[:-4], 'lit': float(lit.mean())}
        for cap in ('r128', 'r96', 'r64'):
            other = f'{out}/cap-{scene}-{cap}/{name}'
            if not os.path.exists(other):
                row[cap] = None
                continue
            var8 = np.asarray(Image.open(other).convert('RGB'), dtype=np.int32)
            var = luma(linear(other))
            rel = np.abs(var - ref)[lit] / ref[lit] if lit.any() else np.zeros(1)
            row[cap] = {
                'same': bool((var8 == ref8).all()),
                'changed': float((var8 != ref8).any(axis=2).mean()),
                'mean': float(var.mean() / max(ref.mean(), 1e-9) - 1),
                'over2': float((rel > 0.02).mean() * lit.mean()),
                'over5': float((rel > 0.05).mean() * lit.mean()),
                'over20': float((rel > 0.20).mean() * lit.mean()),
                'max8': int(np.abs(var8 - ref8).max()),
                'over1level': float((np.abs(var8 - ref8).max(axis=2) > 1).mean()),
                'over4levels': float((np.abs(var8 - ref8).max(axis=2) > 4).mean()),
            }
        rows.append(row)
json.dump(rows, open(f'{out}/capdiff.json', 'w'), indent=1)
print('per cap: the change of the mean in percent, then the percent of pixels that change at all, by more than 1 level of 255 and by more than 4 levels, then the largest change in levels')
print(f"{'scene':20s} {'capture':22s} " + ' | '.join(f'{c:>5s}:  mean %   any   >1 lv  >4 lv  max' for c in ('r128', 'r96', 'r64')))
for r in rows:
    cells = []
    for cap in ('r128', 'r96', 'r64'):
        v = r[cap]
        if v is None: cells.append(' ' * 41 + '-'); continue
        if v['same']: cells.append(f"{'identical':>42s}"); continue
        cells.append(f"{v['mean']*100:+15.3f} {v['changed']*100:6.2f} {v['over1level']*100:6.3f} {v['over4levels']*100:6.3f} {v['max8']:4d}")
    print(f"{r['scene']:20s} {r['capture']:22s} " + ' | '.join(cells))
