# Tool code of t0-rs55. spans03.py <gumm-mac folder>: the GPU span of draws 0 to 8 after the start (draw 0) and the cut (draw
# 134) of queue-timing.sh's runs rs-t-<event>-<code>-r<round> (time-*.txt "<frame> <GPU span ms> ..." per collected frame; a
# frame collected twice counts once). Per event and draw it prints today's and the wide fix's median span over the rounds,
# and the median and the range of the paired difference wide - today (same round); then the same for the sum of draws 0 to 3
# and of draws 1 and 2, and the settled span (the median of each run's last 20 draws).
import re
import statistics
import sys
from pathlib import Path

root = Path(sys.argv[1])
AT = {'start': 0, 'cut': 134}
runs = {}
for folder in root.glob('rs-t-*-r*'):
    m = re.fullmatch(r'rs-t-(start|cut)-(today|wide)-r(\d+)', folder.name)
    logs = list(folder.glob('time-*.txt'))
    if not m or not logs:
        continue
    span = {}
    for line in open(logs[0]):
        span.setdefault(int(line.split()[0]), float(line.split()[1]))
    runs[m.group(1), m.group(2), int(m.group(3))] = span
for event, at in AT.items():
    rounds = sorted({r for (e, c, r) in runs if e == event and (e, 'today', r) in runs and (e, 'wide', r) in runs})
    if not rounds:
        continue
    print(f'== {event} (draw {at}), {len(rounds)} paired rounds: draw, today median ms, wide median ms, '
          f'wide - today median [min, max]')
    def row(label, pick):
        t = [pick(runs[event, 'today', r]) for r in rounds]
        w = [pick(runs[event, 'wide', r]) for r in rounds]
        d = [b - a for a, b in zip(t, w)]
        print(f'  {label}: {statistics.median(t):.3f} {statistics.median(w):.3f} '
              f'{statistics.median(d):+.3f} [{min(d):+.3f}, {max(d):+.3f}]')
    for k in range(9):
        row(f'draw {k}', lambda s, k=k: s[at + k])
    row('draws 0-3 sum', lambda s: sum(s[at + k] for k in range(4)))
    row('draws 1-2 sum', lambda s: s[at + 1] + s[at + 2])
    row('settled (last 20 draws)', lambda s: statistics.median(s[d] for d in sorted(s)[-20:]))
