# Tool code of t0-rs55. cardmotion.py <card job folder>: the render scale and the GPU span of cardwide.sh's turn and walk runs
# on the card (out/card/<turn|walk>-<code>-r<round>/, size-*.txt and time-*.txt as cardwide.py reads them). Per film, code and
# round: the median scale and span of the 20 draws before the motion, of the motion (turn: draws 93 to 152; walk: 213 to
# 332) and of the 60 draws after it, the lowest scale of the motion and after it, and the count of size changes in those
# windows.
import statistics
import sys
from pathlib import Path

WINDOWS = {'turn': (93, 153, 213), 'walk': (213, 333, 393)}
root = Path(sys.argv[1]) / 'out' / 'card'
for film, (a, b, c) in WINDOWS.items():
    print(f'== {film}: before (20 draws) | the motion ({a} to {b - 1}) | after ({b} to {c - 1})')
    for folder in sorted(root.glob(f'{film}-*-r*')):
        scale, span = {}, {}
        for line in open(next(folder.glob('size-*.txt'))):
            scale.setdefault(int(line.split()[0]), float(line.split()[3]))
        for line in open(next(folder.glob('time-*.txt'))):
            span.setdefault(int(line.split()[0]), float(line.split()[1]))
        cells = []
        for lo, hi in ((a - 20, a), (a, b), (b, c)):
            s = [scale[d] for d in range(lo, hi) if d in scale]; t = [span[d] for d in range(lo, hi) if d in span]
            changes = sum(scale.get(d) != scale.get(d - 1) for d in range(lo, hi) if d in scale and d - 1 in scale)
            cells.append(f'scale median {statistics.median(s):.3f} lowest {min(s):.3f}, span median {statistics.median(t):.2f} ms, '
                         f'{changes} size changes')
        print(f'  {folder.name[len(film) + 1:]}: ' + ' | '.join(cells))
