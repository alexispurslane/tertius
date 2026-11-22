# Tool code of t0-5tyj, copied from b53c037f1 into t0-gbgj's record with one change: frames() also reads a PNG
# folder (see there). wobble.py <chart.png> <label|side mp4|size log>...: the horizontal wobble of the switch cabin's
# picture under the dynamic render scale, from a cloud job's side video (cloudvideo.sh video: film "f" frames 140 to 459,
# one draw per frame, 1280 x 720 from the 1920 x 1080 output) and the size log of its run (one line per draw,
# "<frame> <render width> <render height> <scale>"; film frame k is draw 33 + k). The camera stands still in this
# film, so a fixed edge of the scene should stay at one place in the output.
# Per frame, it finds four edges with sub-pixel precision (the peak of the brightness gradient, refined by a parabola
# through the peak and its two neighbours, searched within 6 px of the last frame's place):
#   - horizontal positions on rows 350 to 369: the left edge of the window opening on the left wall (near x 246) and the
#     right edge of the window opening on the right wall (near x 1033);
#   - vertical positions on columns 630 to 649: the top edge of the near ceiling lamp (near y 203) and the bottom edge of
#     the back wall's window (near y 375).
# It converts them to output pixels (x 1.5) and sets the horizontal distance between the two windows' edges, and the
# vertical distance between the two vertical edges, against the render size of the draw. The hypothesis to test: the
# camera's projection takes its aspect from the render size, which RenderExtent rounds down to a multiple of 8 in each
# axis on its own, and the upscaler stretches that picture to the output's 16:9. Then the horizontal distance is
# D0 * (h / w) * (1920 / 1080), where w x h is the render size and D0 the distance at the exact aspect, and the vertical
# distance does not change. The fit gives D0 by least squares. It prints, per side: the range of both distances, the
# root mean square of the horizontal distance around its mean and around the prediction, and the correlation of the
# horizontal distance with the render width, the render height and the predicted ratio, and a noise floor: the change
# of the horizontal distance from one draw to the next where the render size stays the same. The chart: per side, the
# horizontal distance and its prediction per film frame.
import os
import subprocess
import sys
import numpy as np
from PIL import Image, ImageDraw

FFMPEG = os.environ.get('FFMPEG', 'ffmpeg')
out, specs = sys.argv[1], [s.split('|') for s in sys.argv[2:]]
FIRST, STILL, SCALE = 140, 33, 1.5


def frames(path):
    # t0-gbgj's change: a folder of the Mac's PNGs of film frames 140 to 459 (1920 x 1080), scaled to 1280 x 720 with
    # Lanczos first, as boilnoise.py reads them.
    if not path.endswith('.mp4'):
        return np.array([np.asarray(Image.open(f'{path}/f-{k:06d}.png').convert('L').resize((1280, 720), Image.LANCZOS))
                         for k in range(140, 460)]).astype(float)
    raw = subprocess.run([FFMPEG, '-loglevel', 'error', '-i', path, '-f', 'rawvideo', '-pix_fmt', 'gray', '-'],
                         check=True, capture_output=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(-1, 720, 1280).astype(float)


def edge(profile, near):
    g = np.abs(np.diff(profile))
    lo = int(round(near)) - 6
    i = lo + int(np.argmax(g[lo:lo + 13]))
    a, b, c = g[i - 1], g[i], g[i + 1]
    shift = 0.5 * (a - c) / (a - 2 * b + c) if a - 2 * b + c != 0 else 0.0
    return i + 0.5 + shift  # the gradient g[i] sits between pixels i and i + 1


results = []
for label, path, size_log in specs:
    video = frames(path)
    sizes = [tuple(map(int, l.split()[1:3])) for l in open(size_log)]
    places = [246.0, 1033.0, 203.0, 375.0]
    rows = []
    for k, f in enumerate(video):
        row = f[350:370].mean(0)
        col = f[:, 630:650].mean(1)
        places = [edge(row, places[0]), edge(row, places[1]), edge(col, places[2]), edge(col, places[3])]
        w, h = sizes[STILL + FIRST + k]
        rows.append((SCALE * (places[1] - places[0]), SCALE * (places[3] - places[2]), w, h))
    a = np.array(rows)
    dx, dy, w, h = a.T
    ratio = h / w * 1920 / 1080
    d0 = float(np.dot(dx, ratio) / np.dot(ratio, ratio))
    pred = d0 * ratio
    corr = lambda u, v: float(np.corrcoef(u, v)[0, 1]) if np.std(v) > 0 else float('nan')
    print(f'{label}: horizontal distance {dx.min():.2f} to {dx.max():.2f} output px (range {dx.max() - dx.min():.2f}), '
          f'vertical distance {dy.min():.2f} to {dy.max():.2f} (range {dy.max() - dy.min():.2f})')
    print(f'  rms of the horizontal distance around its mean {np.std(dx):.3f} px, around the prediction '
          f'{np.sqrt(np.mean((dx - pred) ** 2)):.3f} px (D0 = {d0:.2f} px); predicted range {pred.min():.2f} to {pred.max():.2f}')
    print(f'  correlation with render width {corr(dx, w):+.3f}, render height {corr(dx, h):+.3f}, '
          f'(h / w) * 16/9 {corr(dx, ratio):+.3f}; render h/w * 16/9 from {ratio.min():.4f} to {ratio.max():.4f}')
    same = [(w[k], h[k]) == (w[k - 1], h[k - 1]) for k in range(1, len(w))]
    noise = np.sqrt(np.mean([((dx[k] - dx[k - 1]) ** 2) / 2 for k in range(1, len(w)) if same[k - 1]]))
    print(f'  noise floor: rms of the frame-to-frame change of the horizontal distance between two draws of one size, '
          f'over sqrt 2: {noise:.3f} px ({sum(same)} pairs); vertical distance against (h / w) * 16/9: '
          f'correlation {corr(dy, ratio):+.3f}, rms around its mean {np.std(dy):.3f} px')
    results.append((label, dx, pred))

W, PH, L = 1300, 260, 70
im = Image.new('RGB', (W, PH * len(results) + 40), 'white')
d = ImageDraw.Draw(im)
for n, (label, dx, pred) in enumerate(results):
    top = n * PH
    lo, hi = min(dx.min(), pred.min()) - 0.5, max(dx.max(), pred.max()) + 0.5
    x = lambda k: L + k / (len(dx) - 1) * (W - L - 20)
    y = lambda v: top + 30 + (hi - v) / (hi - lo) * (PH - 60)
    for v in np.arange(np.ceil(lo), hi, 1.0):
        d.line([(L, y(v)), (W - 20, y(v))], fill=(230, 230, 230))
        d.text((8, y(v) - 6), f'{v:.0f}', fill='black')
    for k in (20, 220):
        d.line([(x(k), top + 30), (x(k), top + PH - 30)], fill=(160, 160, 160))
    d.line([(x(k), y(v)) for k, v in enumerate(pred)], fill=(40, 110, 200), width=2)
    d.line([(x(k), y(v)) for k, v in enumerate(dx)], fill=(200, 60, 60), width=2)
    d.text((L, top + 8), f'{label}: distance between two window edges in output px (red, measured) and its prediction from '
                         f'the render size (blue, D0 * h/w * 16/9). Grey lines: lamps off, lamps on.', fill='black')
d.text((L, PH * len(results) + 12), 'film frame 140 to 459 (draw = 33 + frame)', fill='black')
im.save(out)
print(out, im.size)
