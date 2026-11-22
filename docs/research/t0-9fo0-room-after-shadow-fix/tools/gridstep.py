# Tool code of t0-9fo0, part B. gridstep.py <M run> <X0 run> <X run>: the light across the lines of the anchors' grid at
# y = 288 and y = 300 in the grid sheet's crop (output pixels 0 to 480 across), at film frames 220 and 420 of
# gridfilm.py's frames. Per run and per stretch of 96 pixels along the lines, it prints the mean log scene luminance of
# each row from 276 to 311, as 100 times the natural log against row 287 (the last row above the first line), which is
# the change in % for small changes, and the same for X / X0. A step of the remedy at a line would show as a jump between
# rows 287 and 288 or between rows 299 and 300 that the rows beside them do not have. Run it through m.sh.
import importlib.util
import sys
from pathlib import Path
import numpy as np

spec = importlib.util.spec_from_file_location('survey', Path(__file__).resolve().parents[4] / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
runs = dict(zip(('M', 'X0', 'X'), sys.argv[1:4]))
for k in (220, 420):
    y = {s: survey.probe_luminance(Path(f's/{r}/images/grid/cabin-{k}.png')) for s, r in runs.items()}
    for x0, x1 in ((96, 192), (192, 288), (288, 384), (384, 480)):
        print(k, f'x {x0}-{x1}', 'rows 276..311, 100 ln(luminance) per row, minus row 287:')
        for s in y:
            prof = np.log(y[s][276:312, x0:x1]).mean(axis=1)
            print(f'  {s:3}', ' '.join(f'{100*(v - prof[11]):+5.1f}' for v in prof))
        r = np.log(y['X'][276:312, x0:x1] / y['X0'][276:312, x0:x1]).mean(axis=1)
        print('  X/X0', ' '.join(f'{100*(v - r[11]):+5.1f}' for v in r))
