# Tool code of t0-9fo0, part B. gridedge.py <M run> <X0 run> <X run>: a number for "does a straight edge show along the
# anchors' grid". The grid's lines lie at output pixels 96 i and 96 i + 12 in x and in y (gridsheet.py's header says
# why). For each grid-sheet frame (images/grid/cabin-<frame>.png, from gridfilm.py), it takes the log scene luminance
# (survey.probe_luminance) of M, X0 and X, and the logs of the ratios X / X0 and M / X0. It smooths each by 9 output pixels
# along the line under test and takes the step |v(x) - v(x - 1)| across vertical lines (and the same across horizontal
# lines), over the visible pixels away from the lamps on both sides. It averages the step per phase of the 96-pixel
# period (x mod 96), and prints the step at phase 0 and at phase 12 over the median step of the other phases. A ratio near
# 1 means no edge on the grid. The room's own edges move a side's own ratio. The ratio X / X0 removes the room, so an edge
# that the remedy adds would show there and not in M / X0 (M has no region test). Run it through m.sh.
import importlib.util
import sys
from pathlib import Path
import numpy as np
from scipy.ndimage import uniform_filter1d

spec = importlib.util.spec_from_file_location('survey', Path(__file__).resolve().parents[4] / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
STEP, LINES = 96, (0, 12)
runs = dict(zip(('M', 'X0', 'X'), sys.argv[1:4]))
ref = survey.probe_luminance(Path(f's/{runs["X0"]}/images/grid/cabin-420.png'))
ok = survey.visible(ref) & ~survey.near_emitters(ref)


def ratios(v, axis):
    s = uniform_filter1d(v, 9, axis=1 - axis)
    d = np.abs(np.diff(s, axis=axis))
    m = ok[:, 1:] & ok[:, :-1] if axis == 1 else ok[1:] & ok[:-1]
    phase = np.arange(1, d.shape[axis] + 1) % STEP
    per = np.array([d.take(np.where(phase == p)[0], axis=axis)[m.take(np.where(phase == p)[0], axis=axis)].mean()
                    for p in range(STEP)])
    rest = np.median(np.delete(per, LINES))
    return [per[p] / rest for p in LINES]


print('series  frame   vertical lines, phase 0 / 12   horizontal lines, phase 0 / 12   (step there over the median step)')
for k in (159, 180, 220, 380, 420):
    lg = {s: np.log(np.maximum(survey.probe_luminance(Path(f's/{r}/images/grid/cabin-{k:03d}.png')), 1e-6))
          for s, r in runs.items()}
    series = {**lg, 'X/X0': lg['X'] - lg['X0'], 'M/X0': lg['M'] - lg['X0']}
    for s, v in series.items():
        a, b = ratios(v, 1), ratios(v, 0)
        print(f'{s:6} {k:5}   {a[0]:12.3f} / {a[1]:.3f}   {b[0]:20.3f} / {b[1]:.3f}')
