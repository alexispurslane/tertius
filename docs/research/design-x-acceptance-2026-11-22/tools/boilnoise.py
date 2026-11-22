# Tool code of t0-5tyj. boilnoise.py [--chart <out.png>] <label|side mp4 or PNG folder|size log>...: the "boil" of the
# switch cabin, from a cloud job's side video (cloudvideo.sh video: film "f" frames 140 to 459, one draw per frame,
# 1280 x 720), or from a folder of the Mac's PNGs of the same frames (macdyn.sh pictures, 1920 x 1080, scaled to
# 1280 x 720 with Lanczos first).
# Per frame, three crops (the floor: rows 520 to 699, columns 100 to 1179; the left wall: rows 150 to 329, columns 60 to
# 299; the ceiling: rows 40 to 179, columns 300 to 979) are made display-linear and averaged into blocks of 16 x 16 px.
# Blocks of 16 px leave out the per-pixel grain and keep the blotches that rise and fall. Two measures per draw k, both in
# % of the crop's mean light at k:
#   - change: the mean over the blocks of |b[k] - b[k - 1]|, where b[k] is the blocks' light at frame k. A room that
#     only fades smoothly also gives a change, so this is the fade plus the boil.
#   - boil: the mean over the blocks of |b[k] - (b[k - 1] + b[k + 1]) / 2|, the residual of each block against the
#     straight line through its two neighbouring draws. A fade that is linear over three draws gives zero, so this
#     leaves out the smooth fade and keeps what rises and falls from draw to draw. A fade that bends sharply (the first
#     draws after a switch) still leaves a little.
# It prints per crop and span (the 18 draws before off whose neighbours are both before off, 0 to 60 and 60 to 200 draws after off, 0 to 100 draws after on)
# the mean boil, then the mean change in brackets, each on draws whose render size stayed the same and, after a slash, on
# draws whose size changed (from the size log, draw = 33 + frame; a draw counts as changed when its size differs from
# either neighbour). A size change also moves the picture sideways (wobble.py), which adds change at edges in the crops.
# With --chart it draws each run's crop means per draw (the level), one panel per crop, off and on marked.
import os
import subprocess
import sys
import numpy as np
from PIL import Image, ImageDraw

FFMPEG = os.environ.get('FFMPEG', 'ffmpeg')
CROPS = {'floor': (520, 700, 100, 1180), 'left wall': (150, 330, 60, 300), 'ceiling': (40, 180, 300, 980)}
SPANS = {'before off': range(1, 19), 'off 0-60': range(21, 80), 'off 60-200': range(80, 219), 'on 0-100': range(221, 319)}
COLOURS = [(200, 60, 60), (40, 110, 200), (230, 150, 20), (120, 170, 60)]


def frames(path):
    if not path.endswith('.mp4'):
        return np.array([np.asarray(Image.open(f'{path}/f-{k:06d}.png').convert('L').resize((1280, 720), Image.LANCZOS))
                         for k in range(140, 460)]).astype(float)
    raw = subprocess.run([FFMPEG, '-loglevel', 'error', '-i', path, '-f', 'rawvideo', '-pix_fmt', 'gray', '-'],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(-1, 720, 1280).astype(float)


def blocks(a, k=16):
    a = a / 255
    a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
    h, w = a.shape
    return a[:h // k * k, :w // k * k].reshape(h // k, k, w // k, k).mean((1, 3))


args = sys.argv[1:]
chart = None
if args[0] == '--chart':
    chart, args = args[1], args[2:]
levels = {}
for label, path, size_log in (s.split('|') for s in args):
    video = frames(path)
    sizes = [tuple(l.split()[1:3]) for l in open(size_log)]
    step = [sizes[173 + k] != sizes[172 + k] for k in range(len(video))]
    changed = [step[k] or (k + 1 < len(step) and step[k + 1]) for k in range(len(video))]
    cells = []
    for name, (y0, y1, x0, x1) in CROPS.items():
        b = [blocks(f[y0:y1, x0:x1]) for f in video]
        levels[(label, name)] = [float(x.mean()) for x in b]
        change = {k: 100 * np.mean(np.abs(b[k] - b[k - 1])) / np.mean(b[k]) for k in range(1, len(b))}
        boil = {k: 100 * np.mean(np.abs(b[k] - (b[k - 1] + b[k + 1]) / 2)) / np.mean(b[k]) for k in range(1, len(b) - 1)}
        parts = []
        for span, r in SPANS.items():
            same = [k for k in r if not changed[k]]
            moved = [k for k in r if changed[k]]
            cell = f'{np.mean([boil[k] for k in same]):.2f} ({np.mean([change[k] for k in same]):.2f})'
            if moved:
                cell += f' / {np.mean([boil[k] for k in moved]):.2f} ({np.mean([change[k] for k in moved]):.2f})'
            parts.append(f'{span} {cell}')
        cells.append(f'{name}: ' + ', '.join(parts))
    print(f'{label}: ' + ' | '.join(cells))

if chart:
    labels = list(dict.fromkeys(label for label, _ in levels))
    W, PH, L = 1300, 300, 80
    im = Image.new('RGB', (W, PH * len(CROPS) + 40), 'white')
    d = ImageDraw.Draw(im)
    x = lambda k: L + k / 319 * (W - L - 20)
    for n, name in enumerate(CROPS):
        top = n * PH
        values = [v for label in labels for v in levels[(label, name)]]
        lo, hi = min(values) * 0.95, max(values) * 1.05
        y = lambda v: top + 26 + (hi - v) / (hi - lo) * (PH - 56)
        for v in np.linspace(lo, hi, 6):
            d.line([(L, y(v)), (W - 20, y(v))], fill=(230, 230, 230))
            d.text((8, y(v) - 6), f'{v:.4f}', fill='black')
        for k in (20, 220):
            d.line([(x(k), top + 26), (x(k), top + PH - 30)], fill=(140, 140, 140))
        for i, label in enumerate(labels):
            d.line([(x(k), y(v)) for k, v in enumerate(levels[(label, name)])], fill=COLOURS[i % 4], width=2)
            d.text((W - 300, top + 30 + 16 * i), label, fill=COLOURS[i % 4])
        d.text((L, top + 6), f'{name}: crop mean light per draw (display-linear). Grey lines: lamps off (frame 160), on (360).',
               fill='black')
    d.text((L, PH * len(CROPS) + 14), 'film frame 140 to 459', fill='black')
    im.save(chart)
    print(chart, im.size)
