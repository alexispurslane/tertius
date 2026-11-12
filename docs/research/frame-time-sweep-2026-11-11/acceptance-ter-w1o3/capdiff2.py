#!/usr/bin/env python3
"""Compares the captures of two runs of each script, capture by capture.

usage: capdiff2.py <out folder> <prefix> <control suffix> <variant suffix> [--films]

Every folder <prefix><script>-<control suffix> pairs with <prefix><script>-<variant suffix>. The values are linear
light, decoded from the sRGB stills. Without --films, the frames of a film (names that end in -NNNNNN) are left out.
Prints one row per capture: the change of the mean in percent, the percent of pixels that change at all, by more than
1 level of 255 and by more than 4 levels, and the largest change in levels. Writes <out folder>/<prefix>capdiff2.json.
"""
import glob, json, os, re, sys
import numpy as np
from PIL import Image

def linear(a8):
    a = a8.astype(np.float64) / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)

def luma(a):
    return a @ np.array([0.2126, 0.7152, 0.0722])

out, prefix, ctl, var = sys.argv[1:5]
films = '--films' in sys.argv
rows = []
for folder in sorted(glob.glob(f'{out}/{prefix}*-{ctl}')):
    script = os.path.basename(folder)[len(prefix):-len(ctl) - 1]
    other = f'{out}/{prefix}{script}-{var}'
    for png in sorted(glob.glob(f'{folder}/*.png')):
        name = os.path.basename(png)[:-4]
        if re.search(r'-\d{6}$', name) and not films:
            continue
        twin = f'{other}/{name}.png'
        if not os.path.exists(twin):
            rows.append({'script': script, 'capture': name, 'missing': True})
            continue
        a8 = np.asarray(Image.open(png).convert('RGB'), dtype=np.int32)
        b8 = np.asarray(Image.open(twin).convert('RGB'), dtype=np.int32)
        a, b = luma(linear(a8)), luma(linear(b8))
        d = np.abs(a8 - b8).max(axis=2)
        rows.append({'script': script, 'capture': name, 'same': bool((d == 0).all()),
                     'mean': 0.0 if (d == 0).all() else float(b.mean() / max(a.mean(), 1e-9) - 1), 'changed': float((d > 0).mean()),
                     'over1': float((d > 1).mean()), 'over4': float((d > 4).mean()), 'max': int(d.max())})
json.dump(rows, open(f'{out}/{prefix}capdiff2.json', 'w'), indent=1)
print(f"{'script':22s} {'capture':24s}  mean %    any %   >1 lv %  >4 lv %  max")
for r in rows:
    if r.get('missing'):
        print(f"{r['script']:22s} {r['capture']:24s}  no twin"); continue
    if r['same']:
        print(f"{r['script']:22s} {r['capture']:24s}  identical"); continue
    print(f"{r['script']:22s} {r['capture']:24s} {r['mean']*100:+7.3f} {r['changed']*100:8.2f} {r['over1']*100:8.3f} {r['over4']*100:8.3f} {r['max']:4d}")
real = [r for r in rows if not r.get('missing')]
if real:
    w = max(real, key=lambda r: abs(r['mean']))
    print(f"captures {len(real)}, identical {sum(r['same'] for r in real)}, largest change of the mean {w['mean']*100:+.3f} % ({w['script']} {w['capture']}), largest share over 4 levels {max(r['over4'] for r in real)*100:.3f} %, largest share over 1 level {max(r['over1'] for r in real)*100:.3f} %")
