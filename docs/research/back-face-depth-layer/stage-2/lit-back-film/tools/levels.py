#!/usr/bin/env python3
"""Tool code (t2b-xeo7). levels.py <walk folder> <turn folder> <out prefix>: the room's level per frame in three screen
crops of each film (display-linear mean luminance at the film's fixed exposure), the chart <out prefix>-levels.png, the
table <out prefix>-levels.txt, and the side-by-side frames in <out prefix>-sbs/ for the video (walk left, turn right).

The crops are boxes of the 1280 x 720 frame, so in the walk they see other surfaces as the eye moves. Only the rest
before the move (frames 180 to 239) and the rest after the return (frames 780 to 839) see the same surfaces from the same
pose; the table compares them.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

CROPS = {'top': (440, 40, 840, 160), 'floor': (440, 600, 840, 700), 'right': (1000, 300, 1200, 500)}
COLORS = {'top': (230, 90, 60), 'floor': (60, 170, 230), 'right': (90, 200, 90)}
PHASES = [(0, 'rest'), (240, 'move out'), (420, 'hold'), (540, 'move back'), (720, 'rest')]
walk, turn, prefix = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]


def luminance(rgb):
    a = rgb.astype(float) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]


def levels(folder):
    frames = sorted((folder / 'frames').glob('f-*.png'))
    out = {c: [] for c in CROPS}
    for f in frames:
        y = luminance(np.asarray(Image.open(f).convert('RGB')))
        for c, (x0, y0, x1, y1) in CROPS.items():
            out[c].append(float(y[y0:y1, x0:x1].mean()))
    return frames, {c: np.array(v) for c, v in out.items()}


def phase(k):
    return [name for start, name in PHASES if k >= start][-1]


sides = {'walk': levels(walk), 'turn': levels(turn)}
lines = []
for side, (frames, lv) in sides.items():
    lines.append(f'## {side}: {len(frames)} frames')
    for c, v in lv.items():
        before, after = v[180:240].mean(), v[780:840].mean()
        first = v[780] if len(v) > 780 else float('nan')
        lines.append(f'{c}: rest before {before:.4f}, rest after {after:.4f} ({after / before - 1:+.1%}), frame 780 {first:.4f}, '
                     f'hold mean {v[420:540].mean():.4f}, min {v.min():.4f} at {int(v.argmin())}, max {v.max():.4f} at {int(v.argmax())}')
    # how many draws after the return (frame 720) each crop takes to come within 1 % of its rest-before level
    for c, v in lv.items():
        before = v[180:240].mean()
        settled = next((k for k in range(720, len(v)) if all(abs(v[j] / before - 1) < 0.01 for j in range(k, len(v)))), None)
        lines.append(f'{c}: within 1 % of the rest before from frame {settled} on (the return ends at frame 720)')
Path(f'{prefix}-levels.txt').write_text('\n'.join(lines) + '\n')
print('\n'.join(lines))

# chart: both films, the three crops, against the frame, with the phases marked
W, H, pad = 1200, 520, 40
chart = Image.new('RGB', (W, H), 'white')
d = ImageDraw.Draw(chart)
top = max(max(v.max() for v in lv.values()) for _, lv in sides.values()) * 1.1
for i, (side, (_, lv)) in enumerate(sides.items()):
    x0 = pad + i * (W // 2); w = W // 2 - 2 * pad; h = H - 2 * pad
    d.rectangle((x0, pad, x0 + w, pad + h), outline=(180, 180, 180))
    d.text((x0, 10), f'{side}: display-linear mean of each crop per frame (60 a second)', fill=(0, 0, 0))
    for start, name in PHASES:
        x = x0 + w * start / 840
        d.line((x, pad, x, pad + h), fill=(220, 220, 220))
        d.text((x + 2, pad + h + 4), name, fill=(120, 120, 120))
    for c, v in lv.items():
        pts = [(x0 + w * k / 840, pad + h - h * v[k] / top) for k in range(len(v))]
        d.line(pts, fill=COLORS[c], width=2)
    for j, c in enumerate(CROPS):
        d.text((x0 + 8, pad + 8 + 14 * j), c, fill=COLORS[c])
    d.text((x0 - 34, pad), f'{top:.3f}', fill=(0, 0, 0))
    d.text((x0 - 20, pad + h - 10), '0', fill=(0, 0, 0))
chart.save(f'{prefix}-levels.png')

# side-by-side frames with labels and the crops' boxes
sbs = Path(f'{prefix}-sbs'); sbs.mkdir(exist_ok=True)
for k in range(min(len(sides['walk'][0]), len(sides['turn'][0]))):
    pair = Image.new('RGB', (2560 + 8, 720), 'black')
    for i, side in enumerate(('walk', 'turn')):
        img = Image.open(sides[side][0][k]).convert('RGB')
        dr = ImageDraw.Draw(img)
        for c, box in CROPS.items():
            dr.rectangle(box, outline=COLORS[c])
        label = (f'{"eye walks round the block" if side == "walk" else "eye at rest, camera turns"}   lit-block-back, '
                 f'render scale 2/3, 6.79 stops fixed   draw {k}   {phase(k)}')
        dr.rectangle((0, 0, 8 + 6 * len(label), 18), fill=(0, 0, 0))
        dr.text((4, 4), label, fill=(255, 210, 90))
        pair.paste(img, (i * (1280 + 8), 0))
    pair.save(sbs / f'{k:04d}.png')
print('frames written', k + 1)
