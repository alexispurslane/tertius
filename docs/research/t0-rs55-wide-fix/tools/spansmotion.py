# Tool code of t0-rs55. spansmotion.py <rs-motion folder>: the GPU span of queue-timing.sh's motion runs
# rs-t-<turn|walk>-<code>-r<round> (motionrun.sh TIMING=1, the constant render scale 2/3; time-*.txt as spans03.py reads it).
# Per film, code and window (the 20 draws before the motion, the motion, the 60 draws after it): the median over the rounds of
# each run's mean span, and for wide and probe the median and the range of the paired difference against today.
import re
import statistics
import sys
from pathlib import Path

WINDOWS = {'turn': (93, 153, 213), 'walk': (213, 333, 393)}
root = Path(sys.argv[1])
runs = {}
for folder in root.glob('rs-t-*-r*'):
    m = re.fullmatch(r'rs-t-(turn|walk)-(today|wide|probe)-r(\d+)', folder.name)
    logs = list(folder.glob('time-*.txt'))
    if not m or not logs:
        continue
    span = {}
    for line in open(logs[0]):
        span.setdefault(int(line.split()[0]), float(line.split()[1]))
    runs[m.group(1), m.group(2), int(m.group(3))] = span
for film, (a, b, c) in WINDOWS.items():
    codes = [k for k in ('today', 'wide', 'probe') if any(x[:2] == (film, k) for x in runs)]
    rounds = sorted({r for (f, k, r) in runs if f == film and all((f, x, r) in runs for x in codes)})
    if not rounds:
        continue
    print(f'== {film}, {len(rounds)} paired rounds')
    for label, (lo, hi) in (('before', (a - 20, a)), ('the motion', (a, b)), ('after', (b, c))):
        v = {k: [statistics.mean(runs[film, k, r][d] for d in range(lo, hi)) for r in rounds] for k in codes}
        cells = [f'{k} {statistics.median(v[k]):.3f} ms' for k in codes]
        for k in codes[1:]:
            d = [y - x for x, y in zip(v['today'], v[k])]
            cells.append(f'{k} - today {statistics.median(d):+.3f} [{min(d):+.3f}, {max(d):+.3f}]')
        print(f'  {label} (draws {lo} to {hi - 1}): ' + ', '.join(cells))
