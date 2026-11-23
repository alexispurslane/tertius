# Tool code of t0-gumm. cardreturn.py <card folder> [code]: the card's slow return after each event, for one code (today's
# cliff 1,8,0 by default), from cardrun.sh's scale logs (size-*.txt, "<frame> <width> <height> <scale>" per draw), per run.
# The line orchestrator's column of 2026-11-22, 23:25: the number of draws that the render scale stays more than 0.02 under
# its rest after the event. Two rests, since a light change can move the settled scale:
#   before: the median scale of the settled draws before the event (the start: the idle window 133 to 192 of the switch
#           film; off: 133 to 192; on: 333 to 392; the cut: 74 to 133 of the cut film);
#   after:  the median scale of the last 40 draws before the next event or the film's end (the start: 153 to 192; off:
#           353 to 392; on: 453 to 492; the cut: 294 to 333).
# Per event and rest it prints "last/count": the draws from the event to the last draw under the rest - 0.02 (0 if none),
# and the count of draws under it, within the window from the event to the next event or the film's end.
import sys
from pathlib import Path
import numpy as np

root, code = Path(sys.argv[1]), (sys.argv[2] if len(sys.argv) > 2 else '1,8,0')
EVENTS = {'start': ('switch', 0, 193, (133, 193), (153, 193)), 'off': ('switch', 193, 393, (133, 193), (353, 393)),
          'on': ('switch', 393, 493, (333, 393), (453, 493)), 'cut': ('cut', 134, 334, (74, 134), (294, 334))}
runs = sorted({p.name.rsplit('-r', 1)[1] if '-r' in p.name.split(code)[-1] else '' for p in root.glob(f'switch-{code}*')})
print(f'{root.name}, code {code}: per event, rest before | rest after, each "last draw under the rest - 0.02 / count of draws under"')
for run in runs:
    sfx = f'-r{run}' if run else ''
    cells = []
    for name, (film, at, end, before, after) in EVENTS.items():
        folder = root / f'{film}-{code}{sfx}'
        if not folder.exists():
            cells.append(f'{name}: n/a')
            continue
        scale = {}
        for line in open(next(folder.glob('size-*.txt'))):
            scale.setdefault(int(line.split()[0]), float(line.split()[3]))
        out = []
        for a, b in (before, after):
            rest = float(np.median([scale[d] for d in range(a, b) if d in scale]))
            under = [d - at for d in range(at, end) if d in scale and scale[d] < rest - 0.02]
            out.append(f'rest {rest:.3f}: {max(under) if under else 0}/{len(under)}')
        cells.append(f'{name}: ' + ' | '.join(out))
    print(f'  run {run or 1}: ' + '; '.join(cells))
