# Tool code of t0-5tyj. panecheck.py <video.mp4> [<reference.mp4> <pane:reference pane>...]: the checks of a video of
# the switch cabin made of 1280 x 720 panes side by side (sidevideo.py's frames 140 to 459, one draw per frame), per pane:
#   - the fade: the 3 x 3 regions' display-linear luminance against the pane's last frame, at 0 and 60 draws after off
#     and 20 draws after on (video frames 20, 80 and 240), as r11 and as the largest of the six lower regions (the labels
#     cover the top row);
#   - the boil: sidevideo.py's residual per 16 x 16 block, |b[k] - (b[k - 1] + b[k + 1]) / 2| as a share of the crop's
#     light, in the floor, the left wall and the ceiling's rows 100 to 179 (under the labels), in the spans of
#     boilnoise.py;
#   - with a reference video, for each pair "pane:reference pane" (counted from 1), the difference between the two panes
#     per frame: the largest change of a region's light in %, and the 99th percentile of the per-pixel difference in
#     8-bit steps. It shows what a second encode of a pane changed.
# FFMPEG names the ffmpeg binary.
import os
import subprocess
import sys
import numpy as np

FFMPEG = os.environ.get('FFMPEG', 'ffmpeg')
W, H = 1280, 720
CROPS = {'floor': (520, 700, 100, 1180), 'left wall': (150, 330, 60, 300), 'ceiling': (100, 180, 300, 980)}
SPANS = {'before off': range(1, 19), 'off 0-60': range(21, 80), 'off 60-200': range(80, 219), 'on 0-100': range(221, 319)}
CHECKS = {'off, draw 0': 20, 'off, draw 60': 80, 'on, draw 20': 240}


def decode(path):
    probe = subprocess.run([FFMPEG, '-i', path], capture_output=True, text=True).stderr
    import re
    w, h = map(int, re.search(r'Video:.*?\b(\d{3,5})x(\d{3,5})\b', probe).groups())
    raw = subprocess.run([FFMPEG, '-loglevel', 'error', '-i', path, '-vf', 'scale=in_color_matrix=bt709:in_range=tv',
                          '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], check=True, capture_output=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(-1, h, w, 3)


def linear(rgb):
    a = rgb.astype(float) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]


def regions(y):
    return np.array([y[r * H // 3:(r + 1) * H // 3, c * W // 3:(c + 1) * W // 3].mean() for r in range(3) for c in range(3)])


def blocks(a, n=16):
    h, w = a.shape
    return a[:h // n * n, :w // n * n].reshape(h // n, n, w // n, n).mean((1, 3))


def pane(video, k):
    return video[:, :, (k - 1) * W:k * W]


video = decode(sys.argv[1])
panes = video.shape[2] // W
for k in range(1, panes + 1):
    frames = [linear(f) for f in pane(video, k)]
    last = regions(frames[-1])
    fade = []
    for name, i in CHECKS.items():
        r = regions(frames[i]) / last - 1
        fade.append(f'{name} r11 {100 * r[4]:+.2f} %')
    cells = []
    for name, (y0, y1, x0, x1) in CROPS.items():
        b = [blocks(f[y0:y1, x0:x1]) for f in frames]
        res = {i: 100 * np.mean(np.abs(b[i] - (b[i - 1] + b[i + 1]) / 2)) / np.mean(b[i]) for i in range(1, len(b) - 1)}
        cells.append(f'{name} ' + ', '.join(f'{span} {np.mean([res[i] for i in r]):.2f}' for span, r in SPANS.items()))
    print(f'pane {k}: fade against the last frame: ' + '; '.join(fade))
    print(f'pane {k}: boil (%): ' + ' | '.join(cells))
if len(sys.argv) > 2:
    reference = decode(sys.argv[2])
    for pair in sys.argv[3:]:
        a, b = map(int, pair.split(':'))
        mine, theirs = pane(video, a), pane(reference, b)
        region_change = max(np.max(np.abs(regions(linear(x))[3:] / regions(linear(y))[3:] - 1)) for x, y in zip(mine, theirs))
        p99 = max(np.percentile(np.abs(x.astype(int) - y.astype(int)), 99) for x, y in zip(mine[::8], theirs[::8]))
        print(f'pane {a} against the reference pane {b}: largest region change {100 * region_change:.2f} %, '
              f'99th percentile of the per-pixel difference up to {p99:.0f} of 255 (every 8th frame)')
