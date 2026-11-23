# Tool code of t0-rs55, from t0-gumm's videos.py. videos.py <out.mp4> <bay-start|turn|walk> <pane>...: a side-by-side video of
# one film, one pane per Mac run, left to right. Each pane is "label|run folder": the folder holds the run's PNGs (every draw
# of the window) and its size log (size-*.txt, "<draw> <width> <height> <scale>"). The windows, one draw per video frame:
#   bay-start: fairrun.sh VIDEO=1, f 0 to 119, draws 0 to 119 (the frozen bay start at the constant render scale 2/3);
#   turn: motionrun.sh, f 50 to 59, g000 to g059 (the turn) and h 0 to 59: draws 83 to 212;
#   walk: motionrun.sh, f 170 to 179, g 0 to 119 (the walk) and h 0 to 59: draws 203 to 392.
# Each pane is scaled with Lanczos to PANE (1280x720 by default). Under its label: the render scale of that draw and its
# render size, in orange on a draw whose render size differs from the draw before. In the corner: the draw counter. 60
# frames a second, H.264 yuv420p (BT.709) at CRF 12 or CRF=<value>, no other filter. The exposure is the runs' fixed one.
# The check decodes the file back and compares, at three frames, each pane's 3 x 3 regions' display-linear luminance
# against the video's last frame with the source PNGs'. CHECK_DIR=<folder> saves those three frames as PNGs.
import glob
import os
import subprocess
import sys
import tempfile
import numpy as np
from PIL import Image, ImageDraw, ImageFont

out, event, *specs = sys.argv[1:]
if event == 'bay-start':
    names = [f'f-{k:06d}' for k in range(120)]
    draws = list(range(120))
    counters = [f'draws since the start: {d}' for d in draws]
    checks = (2, 10, 40)
elif event == 'turn':
    names = [f'f-{k:06d}' for k in range(50, 60)] + [f'g{k:03d}-000000' for k in range(60)] + [f'h-{k:06d}' for k in range(60)]
    draws = list(range(83, 213))
    counters = [f'{d - 93:+d} draws to the turn' if d < 93 else f'turning, draw {d - 93}' if d < 153
                else f'standing again, draw {d - 153}' for d in draws]
    checks = (20, 60, 100)
else:
    names = [f'f-{k:06d}' for k in range(170, 180)] + [f'g-{k:06d}' for k in range(120)] + [f'h-{k:06d}' for k in range(60)]
    draws = list(range(203, 393))
    counters = [f'{d - 213:+d} draws to the walk' if d < 213 else f'walking, draw {d - 213}' if d < 333
                else f'standing again, draw {d - 333}' for d in draws]
    checks = (30, 100, 160)
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
