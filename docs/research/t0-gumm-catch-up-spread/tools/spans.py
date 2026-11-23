# Tool code of t0-gumm. spans.py <timing folder>...: the GPU span per draw of macrun.sh's timing runs (TIMING=1: the
# GPU lock held alone, no pictures, the constant scale 2/3; time-*.txt "<frame> <GPU span ms> ..." per collected frame,
# a frame collected twice counts once). The folder's name is t-<event>-<code>, with the event switch, start or cut. Per
# event window (off at draw 193 and on at 393 for the switch, draw 0 for the start, draw 134 for the cut) it prints the
# settled span (the median of the switch's draws 133 to 192, or of the film's last 20 draws), the peak span in the 60
# draws from the event, the draws above the settled span by more than 10 % of (peak - settled), and the sum of the span
# over the settled one in those 60 draws (ms).
import statistics
import sys
from pathlib import Path

WINDOWS = {'switch': [('off', 193), ('on', 393)], 'start': [('start', 0)], 'cut': [('cut', 134)]}
for folder in map(Path, sys.argv[1:]):
    _, event, code = folder.name.split('-', 2)
    span = {}
    for line in open(next(folder.glob('time-*.txt'))):
        span.setdefault(int(line.split()[0]), float(line.split()[1]))
    last = max(span)
    settled_draws = range(133, 193) if event == 'switch' else range(last - 19, last + 1)
    base = statistics.median(span[d] for d in settled_draws if d in span)
    for name, at in WINDOWS[event]:
        win = [span[d] for d in range(at, at + 60) if d in span]
        peak = max(win)
        above = sum(x > base + 0.1 * (peak - base) for x in win)
        extra = sum(max(x - base, 0) for x in win)
        print(f'{code} {name}: settled {base:.2f} ms, peak {peak:.2f} ms, {above} draws above, extra {extra:.1f} ms over 60 draws')
