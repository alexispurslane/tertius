# Tool code of t0-9fo0, part B. gridedge.py <M run> <X0 run> <X run>: a number for "does a straight edge show along the
# anchors' grid". For each run and each grid-sheet frame (images/grid/cabin-<frame>.png, from gridfilm.py), it takes the
# log2 scene luminance (survey.probe_luminance), smooths it by 9 output pixels along the line under test, and takes the
# step |log2 y(x) - log2 y(x - 1)| across vertical lines (and the same across horizontal lines), over the visible pixels
# away from the lamps on both sides. It averages the step per phase of the 96-pixel grid (x mod 96) and prints the step
# at the grid's phase 0 over the median step of the other phases. A ratio near 1 means no edge on the grid. Run it
# through m.sh.
import importlib.util
import sys
from pathlib import Path
import numpy as np
from scipy.ndimage import uniform_filter1d

spec = importlib.util.spec_from_file_location('survey', Path(__file__).resolve().parents[4] / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
STEP = 96
runs = dict(zip(('M', 'X0', 'X'), sys.argv[1:4]))
ref = survey.probe_luminance(Path(f's/{runs["X0"]}/images/grid/cabin-420.png'))
ok = survey.visible(ref) & ~survey.near_emitters(ref)


def ratio(lg, mask, axis):
    s = uniform_filter1d(lg, 9, axis=1 - axis)
    d = np.abs(np.diff(s, axis=axis))
    m = mask[:, 1:] & mask[:, :-1] if axis == 1 else mask[1:] & mask[:-1]
    n = d.shape[axis]
    phase = (np.arange(1, n + 1)) % STEP
    per = np.array([d.take(np.where(phase == p)[0], axis=axis)[m.take(np.where(phase == p)[0], axis=axis)].mean()
                    for p in range(STEP)])
    return per[0] / np.median(per[1:])


print('run frame  vertical-lines  horizontal-lines   (step at the grid over the median step elsewhere)')
for k in (159, 180, 220, 380, 420):
    for s, r in runs.items():
        lg = np.log2(np.maximum(survey.probe_luminance(Path(f's/{r}/images/grid/cabin-{k:03d}.png')), 1e-6))
        print(f'{s:3} {k:5}  {ratio(lg, ok, 1):14.3f}  {ratio(lg, ok, 0):16.3f}')
