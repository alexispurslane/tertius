#!/usr/bin/env python3
"""Tool code (t2b-xeo7). point.py <walk folder> <turn folder> <out.png>: one floor point of the room, P = (1.95, 0, -1)
in the gap behind the block's +X side, lit by bounce alone, followed through both films. Each frame's pose comes from
the film's path (film-probe.patch: LitBlockWalk, LitBlockTurn) at the step end of draw k, t = (k + 2) / 60, and the
default 60 degree vertical field of view on 1280 x 720. Where P is in the picture and the block does not hide it, the
display-linear mean of the 5 x 5 pixels around its pixel; otherwise nothing. Prints the level by phase, and draws it.
"""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

P = np.array([1.95, 0.0, -1.0])
CENTER = np.array([0, 0.8, 0])
BLOCK = (np.array([-1.2, 0, -0.75]), np.array([1.2, 1.6, 0.75]))
W, H, TAN_Y = 1280, 720, math.tan(math.pi / 6)


def phase(t):
    ease = lambda x: 0 if x <= 0 else 1 if x >= 1 else x * x * (3 - 2 * x)
    return ease((t - 4) / 3) if t < 9 else 1 - ease((t - 9) / 3)


def walk(t):
    a = math.radians(30 - 210 * phase(t))
    return np.array([-1 + 2.6 * math.sin(a), 1.5, 0.25 + 3.7 * math.cos(a)]), CENTER


def turn(t):
    u = phase(t)
    return np.array([0.3, 1.5, 3.45]), CENTER * (1 - u) + np.array([1.6, 0.8, -2.5]) * u


def hidden_by_block(eye, point):
    d = point - eye
    lo, hi = 0.0, 1.0
    for i in range(3):
        if abs(d[i]) < 1e-12:
            if not BLOCK[0][i] <= eye[i] <= BLOCK[1][i]:
                return False
            continue
        a, b = (BLOCK[0][i] - eye[i]) / d[i], (BLOCK[1][i] - eye[i]) / d[i]
        lo, hi = max(lo, min(a, b)), min(hi, max(a, b))
    return lo < hi


def pixel(eye, target):
    f = (target - eye) / np.linalg.norm(target - eye)
    r = np.cross(f, [0, 1, 0]); r /= np.linalg.norm(r)
    u = np.cross(r, f)
    v = P - eye
    z = v @ f
    if z <= 0:
        return None
    x = W / 2 + (v @ r) / z / (TAN_Y * W / H) * W / 2
    y = H / 2 - (v @ u) / z / TAN_Y * H / 2
    return (x, y) if 3 <= x < W - 3 and 3 <= y < H - 3 else None


def luminance(rgb):
    a = rgb.astype(float) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]


traces = {}
for name, folder, path in (('walk', Path(sys.argv[1]), walk), ('turn', Path(sys.argv[2]), turn)):
    trace = []
    for k in range(840):
        eye, target = path((k + 2) / 60)
        at = None if hidden_by_block(eye, P) else pixel(eye, target)
        if at is None:
            trace.append(np.nan)
            continue
        x, y = int(at[0]), int(at[1])
        img = np.asarray(Image.open(folder / 'frames' / f'f-{k:06d}.png').convert('RGB'))
        trace.append(float(luminance(img[y - 2:y + 3, x - 2:x + 3]).mean()))
    traces[name] = np.array(trace)
    t = traces[name]
    for label, a, b in (('rest before', 180, 240), ('hold', 420, 540), ('rest after', 780, 840)):
        seg = t[a:b]
        print(f'{name} {label} (frames {a}-{b - 1}): P seen in {np.isfinite(seg).sum()} frames, mean {np.nanmean(seg) if np.isfinite(seg).any() else float("nan"):.4f}')
    seen = np.isfinite(t)
    print(f'{name}: P seen in {seen.sum()} of 840 frames')

img = Image.new('RGB', (1200, 360), 'white')
d = ImageDraw.Draw(img)
top = max(np.nanmax(v) for v in traces.values()) * 1.1
for (name, t), color in zip(traces.items(), ((230, 90, 60), (60, 120, 230))):
    pts = [(40 + 1120 * k / 840, 320 - 280 * v / top) for k, v in enumerate(t) if np.isfinite(v)]
    for p0, p1 in zip(pts, pts[1:]):
        if p1[0] - p0[0] < 3:
            d.line((p0, p1), fill=color, width=2)
d.text((44, 10), f'floor point P = (1.95, 0, -1): display-linear level per frame; walk red, turn blue; top {top:.3f}', fill=(0, 0, 0))
for k, name in ((0, 'rest'), (240, 'move out'), (420, 'hold'), (540, 'move back'), (720, 'rest')):
    x = 40 + 1120 * k / 840
    d.line((x, 40, x, 320), fill=(220, 220, 220)); d.text((x + 2, 326), name, fill=(120, 120, 120))
img.save(sys.argv[3])

# After the return (draw 720 on), the first draw from which P stays within 5 % of its rest level before the walk.
w = traces['walk']
rest = np.nanmean(w[180:240])
after = [k for k in range(720, 840) if np.isfinite(w[k])]
print('walk after the return:', ', '.join(f'{k}: {w[k]:.4f}' for k in after[:1] + after[10:11] + after[20:21] + after[40:41] + after[60:61]))
steady = next((k for k in after if all(abs(w[j] / rest - 1) < 0.05 for j in after if j >= k)), None)
print(f'walk: P within 5 % of its rest level ({rest:.4f}) from draw {steady} on')
