# Tool code of t0-rs55. spans03.py <gumm-mac folder>: the GPU span of draws 0 to 8 after the start (draw 0) and the cut (draw
# 134) of queue-timing.sh's runs rs-t-<event>-<code>-r<round> (time-*.txt "<frame> <GPU span ms> ..." per collected frame;
# a frame collected twice counts once). The codes: today, wide (the wide fix) and probe (its cost probe). Per event and draw
# it prints each code's median span over the rounds, and for wide and probe the median and the range of the paired
# difference against today (same round); then the same for the sum of draws 0 to 3, of draws 1 and 2, and the settled span
# (the median of each run's last 20 draws).
import re
import statistics
import sys
from pathlib import Path

root = Path(sys.argv[1])
AT = {'start': 0, 'cut': 134}
runs = {}
for folder in root.glob('rs-t-*-r*'):
    m = re.fullmatch(r'rs-t-(start|cut)-(today|wide|probe)-r(\d+)', folder.name)
    logs = list(folder.glob('time-*.txt'))
    if not m or not logs:
        continue
    span = {}
    for line in open(logs[0]):
        span.setdefault(int(line.split()[0]), float(line.split()[1]))
    runs[m.group(1), m.group(2), int(m.group(3))] = span
for event, at in AT.items():
    codes = [c for c in ('today', 'wide', 'probe') if any(k[:2] == (event, c) for k in runs)]
    rounds = sorted({r for (e, c, r) in runs if e == event and all((e, k, r) in runs for k in codes)})
    if not rounds:
        continue
    print(f'== {event} (draw {at}), {len(rounds)} paired rounds: ' + ', '.join(f'{c} median ms' for c in codes)
          + ' | ' + ', '.join(f'{c} - today median [min, max]' for c in codes[1:]))

    def row(label, pick):
        v = {c: [pick(runs[event, c, r]) for r in rounds] for c in codes}
        cells = [f'{statistics.median(v[c]):.3f}' for c in codes]
        for c in codes[1:]:
            d = [b - a for a, b in zip(v['today'], v[c])]
            cells.append(f'{statistics.median(d):+.3f} [{min(d):+.3f}, {max(d):+.3f}]')
        print(f'  {label}: ' + ' '.join(cells[:len(codes)]) + ' | ' + ' '.join(cells[len(codes):]))
    for k in range(9):
        row(f'draw {k}', lambda s, k=k: s[at + k])
    row('draws 0-3 sum', lambda s: sum(s[at + k] for k in range(4)))
    row('draws 1-2 sum', lambda s: s[at + 1] + s[at + 2])
    row('settled (last 20 draws)', lambda s: statistics.median(s[d] for d in sorted(s)[-20:]))
