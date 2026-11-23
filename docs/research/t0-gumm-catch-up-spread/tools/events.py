# Tool code of t0-gumm. events.py <label|run folder|event[|timing folder]>...: the picture's measures of macrun.sh's runs
# (1920 x 1080, the constant render scale 2/3) per event window. The event is the one macrun.sh ran: switch[+k] (off at
# draw 193 + k, on at 393 + k), start (draw 0) or cut[+k] (draw 134 + k). Per run and event it prints:
#   - the settle, by the survey's rule (renderer-survey.py, settle_draws): the draws from the event until a region's mean
#     light (frames.series: the whole image and a 3 x 3 grid) stays within 5 % of its settled level. The settled level is
#     the mean of 20 frames: for off, draws 373 to 392 (+k) with the lamps off for 180 draws; for on, draws 173 to 192 (+k)
#     before off; for the start and the cut, the film's last 20 frames. After a switch, a region counts when the switch
#     moves its settled level by more than 10 %; after the start and the cut every region counts. The regions' light differs widely: in the cabin
#     at draw 333 the ceiling's middle third holds the lamp (2632), its side thirds the dim ceiling lit by bounce light
#     alone (0.0029), the whole image 302. It prints the whole
#     image's settle, the slowest region's, and each counted region's (r<row><column>, row 0 at the top). "none" means not settled within the window: 180 draws after off, the
#     start and the cut, 100 after on.
#   - the boil without the fade, boilnoise.py's measure at 1920 x 1080: three crops (the floor x 150 to 1769, y 780 to
#     1049; the left wall x 90 to 449, y 225 to 494; the ceiling x 450 to 1469, y 60 to 269) in display-linear luminance,
#     averaged in blocks of 24 x 24 px (16 px of boilnoise's 1280 x 720); per draw k, the mean over the blocks of
#     |b[k] - (b[k-1] + b[k+1]) / 2| in % of the crop's mean. A fade that is linear over three draws gives zero. The mean
#     over the draws 1 to 59 after the event, and of the three crops.
#   - the grain of the first 16 draws, measure5.py's measure: per crop (floor and wall), the rms of the luminance minus
#     its 5 x 5 box mean, over the crop's mean, in %; the mean over the draws 0 to 15 after the event and the two crops.
#   - with a timing folder (macrun.sh TIMING=1): the GPU span per draw, its peak in the 60 draws from the event, the
#     settled span (the median of draws 133 to 192 of the switch, or of the film's last 20 frames), and the draws of the
#     window above the settled span by more than 10 % of (peak - settled).
import sys
from pathlib import Path
import numpy as np
from PIL import Image

CROPS = {'floor': (150, 780, 1770, 1050), 'wall': (90, 225, 450, 495), 'ceiling': (450, 60, 1470, 270)}
GRAIN = ('floor', 'wall')
BLOCK = 24


def name_of(event, k, d):
    if event == 'switch':
        return f'f-{d - 33:06d}'
    if event == 'start':
        return f'f-{d:06d}'
    if d < 133 + k:
        return f'a-{d - 33:06d}'
    return 'x-000000' if d == 133 + k else f'b-{d - 134 - k:06d}'


def windows(event, k):
    """(window name, event draw, settle end, settled level's draws, the draws of the level before or None)"""
    if event == 'switch':
        off, on = 193 + k, 393 + k
        return [('off', off, off + 180, range(373 + k, 393 + k), range(173 + k, 193 + k)),
                ('on', on, on + 100, range(173 + k, 193 + k), range(373 + k, 393 + k))]
    if event == 'start':
        return [('start', 0, 180, range(180, 200), None)]
    return [('cut', 134 + k, 314 + k, range(314 + k, 334 + k), None)]


def luminance(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(float) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]


def blocks(y):
    h, w = (y.shape[0] // BLOCK) * BLOCK, (y.shape[1] // BLOCK) * BLOCK
    return y[:h, :w].reshape(h // BLOCK, BLOCK, w // BLOCK, BLOCK).mean(axis=(1, 3))


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
    label, folder, ev, *timing = spec.split('|')
    folder = Path(folder)
    event, k = (ev.split('+') + ['0'])[:2]
    k = int(k)
    rows = {}
    for line in open(folder / 'frames.series'):
        n, *v = line.split()
        rows[n] = [float(x) for x in v]
    span = {}
    if timing:
        for line in open(next(Path(timing[0]).glob('time-*.txt'))):
            span.setdefault(int(line.split()[0]), float(line.split()[1]))
    for w, at, end, lev, pre in windows(event, k):
        series = lambda d, r: rows[name_of(event, k, d)][r]
        # The switch film has 460 frames whatever k is, so a moved event's last window ends at the film's last frame.
        end = next((d for d in range(at, end) if name_of(event, k, d) not in rows), end)
        counts, names = [], []
        for r in range(10):
            level = np.mean([series(d, r) for d in lev])
            if pre is not None and abs(np.mean([series(d, r) for d in pre]) - level) <= 0.10 * abs(level) and r > 0:
                continue
            counts.append(settle({d: series(d, r) for d in range(at, end)}, level, at, end))
            names.append('whole' if r == 0 else f'r{(r - 1) // 3}{(r - 1) % 3}')
        whole, regions = counts[0], counts[1:]
        slowest = None if None in regions else max(regions, default=0)
        fmt = lambda s: 'none' if s is None else str(s)
        # the boil and the grain from the kept PNGs
        cache = {}
        def crop_blocks(d):
            if d not in cache:
                y = luminance(folder / f'{name_of(event, k, d)}.png')
                cache[d] = {c: (blocks(y[y0:y1, x0:x1]), y[y0:y1, x0:x1]) for c, (x0, y0, x1, y1) in CROPS.items()}
            return cache[d]
        boil = {c: [] for c in CROPS}
        for d in range(at + 1, at + 60):
            a, b, n = crop_blocks(d - 1), crop_blocks(d), crop_blocks(d + 1)
            for c in CROPS:
                boil[c].append(100 * np.abs(b[c][0] - (a[c][0] + n[c][0]) / 2).mean() / b[c][0].mean())
        gr = [np.mean([grain(crop_blocks(d)[c][1]) for c in GRAIN]) for d in range(at, at + 16)]
        bm = {c: np.mean(v) for c, v in boil.items()}
        line = (f'{label} {w}' + (f' (settle window {end - at} draws)' if end - at not in (100, 180) else '') + f': settle whole {fmt(whole)}, slowest region {fmt(slowest)} ('
                + ' '.join(f'{n} {fmt(c)}' for n, c in zip(names[1:], regions)) + '); '
                f'boil 1-59 {np.mean(list(bm.values())):.3f} % (' + ', '.join(f'{c} {v:.3f}' for c, v in bm.items()) +
                f'); grain 0-15 {np.mean(gr):.2f} %')
        if span:
            settled_draws = range(133, 193) if event == 'switch' else lev
            base = float(np.median([span[d] for d in settled_draws if d in span]))
            win = [span[d] for d in range(at, at + 60) if d in span]
            peak = max(win)
            line += (f'; GPU span settled {base:.2f} ms, peak {peak:.2f} ms, '
                     f'{sum(x > base + 0.1 * (peak - base) for x in win)} draws above')
        print(line, flush=True)
