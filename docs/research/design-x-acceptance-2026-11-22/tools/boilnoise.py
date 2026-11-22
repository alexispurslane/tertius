# Tool code of t0-5tyj. boilnoise.py <label|side mp4|size log>...: the "boil" of the switch cabin in a cloud job's side
# video (cloudvideo.sh video: film "f" frames 140 to 459, one draw per frame, 1280 x 720) as a number. Per frame, three
# crops (the floor: rows 520 to 699, columns 100 to 1179; the left wall: rows 150 to 329, columns 60 to 299; the ceiling:
# rows 40 to 179, columns 300 to 979) are made display-linear and averaged into blocks of 16 x 16 px. The boil of a frame
# is the mean absolute change of the blocks from the frame before, in % of the crop's mean light. Blocks of 16 px leave
# out the per-pixel grain and keep the blotches that rise and fall. It prints per crop and span (the 20 draws before off,
# 0 to 60 and 60 to 200 draws after off, 0 to 100 draws after on) the mean boil on draws whose render size stayed the
# same, then after a slash on draws whose size changed (from the size log, draw = 33 + frame). A size change also moves
# the picture sideways (wobble.py), which adds change at edges in the crops.
import os
import subprocess
import sys
import numpy as np

FFMPEG = os.environ.get('FFMPEG', 'ffmpeg')
CROPS = {'floor': (520, 700, 100, 1180), 'left wall': (150, 330, 60, 300), 'ceiling': (40, 180, 300, 980)}
SPANS = {'before off': range(1, 20), 'off 0-60': range(21, 80), 'off 60-200': range(80, 220), 'on 0-100': range(221, 320)}


def frames(path):
    raw = subprocess.run([FFMPEG, '-loglevel', 'error', '-i', path, '-f', 'rawvideo', '-pix_fmt', 'gray', '-'],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(-1, 720, 1280).astype(float)


def blocks(a, k=16):
    a = a / 255
    a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
    h, w = a.shape
    return a[:h // k * k, :w // k * k].reshape(h // k, k, w // k, k).mean((1, 3))


for label, path, size_log in (s.split('|') for s in sys.argv[1:]):
    video = frames(path)
    sizes = [tuple(l.split()[1:3]) for l in open(size_log)]
    changed = [sizes[173 + k] != sizes[172 + k] for k in range(len(video))]
    cells = []
    for name, (y0, y1, x0, x1) in CROPS.items():
        b = [blocks(f[y0:y1, x0:x1]) for f in video]
        boil = [100 * np.mean(np.abs(b[k] - b[k - 1])) / np.mean(b[k]) for k in range(1, len(b))]
        parts = []
        for span, r in SPANS.items():
            same = [boil[k - 1] for k in r if not changed[k]]
            moved = [boil[k - 1] for k in r if changed[k]]
            parts.append(f'{span} {np.mean(same):.2f}' + (f'/{np.mean(moved):.2f}' if moved else ''))
        cells.append(f'{name}: ' + ', '.join(parts))
    print(f'{label}: ' + ' | '.join(cells))
