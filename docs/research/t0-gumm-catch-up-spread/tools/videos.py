# Tool code of t0-gumm. videos.py <out.mp4> <switch|start|cut> <pane>...: the final side-by-side video of one event, one
# pane per Mac run of queue-mac5.sh, left to right. Each pane is "label|run folder": the folder holds the run's PNGs
# (macrun.sh VIDEO=1, every draw of the window) and its size log (size-*.txt, "<draw> <width> <height> <scale>"), which
# played back the card's scale log of that code. The windows, one draw per video frame:
#   switch: film frames f 140 to 459, draws 173 to 492 (the lamps go off at draw 193 and on at 393);
#   start: f 0 to 119, draws 0 to 119 (start.json draws its first frame once, so f-k is draw k);
#   cut: a 80 to 99 (draws 113 to 132), x (the chase camera, draw 133), b 0 to 119 (draws 134 to 253; the cut back to the
#        cabin at draw 134 restarts every history).
# Each pane is scaled with Lanczos to PANE (1280x720 by default). Under its label: the render scale of that draw and its
# render size, in orange on a draw whose render size differs from the draw before. In the corner: the draws since the
# event. 60 frames a second, H.264 yuv420p (BT.709) at CRF 12 or CRF=<value>, no other filter. The exposure is the runs'
# fixed TERTIUS_PROBE_EXPOSURE=4.08 (macrun.sh). ffmpeg and Pillow come from the dev shell.
# The check decodes the file back and compares, at three frames, each pane's fade (its middle and bottom 3 x 3 regions'
# display-linear luminance over that of the video's last frame; the last pane leaves out its bottom right region, under
# the counter) with the source PNGs' fade. CHECK_DIR=<folder> saves
# those three frames as PNGs.
import glob
import os
import subprocess
import sys
import tempfile
import numpy as np
from PIL import Image, ImageDraw, ImageFont

out, event, *specs = sys.argv[1:]
STILL = 33
if event == 'switch':
    names = [f'f-{k:06d}' for k in range(140, 460)]
    draws = [STILL + k for k in range(140, 460)]
    counters = [f'lamps on, {d - 193:+d} draws to off' if d < 193 else f'lamps off, draw {d - 193}' if d < 393
                else f'lamps on again, draw {d - 393}' for d in draws]
    checks = (40, 100, 240)
elif event == 'start':
    names = [f'f-{k:06d}' for k in range(120)]
    draws = list(range(120))
    counters = [f'draws since the start: {d}' for d in draws]
    checks = (2, 10, 40)
else:
    names = [f'a-{k:06d}' for k in range(80, 100)] + ['x-000000'] + [f'b-{k:06d}' for k in range(120)]
    draws = list(range(113, 254))
    counters = [f'{d - 134:+d} draws to the cut' if d < 133 else 'the chase camera' if d == 133
                else f'draws since the cut: {d - 134}' for d in draws]
    checks = (23, 31, 61)
W, H = map(int, os.environ.get('PANE', '1280x720').split('x'))
try:
    FONT = ImageFont.load_default(size=26)
except TypeError:
    FONT = ImageFont.load_default()


def linear_regions(a):
    a = a.astype(float) / 255
    a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]
    h, w = a.shape
    return np.array([a[r * h // 3:(r + 1) * h // 3, c * w // 3:(c + 1) * w // 3].mean() for r in range(3) for c in range(3)])


class Pane:
    def __init__(self, spec):
        self.label, self.folder = spec.split('|')
        log = {int(l.split()[0]): l.split() for l in open(glob.glob(f'{self.folder}/size-*.txt')[0])}
        self.sizes = {d: (int(v[1]), int(v[2])) for d, v in log.items()}
        self.scales = {d: float(v[3]) for d, v in log.items()}
        missing = [n for n in names if not os.path.exists(f'{self.folder}/{n}.png')]
        assert not missing, f'{self.folder} lacks {len(missing)} pictures, the first {missing[0]}'

    def frame(self, i):
        return Image.open(f'{self.folder}/{names[i]}.png').convert('RGB')


def text(draw, x, y, words, colour=(255, 255, 255)):
    box = draw.textbbox((x, y), words, font=FONT)
    draw.rectangle((box[0] - 8, box[1] - 6, box[2] + 8, box[3] + 6), fill=(0, 0, 0))
    draw.text((x, y), words, fill=colour, font=FONT)


panes = [Pane(s) for s in specs]
N = len(panes)
with tempfile.TemporaryDirectory(dir=os.path.dirname(os.path.abspath(out))) as work:
    for i, d in enumerate(draws):
        frame = Image.new('RGB', (N * W, H))
        g = ImageDraw.Draw(frame)
        for k, pane in enumerate(panes):
            frame.paste(pane.frame(i).resize((W, H), Image.LANCZOS), (k * W, 0))
            text(g, k * W + 14, 12, pane.label)
            changed = d > 0 and pane.sizes[d] != pane.sizes[d - 1]
            w, h = pane.sizes[d]
            text(g, k * W + 14, 54, f'render scale {pane.scales[d]:.3f} ({w} x {h}){"  changed" if changed else ""}',
                 (255, 160, 40) if changed else (255, 255, 255))
        box = g.textbbox((0, 0), counters[i], font=FONT)
        text(g, N * W - (box[2] - box[0]) - 30, H - 46, counters[i])
        frame.save(f'{work}/c-{i:04d}.png', compress_level=1)
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-framerate', '60', '-i', f'{work}/c-%04d.png', '-c:v', 'libx264',
                    '-preset', 'slow', '-crf', os.environ.get('CRF', '12'), '-vf', 'scale=out_color_matrix=bt709:out_range=tv',
                    '-pix_fmt', 'yuv420p', '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'iec61966-2-1',
                    '-color_range', 'tv', '-movflags', '+faststart', out], check=True)
print(out, f'{os.path.getsize(out) / 1e6:.1f} MB, {len(names)} frames at 60 per second, {N * W} x {H}')
for pane in panes:
    changes = sum(pane.sizes[d] != pane.sizes[d - 1] for d in draws if d > 0)
    print(f'{pane.label}: render scale {min(pane.scales[d] for d in draws):.3f} to {max(pane.scales[d] for d in draws):.3f}, '
          f'{changes} size changes in the video\'s {len(draws)} draws')
raw = subprocess.run(['ffmpeg', '-loglevel', 'error', '-i', out, '-vf', 'scale=in_color_matrix=bt709:in_range=tv',
                      '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], check=True, capture_output=True).stdout
final = np.frombuffer(raw, np.uint8).reshape(-1, H, N * W, 3)
assert len(final) == len(names), f'the file has {len(final)} frames, not {len(names)}'
KEEP = [3, 4, 5, 6, 7, 8]  # the labels cover the top row of regions
KEEP_LAST = [3, 4, 5, 6, 7]  # and the counter covers the last pane's bottom right region
last = len(names) - 1
for i in checks:
    cells = []
    for k, pane in enumerate(panes):
        part = lambda a: a[:, k * W:(k + 1) * W]
        keep = KEEP_LAST if k == N - 1 else KEEP
        video = linear_regions(part(final[i])) / linear_regions(part(final[last]))
        source = linear_regions(np.asarray(pane.frame(i))) / linear_regions(np.asarray(pane.frame(last)))
        cells.append(f'{pane.label}: r11 source {100 * (source[4] - 1):+.2f} %, video {100 * (video[4] - 1):+.2f} %, '
                     f'largest difference {100 * np.max(np.abs(video[keep] - source[keep])):.2f} %')
    print(f'{counters[i]}, against the last frame: ' + '; '.join(cells))
    if os.environ.get('CHECK_DIR'):
        Image.fromarray(final[i]).save(f"{os.environ['CHECK_DIR']}/{event}-check-{i:03d}.png")
