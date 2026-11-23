# Tool code of t0-5tyj (ticket t4f-71qp). sidevideo.py <out.mp4> <walk|switch> <side>...: a side-by-side video of the
# walk cabin-walk's stop or the switch cabin, one pane per side from left to right (such as M left and X right), under
# the dynamic render scale or a fixed one. PANE=<width>x<height> scales each pane to that size (Lanczos) before the labels
# are drawn; without it each pane keeps its source size. Each side is "label|source|size log":
#   - source: a folder of the run's PNGs (the Mac's runs), or a cloud job's mp4 of that side (cloudvideo.py), whose
#     frames it decodes and whose <name>-means.json holds the full-size source frames' region means;
#   - size log: the scratch patch's size log of the same run, "<frame number> <width> <height> <scale>" per draw.
# The frames: the walk's last 60 draws and the 200 draws after the stop (films "g" 60 to 119 and "h" 0 to 199), or the
# switch film's frames 140 to 459 (the lamps go off at frame 160 and come on at 360). A film frame's draw is its index in
# the size log: the still's 33 draws come first, then the films in order (the walk: "f" 180, "g" 120, "h" 200).
# Under each label: the render scale of that draw and its size, in orange on a draw whose render size differs from the
# draw before. In the corner: the draws since the stop, or since the last switch with "off" or "on" (negative before the
# lamps go off). One draw per video frame at 60 frames a second, H.264 yuv420p at CRF 12 or CRF=<value> (BT.709). Each side keeps its
# source size (1920 x 1080 from the Mac, the job's size from the cloud). FFMPEG names the ffmpeg binary.
# The check decodes three frames back from the file and compares each frame's fade (its 3 x 3 regions' display-linear
# luminance over that of the last frame) with the source's, and saves those frames as PNGs into CHECK_DIR if it is set.
import json
import os
import re
import subprocess
import sys
import tempfile
import numpy as np
from PIL import Image, ImageDraw, ImageFont

out, scene, *sides = sys.argv[1:]
FFMPEG = os.environ.get('FFMPEG', 'ffmpeg')
STILL = 33
if scene == 'walk':
    names = [f'g-{k:06d}' for k in range(60, 120)] + [f'h-{k:06d}' for k in range(200)]
    draws = [STILL + 180 + k for k in range(60, 120)] + [STILL + 300 + k for k in range(200)]
    counters = [f'draws after the stop: {k - 120:+d}' for k in range(60, 120)] + [f'draws after the stop: {k}' for k in range(200)]
    checks = (60, 80, 100)
else:
    names = [f'f-{k:06d}' for k in range(140, 460)]
    draws = [STILL + k for k in range(140, 460)]
    counters = [f'lamps on, {k - 160:+d} draws to off' if k < 160 else f'lamps off, draw {k - 160}' if k < 360
                else f'lamps on again, draw {k - 360}' for k in range(140, 460)]
    checks = (20, 80, 240)
try:
    FONT = ImageFont.load_default(size=28)
except TypeError:
    FONT = ImageFont.load_default()


def linear_regions(a):
    a = a.astype(float) / 255
    a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]
    h, w = a.shape
    return np.array([a[r * h // 3:(r + 1) * h // 3, c * w // 3:(c + 1) * w // 3].mean() for r in range(3) for c in range(3)])


def decode_all(path):
    probe = subprocess.run([FFMPEG, '-i', path], capture_output=True, text=True).stderr
    w, h = map(int, re.search(r'Video:.*?\b(\d{3,5})x(\d{3,5})\b', probe).groups())
    raw = subprocess.run([FFMPEG, '-loglevel', 'error', '-i', path, '-vf', 'scale=in_color_matrix=bt709:in_range=tv',
                          '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], check=True, capture_output=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(-1, h, w, 3)


class Side:
    def __init__(self, spec):
        self.label, self.source, size_log = spec.split('|')
        lines = [l.split() for l in open(size_log)]
        self.sizes = [(int(l[1]), int(l[2])) for l in lines]
        self.scales = [float(l[3]) for l in lines]
        if self.source.endswith('.mp4'):
            self.video = decode_all(self.source)
            self.means = np.array(json.load(open(self.source[:-4] + '-means.json'))['means'])
            self.h, self.w = self.video.shape[1:3]
        else:
            self.video = None
            self.means = None
            self.w, self.h = Image.open(f'{self.source}/{names[0]}.png').size

    def frame(self, i):
        if self.video is not None:
            return Image.fromarray(self.video[i])
        return Image.open(f'{self.source}/{names[i]}.png').convert('RGB')

    def source_regions(self, i):
        return self.means[i] if self.means is not None else linear_regions(np.asarray(self.frame(i)))


def text(draw, x, y, words, colour=(255, 255, 255)):
    box = draw.textbbox((x, y), words, font=FONT)
    draw.rectangle((box[0] - 8, box[1] - 6, box[2] + 8, box[3] + 6), fill=(0, 0, 0))
    draw.text((x, y), words, fill=colour, font=FONT)


panes = [Side(spec) for spec in sides]
assert len({(side.w, side.h) for side in panes}) == 1, 'all sides must have one size'
W, H = map(int, os.environ['PANE'].split('x')) if os.environ.get('PANE') else (panes[0].w, panes[0].h)
N = len(panes)
with tempfile.TemporaryDirectory(dir=os.path.dirname(os.path.abspath(out))) as work:
    for i, d in enumerate(draws):
        frame = Image.new('RGB', (N * W, H))
        g = ImageDraw.Draw(frame)
        for k, side in enumerate(panes):
            image = side.frame(i)
            frame.paste(image if image.size == (W, H) else image.resize((W, H), Image.LANCZOS), (k * W, 0))
            text(g, k * W + 16, 14, side.label)
            changed = d > 0 and side.sizes[d] != side.sizes[d - 1]
            w, h = side.sizes[d]
            text(g, k * W + 16, 60, f'render scale {side.scales[d]:.3f} ({w} x {h}){"  changed" if changed else ""}',
                 (255, 160, 40) if changed else (255, 255, 255))
        box = g.textbbox((0, 0), counters[i], font=FONT)
        text(g, N * W - (box[2] - box[0]) - 32, 14, counters[i])
        frame.save(f'{work}/c-{i:04d}.png', compress_level=1)
    subprocess.run([FFMPEG, '-y', '-loglevel', 'error', '-framerate', '60', '-i', f'{work}/c-%04d.png', '-c:v', 'libx264',
                    '-preset', 'slow', '-crf', os.environ.get('CRF', '12'), '-vf', 'scale=out_color_matrix=bt709:out_range=tv', '-pix_fmt', 'yuv420p',
                    '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'iec61966-2-1', '-color_range', 'tv',
                    '-movflags', '+faststart', out], check=True)
print(out, f'{os.path.getsize(out) / 1e6:.1f} MB, {len(names)} frames at 60 per second, {N * W} x {H}')
for label, side in ((f'pane {k + 1}', side) for k, side in enumerate(panes)):
    changes = sum(side.sizes[d] != side.sizes[d - 1] for d in draws)
    print(f'{label} {side.label}: render scale {min(side.scales[d] for d in draws):.3f} to {max(side.scales[d] for d in draws):.3f}, '
          f'{changes} size changes in the video\'s {len(draws)} draws')
final = decode_all(out)
# The labels cover the top row of regions on both sides, so only the middle and bottom rows count.
KEEP = [3, 4, 5, 6, 7, 8]
last = len(names) - 1
for i in checks:
    cells = []
    for k, side in enumerate(panes):
        part = lambda a: a[:, k * W:(k + 1) * W]
        video = linear_regions(part(final[i])) / linear_regions(part(final[last]))
        source = side.source_regions(i) / side.source_regions(last)
        cells.append(f'{side.label}: r11 source {100 * (source[4] - 1):+.2f} %, video {100 * (video[4] - 1):+.2f} %, '
                     f'largest difference {100 * np.max(np.abs(video[KEEP] - source[KEEP])):.2f} %')
    print(f'{counters[i]}, against the last frame: ' + '; '.join(cells))
    if os.environ.get('CHECK_DIR'):
        Image.fromarray(final[i]).save(f"{os.environ['CHECK_DIR']}/{scene}-check-{i:03d}.png")

# The boil per pane (switch only, panes of 1280 x 720): boilnoise.py's residual, the mean over 16 x 16 blocks of
# |b[k] - (b[k - 1] + b[k + 1]) / 2| as a share of the crop's light, in the same crops and spans, read from the decoded
# video's pane and from the source frames scaled to the pane with Lanczos, so that the codec's share of it shows. The
# labels cover the top of boilnoise.py's ceiling crop (rows 40 to 179), so this check reads the ceiling's rows 100 to 179.
if scene == 'switch' and (W, H) == (1280, 720):
    CROPS = {'floor': (520, 700, 100, 1180), 'left wall': (150, 330, 60, 300), 'ceiling': (100, 180, 300, 980)}
    SPANS = {'before off': range(1, 19), 'off 0-60': range(21, 80), 'off 60-200': range(80, 219), 'on 0-100': range(221, 319)}

    def gray_linear(rgb):
        a = rgb.astype(float) / 255
        return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]

    def blocks(a, n=16):
        h, w = a.shape
        return a[:h // n * n, :w // n * n].reshape(h // n, n, w // n, n).mean((1, 3))

    def boil(frames):
        cells = {}
        for name, (y0, y1, x0, x1) in CROPS.items():
            b = [blocks(f[y0:y1, x0:x1]) for f in frames]
            r = {k: 100 * np.mean(np.abs(b[k] - (b[k - 1] + b[k + 1]) / 2)) / np.mean(b[k]) for k in range(1, len(b) - 1)}
            cells[name] = {span: np.mean([r[k] for k in ks]) for span, ks in SPANS.items()}
        return cells

    for k, side in enumerate(panes):
        video = boil([gray_linear(final[i][:, k * W:(k + 1) * W]) for i in range(len(names))])
        source = boil([gray_linear(np.asarray(side.frame(i).resize((W, H), Image.LANCZOS))) for i in range(len(names))])
        print(f'boil of {side.label} (video / source, %): ' + ' | '.join(
            f'{name}: ' + ', '.join(f'{span} {video[name][span]:.2f} / {source[name][span]:.2f}' for span in SPANS)
            for name in CROPS))
