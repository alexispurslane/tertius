# Tool code of t0-3vwx. startchart.py <times folder> <out.png>: the start of each run of cloudvideo.sh times (the card,
# no pictures, the dynamic render scale at 16.67 ms), draws 0 to 120, in three panels: the render scale, the GPU span
# (ms, cut at 40 ms; the first run's draw 0 of 230 to 239 ms is written beside its point), and the diffuse ray pass in ms
# per million render pixels. One line per run: the first run of each binary (r1 of the walk) in red, the other runs in
# blue, main and Design X with the same colour, since they lie on each other.
import gzip
import sys
from pathlib import Path
from PIL import Image, ImageDraw

root, out = Path(sys.argv[1]), sys.argv[2]
N = 121
runs = []
for folder in sorted(p for p in root.iterdir() if p.is_dir()):
    size = {int(l.split()[0]): (int(l.split()[1]), int(l.split()[2]), float(l.split()[3]))
            for l in open(next(folder.glob('size-*.txt')))}
    span = {}
    for l in open(next(folder.glob('time-*.txt'))):
        span.setdefault(int(l.split()[0]), float(l.split()[1]))
    rays = {}
    with gzip.open(next(folder.glob('pass-*.txt.gz')), 'rt') as stream:
        for l in stream:
            f, name, _, ms = l.split()
            if name == 'diffuse_rays':
                rays[int(f)] = float(ms)
    first = folder.name.startswith('walk') and folder.name.endswith('r1')
    runs.append((folder.name, first, [size[d][2] for d in range(N)], [span.get(d, 0.0) for d in range(N)],
                 [rays.get(d, 0.0) / (size[d][0] * size[d][1] / 1e6) for d in range(N)]))

W, PH, L = 1300, 280, 70
im = Image.new('RGB', (W, PH * 3 + 40), 'white')
d = ImageDraw.Draw(im)
x = lambda k: L + k / (N - 1) * (W - L - 20)
panels = [('render scale', 2, 0.5, 1.02, [0.5, 0.6, 0.7, 0.8, 0.9, 1.0]),
          ('GPU span, ms (cut at 40)', 3, 0, 40, [0, 10, 16.67, 20, 30, 40]),
          ('diffuse_rays, ms per million render pixels', 4, 0, 4, [0, 1, 2, 3, 4])]
for p, (title, k, lo, hi, ticks) in enumerate(panels):
    top = p * PH
    y = lambda v: top + 26 + (hi - min(max(v, lo), hi)) / (hi - lo) * (PH - 56)
    for t in ticks:
        d.line([(L, y(t)), (W - 20, y(t))], fill=(225, 225, 225))
        d.text((8, y(t) - 6), f'{t:g}', fill='black')
    for draw in (8, 33):
        d.line([(x(draw), top + 26), (x(draw), top + PH - 30)], fill=(170, 170, 170))
    for name, first, *series in runs:
        values = series[k - 2]
        colour = (200, 60, 60) if first else (40, 110, 200)
        d.line([(x(i), y(v)) for i, v in enumerate(values)], fill=colour, width=2)
        if k == 3 and values[0] > hi:
            d.text((x(0) + 6, y(hi) + 2), f'draw 0: {values[0]:.0f} ms ({name})', fill=colour)
    d.text((L, top + 6), f'{title}. Red: the first run of each binary (walk r1, main and Design X); blue: the six other runs. '
                         f'Grey lines: draw 8, draw 33 (the film starts)', fill='black')
d.text((L, PH * 3 + 14), 'draw 0 to 120 of the run (the card, FrameTimeTarget 16.67 ms, no pictures)', fill='black')
im.save(out)
print(out, im.size)
