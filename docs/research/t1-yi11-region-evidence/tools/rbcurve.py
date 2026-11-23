# Tool code of t1-yi11. rbcurve.py [column...]: the brightness of rect-bay's films (yifilm.py, /private/tmp/t1-yi11/films/
# rb-<column>) per draw, as the mean 8-bit gray of the whole picture and of three boxes of output pixels chosen by eye on
# draw 90 of column a: the left wall (x 0 to 220, y 150 to 1000), the floor (x 330 to 1100, y 520 to 1000), the back wall
# (x 230 to 660, y 330 to 440, below the slot). It prints the gray at every 15th draw and writes rect-bay-curve.json.
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

F = Path('/private/tmp/t1-yi11/films')
columns = sys.argv[1:] or ['a', 'b', 'c', 'd']
BOXES = {'whole': (0, 0, 1920, 1080), 'left wall': (0, 150, 220, 1000), 'floor': (330, 520, 1100, 1000),
         'back wall': (230, 330, 660, 440)}
curves = {}
for col in columns:
    grays = [np.asarray(Image.open(p).convert('L'), np.float64) for p in sorted((F / f'rb-{col}').glob('*.png'))]
    curves[col] = {name: [float(g[y0:y1, x0:x1].mean()) for g in grays] for name, (x0, y0, x1, y1) in BOXES.items()}
(Path(__file__).resolve().parent.parent / 'rect-bay-curve.json').write_text(json.dumps(curves))
draws = list(range(0, 180, 15)) + [179]
for name in BOXES:
    print(f'\n{name}: mean 8-bit gray at draw')
    print('| column | ' + ' | '.join(str(d) for d in draws) + ' |')
    print('|---|' + '---:|' * len(draws))
    for col in columns:
        print(f'| {col} | ' + ' | '.join(f'{curves[col][name][d]:.1f}' for d in draws) + ' |')
