# Tool code of t0-3vwx. timeslog.py <times folder>: the card's time and per-pass logs of cloudvideo.sh times (runs without
# pictures under the dynamic render scale at 16.67 ms, two runs per scene and code; m = main ac5f23915, x = X5). Per run
# folder <scene>-<m|x>-<target>-r<run>: size-*.txt ("<frame> <width> <height> <scale>" per draw), time-*.txt ("<frame>
# <GPU span> <before the output> <at the render size> <output>" in ms per collected frame) and pass-*.txt.gz ("<frame>
# <pass> <cost group> <ms>" per pass). A frame collected twice counts once.
# It prints:
#   1. the render scale over draws 0 to 60, and the first draw where it falls below 0.9 and its lowest draw;
#   2. per cost group (the controller's: BeforeRenderSize, AtRenderSize, AfterRenderSize, Output) the mean ms per draw in
#      the window of the fall (draws 2 to 53) and in a quiet stretch (draws 100 to 170, before the walk starts at draw
#      213 and before the lamps go off at draw 193), with the passes at the render size also per million render pixels;
#      then the passes whose mean rises most from the quiet stretch to the fall's window;
#   3. in the switch film's window (draws 173 to the end): the GPU span and the at-size cost per million render pixels on
#      the draws 1 to 3 after a size change against draws of an unchanged size for at least 6 draws, each pair compared at
#      the same render size where both kinds occur.
import gzip
import sys
from collections import defaultdict
from pathlib import Path
import numpy as np

GROUPS = ('BeforeRenderSize', 'AtRenderSize', 'AfterRenderSize', 'Output')
FALL, QUIET = range(2, 54), range(100, 171)


def load(folder):
    size = {int(l.split()[0]): (int(l.split()[1]), int(l.split()[2]), float(l.split()[3]))
            for l in open(next(folder.glob('size-*.txt')))}
    span = {}
    for l in open(next(folder.glob('time-*.txt'))):
        f, s, *_ = l.split()
        span[int(f)] = float(s)
    passes = defaultdict(dict)
    with gzip.open(next(folder.glob('pass-*.txt.gz')), 'rt') as stream:
        for l in stream:
            f, name, group, ms = l.split()
            passes[int(f)][name] = (group, float(ms))
    return size, span, passes


def group_ms(passes, frames):
    out = {g: [] for g in GROUPS}
    for f in frames:
        if f not in passes:
            continue
        sums = defaultdict(float)
        for group, ms in passes[f].values():
            sums[group] += ms
        for g in GROUPS:
            out[g].append(sums[g])
    return {g: float(np.mean(v)) if v else float('nan') for g, v in out.items()}


root = Path(sys.argv[1])
for folder in sorted(p for p in root.iterdir() if p.is_dir()):
    size, span, passes = load(folder)
    scale = [size[d][2] for d in range(61)]
    low = int(np.argmin(scale))
    first = next((d for d in range(61) if scale[d] < 0.9), None)
    print(f'== {folder.name}: {len(size)} draws, {len(span)} timed frames')
    print(f'  scale draws 0-60: ' + ' '.join(f'{s:.2f}' for s in scale[:61:3]) + f' (every third draw); first below 0.9 at '
          f'draw {first}, lowest {scale[low]:.3f} at draw {low}')
    fall, quiet = group_ms(passes, FALL), group_ms(passes, QUIET)
    area = lambda frames: np.mean([size[f][0] * size[f][1] / 1e6 for f in frames if f in passes])
    print('  mean ms per draw, fall (draws 2-53) / quiet (draws 100-170): ' +
          ', '.join(f'{g} {fall[g]:.3f} / {quiet[g]:.3f}' for g in GROUPS) +
          f'; GPU span {np.mean([span[f] for f in FALL if f in span]):.3f} / {np.mean([span[f] for f in QUIET if f in span]):.3f}')
    print(f'  at the render size per million render pixels: {fall["AtRenderSize"] / area(FALL):.3f} / '
          f'{quiet["AtRenderSize"] / area(QUIET):.3f} (mean render pixels {area(FALL):.3f} / {area(QUIET):.3f} million)')
    names = sorted({n for f in passes for n in passes[f]})
    rise = []
    for n in names:
        a = [passes[f][n][1] for f in FALL if f in passes and n in passes[f]]
        b = [passes[f][n][1] for f in QUIET if f in passes and n in passes[f]]
        if a and b:
            group = next(passes[f][n][0] for f in passes if n in passes[f])
            rise.append((np.mean(a) - np.mean(b), n, group, np.mean(a), np.mean(b)))
    rise.sort(reverse=True)
    print('  passes that rise most: ' + '; '.join(f'{n} ({g}) {a:.3f} / {b:.3f}' for d, n, g, a, b in rise[:6]))
    if folder.name.startswith('switch'):
        draws = sorted(d for d in size if d >= 173)
        since, n = {}, 99
        for d in range(1, max(draws) + 1):
            n = 0 if size[d][:2] != size[d - 1][:2] else n + 1
            since[d] = n
        after = defaultdict(list)
        steady = defaultdict(list)
        for d in draws:
            if d not in passes or d not in span:
                continue
            at = sum(ms for group, ms in passes[d].values() if group == 'AtRenderSize')
            key = size[d][:2]
            row = (span[d], at / (key[0] * key[1] / 1e6))
            if 1 <= since[d] <= 3:
                after[key].append(row)
            elif since[d] >= 6:
                steady[key].append(row)
        shared = [k for k in after if k in steady]
        da = [np.mean([r[0] for r in after[k]]) - np.mean([r[0] for r in steady[k]]) for k in shared]
        dp = [np.mean([r[1] for r in after[k]]) - np.mean([r[1] for r in steady[k]]) for k in shared]
        na = sum(len(after[k]) for k in shared)
        ns = sum(len(steady[k]) for k in shared)
        print(f'  after a size change (draws 1-3 after) against unchanged (6+ draws), paired by render size over '
              f'{len(shared)} sizes ({na} / {ns} draws): GPU span {np.mean(da):+.3f} ms, at-size cost '
              f'{np.mean(dp):+.3f} ms per million pixels' if shared else '  no size has both kinds of draw')
