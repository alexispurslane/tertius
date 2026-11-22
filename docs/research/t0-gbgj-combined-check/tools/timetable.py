# Tool code of t0-gbgj. timetable.py <timing folder>: the deck's GPU frame span of each run of timing.sh (the median over
# the run's frames, from gpu-timings.json, with the mean GPU clock of its timed span), the difference X2 - M2 per round,
# and over the rounds: each side's mean, the difference of the means, and the spread of the per-round differences (the
# highest minus the lowest). Design X's allowance is +0.30 ms above main.
import json, statistics, sys
from pathlib import Path
folder = Path(sys.argv[1])
def span(run):
    data = json.loads((run / 'gpu-timings.json').read_text())
    return data['frame']['medianMs'], data['frame'].get('p95Ms'), data.get('gpuClock', {}).get('meanMhz', 'unknown')
rounds = sorted({p.name.split('-')[0] for p in folder.glob('r*-m2')}, key=lambda r: int(r[1:]))
m, x, d = [], [], []
print('round  M2 ms  X2 ms  X2 - M2  clock M2 / X2 (MHz)')
for r in rounds:
    if not all((folder / f'{r}-{s}' / 'gpu-timings.json').exists() for s in ('m2', 'x2')): continue
    (a, _, ac), (b, _, bc) = span(folder / f'{r}-m2'), span(folder / f'{r}-x2')
    m.append(a); x.append(b); d.append(b - a)
    mhz = lambda c: f'{c:.0f}' if isinstance(c, (int, float)) else c
    print(f'{r:5s}  {a:.3f}  {b:.3f}  {b - a:+.3f}  {mhz(ac)} / {mhz(bc)}')
if d:
    print(f'mean   {statistics.mean(m):.3f}  {statistics.mean(x):.3f}  {statistics.mean(x) - statistics.mean(m):+.3f}'
          f'  (per-round differences: median {statistics.median(d):+.3f}, spread {max(d) - min(d):.3f})')
