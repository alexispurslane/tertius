# Tool code of t0-gumm. knowndip.py <card folder>...: an estimate, not a measure, of form (b) of the line orchestrator's
# order of 2026-11-22, 23:32: today's cliff with a known dip. The renderer tells the controller which draws trace the
# catch-up rays (the dear draws), the controller gives each of them the render scale that fits the target, and it returns to
# the rest at once after the last one. From today's card logs of cardrun.sh (size-*.txt "<draw> <w> <h> <scale>", time-*.txt
# "<draw> <GPU span ms> ..."; a draw logged twice counts once):
#   the dear draws: the draws whose own-ray logs on the Mac (gumm-mac/*-today, the same code and films) hold the catch-up
#     rays: the start 0 to 7, the lamps off 197 to 206, on 397 to 406, the cut 133 to 141 (the frame on the chase camera at
#     133 is a cut too);
#   the rest: the median scale of the settled draws before the event (cardreturn.py's "before");
#   the estimated scale of a dear draw d: min(rest, scale[d] * sqrt(target / span[d])), the scale at which its logged span
#     would fit the target of 16.67 ms if the span grew with the pixels (the square of the scale); every other draw of the
#     window keeps the rest.
# Per event it prints the logged draws under the rest - 0.02 (to the next event or the film's end) and the logged lowest scale,
# against the estimate's. The estimate leaves out: the part of a draw's cost that does not grow with the pixels (so the
# real dip must be deeper), the first draws' costs that are not catch-up rays (the start dips about 50 draws without any
# catch-up ray: "none" on the card), FSR's history across two sudden changes of the scale, and the controller's dead band.
import sys
from pathlib import Path
import numpy as np

TARGET = 16.6667
EVENTS = {'start': ('switch', 0, 193, range(0, 8), (133, 193)), 'off': ('switch', 193, 393, range(197, 207), (133, 193)),
          'on': ('switch', 393, 493, range(397, 407), (333, 393)), 'cut': ('cut', 134, 334, range(133, 142), (74, 134))}
for folder in map(Path, sys.argv[1:]):
    runs = sorted(p.name[len('switch-1,8,0'):] for p in folder.glob('switch-1,8,0*'))
    for sfx in runs:
        cells = []
        for name, (film, at, end, dear, before) in EVENTS.items():
            f = folder / f'{film}-1,8,0{sfx}'
            scale, span = {}, {}
            for line in open(next(f.glob('size-*.txt'))):
                scale.setdefault(int(line.split()[0]), float(line.split()[3]))
            for line in open(next(f.glob('time-*.txt'))):
                span.setdefault(int(line.split()[0]), float(line.split()[1]))
            rest = float(np.median([scale[d] for d in range(*before) if d in scale]))
            if name == 'start':
                rest = float(np.median([scale[d] for d in range(133, 193)]))
            window = [d for d in range(min(at, dear.start), end) if d in scale]
            est = {d: (min(rest, scale[d] * np.sqrt(TARGET / span[d])) if d in dear and d in span else rest) for d in window}
            logged_under = sum(scale[d] < rest - 0.02 for d in window)
            est_under = sum(est[d] < rest - 0.02 for d in window)
            cells.append(f'{name}: logged {logged_under} under, lowest {min(scale[d] for d in window):.3f}; '
                         f'estimate {est_under} under, lowest {min(est.values()):.3f}')
        print(f'{folder.name}{sfx or ""}: rest per event as cardreturn.py; ' + ' | '.join(cells))
