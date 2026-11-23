# Tool code of t0-gumm. fairmeasure.py <label|run folder|bay or cabin|start or cut[+k]>...: the picture's measures of the
# fair test's runs (fairrun.sh: frozen films at 1920 x 1080 and the constant render scale 2/3), with events.py's formulas:
#   - the settle: the draws from the event until a region's mean light (frames.series: the whole image and a 3 x 3 grid)
#     stays within 5 % of its settled level, the mean of the film's last 20 frames; every region counts. The window is
#     180 draws. The slowest region's settle, "none" if one does not settle in the window;
#   - the boil without the fade: per crop, display-linear luminance averaged in blocks (24 x 24 px for a broad crop,
#     6 x 6 px for the narrow one), per draw k the mean over the blocks of |b[k] - (b[k-1] + b[k+1]) / 2| in % of the
#     crop's mean. A fade that is linear over three draws gives zero;
#   - the grain: per crop, the rms of the luminance minus its 5 x 5 box mean, over the crop's mean, in %.
# Per run it prints the slowest region's settle, the boil over draws 1 to 59 after the event (the mean of the broad
# crops), the grain over draws 0 to 15 (the mean of the two grain crops), and the narrow crop's boil 1 to 59 and grain
# 0 to 15. It writes <run folder>/fair.json with the per-draw values: the grain of draws 0 to 15 and the boil of draws 1
# to 59 after the event, per crop.
# The crops, in pixels of the 1920 x 1080 picture (x0, y0, x1, y1), drawn on each scene's settled frame:
#   cabin (the switch film's view): events.py's floor, wall and ceiling; the narrow crop is the lit wall strip beside the
#     near left window opening's edge, 22 px wide;
#   bay (bay-camera): the floor, the right wall and the back wall below the window opening; the narrow crop is a strip
#     of the right wall beside its corner with the back wall, 30 px wide.
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image

CROPS = {
    'cabin': {'floor': (150, 780, 1770, 1050), 'wall': (90, 225, 450, 495), 'ceiling': (450, 60, 1470, 270),
              'narrow': (156, 490, 178, 590)},
    'bay': {'floor': (560, 700, 1000, 1000), 'wall': (1300, 160, 1840, 560), 'back wall': (620, 420, 980, 560),
            'narrow': (1030, 120, 1060, 460)},
}
GRAIN = ('floor', 'wall')


def name_of(event, k, d):
    if event == 'start':
        return f'f-{d:06d}'
    if d < 100 + k:
        return f'a-{d:06d}'
    return 'x-000000' if d == 100 + k else f'b-{d - 101 - k:06d}'


def luminance(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(float) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]


def blocks(y, n):
    h, w = (y.shape[0] // n) * n, (y.shape[1] // n) * n
    return y[:h, :w].reshape(h // n, n, w // n, n).mean(axis=(1, 3))


def grain(y):
    p = np.pad(y, 2, mode='edge')
    c = np.cumsum(np.cumsum(p, 0), 1)
    c = np.pad(c, ((1, 0), (1, 0)))
    box = (c[5:, 5:] - c[:-5, 5:] - c[5:, :-5] + c[:-5, :-5]) / 25
    return 100 * np.sqrt(((y - box) ** 2).mean()) / y.mean()


def settle(series, level, first, last):
    bad = [d for d in range(first, last) if abs(series[d] - level) > 0.05 * abs(level)]
    if not bad:
        return 0
    return None if bad[-1] == last - 1 else bad[-1] - first + 1


for spec in sys.argv[1:]:
    label, folder, scene, ev = spec.split('|')
    folder = Path(folder)
    event, k = (ev.split('+') + ['0'])[:2]
    k = int(k)
    crops = CROPS[scene]
    rows = {}
    for line in open(folder / 'frames.series'):
        n, *v = line.split()
        rows[n] = [float(x) for x in v]
    at = 0 if event == 'start' else 101 + k
    last = max(d for d in range(0, 400 + k) if name_of(event, k, d) in rows)
    end, lev = min(at + 180, last + 1), range(last - 19, last + 1)
    counts = []
    for r in range(10):
        series = {d: rows[name_of(event, k, d)][r] for d in range(at, end)}
        level = np.mean([rows[name_of(event, k, d)][r] for d in lev])
        counts.append(settle(series, level, at, end))
    slowest = None if None in counts[1:] else max(counts[1:])
    cache = {}

    def crop(d):
        if d not in cache:
            y = luminance(folder / f'{name_of(event, k, d)}.png')
            cache[d] = {c: y[y0:y1, x0:x1] for c, (x0, y0, x1, y1) in crops.items()}
        return cache[d]

    boil = {c: [] for c in crops}
    for d in range(at + 1, at + 60):
        a, b, n = crop(d - 1), crop(d), crop(d + 1)
        for c in crops:
            size = 6 if c == 'narrow' else 24
            ba, bb, bn = blocks(a[c], size), blocks(b[c], size), blocks(n[c], size)
            boil[c].append(100 * np.abs(bb - (ba + bn) / 2).mean() / bb.mean())
    gr = {c: [grain(crop(d)[c]) for d in range(at, at + 16)] for c in crops}
    broad = [c for c in crops if c != 'narrow']
    result = {'label': label, 'scene': scene, 'event': ev, 'settleWhole': counts[0], 'settleSlowest': slowest,
              'boil': boil, 'grain': gr,
              'boil1to59': float(np.mean([np.mean(boil[c]) for c in broad])),
              'grain0to15': float(np.mean([np.mean(gr[c]) for c in GRAIN])),
              'narrowBoil1to59': float(np.mean(boil['narrow'])), 'narrowGrain0to15': float(np.mean(gr['narrow'])),
              'boil1to8': float(np.mean([np.mean(boil[c][:8]) for c in broad])),
              'narrowBoil1to8': float(np.mean(boil['narrow'][:8]))}
    (folder / 'fair.json').write_text(json.dumps(result))
    fmt = lambda s: 'none' if s is None else str(s)
    print(f"{label} {scene} {ev}: settle whole {fmt(counts[0])}, slowest region {fmt(slowest)}; boil 1-59 "
          f"{result['boil1to59']:.3f} %, 1-8 {result['boil1to8']:.3f} %; grain 0-15 {result['grain0to15']:.2f} %; narrow: "
          f"boil 1-59 {result['narrowBoil1to59']:.3f} %, 1-8 {result['narrowBoil1to8']:.3f} %, grain 0-15 "
          f"{result['narrowGrain0to15']:.2f} %", flush=True)
