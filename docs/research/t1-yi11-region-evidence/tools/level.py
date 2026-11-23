# Tool code of t1-yi11. level.py <reference column> <column>...: the frozen bay's light level per region, from the long
# takes of gtakes.sh (1,660 draws, every 50th draw from 660 to 1,610 kept): bay-<column>-long-dif (the diffuse signal
# alone) and bay-<column>-long (the full picture), in /private/tmp/t1-yi11/runs.
# The unit: the mean display-linear luminance of the region (the 8-bit sRGB PNG decoded to linear light, Rec. 709
# weights) at the takes' one fixed exposure of -4.089 stops. For each column and region it prints:
#   - level: the mean over the 20 captures, divided by the reference column's;
#   - first and last: the first capture's (draw 660) and the last capture's (draw 1,610) region mean, divided by the
#     column's own mean over the 20 captures, which says how settled the take is;
#   - spread: the standard deviation of the 20 captures' region means, divided by their mean. The captures lie 50 draws
#     apart, closer than a history of 128 draws, so they are not independent.
# The regions are the back-face line's boxes (bay.py). It writes level.json beside the record.
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

R = Path('/private/tmp/t1-yi11/runs')
W = np.array([0.2126, 0.7152, 0.0722])
BOXES = {'sunlit patch': (100, 40, 420, 360), 'back wall': (560, 420, 980, 560), 'floor': (600, 650, 1200, 950),
         'right wall': (1300, 100, 1800, 500)}
reference, *columns = sys.argv[1:]


def linear(path):
    a = np.asarray(Image.open(path).convert('RGB'), np.float64) / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ W


def means(run):
    """The region means of each kept capture of a take, captures by regions."""
    frames = sorted((R / run).glob('f-*.png'))
    rows = []
    for path in frames:
        lum = linear(path)
        rows.append([lum[y0:y1, x0:x1].mean() for x0, y0, x1, y1 in BOXES.values()])
    return np.array(rows)


out = {}
for kind, suffix in (('diffuse alone', '-long-dif'), ('full picture', '-long')):
    if not (R / f'bay-{reference}{suffix}').exists():
        continue
    ref = means(f'bay-{reference}{suffix}').mean(axis=0)
    print(f'\n{kind}: level against column {reference} / first, last against the take\'s own mean / spread')
    print('| column | ' + ' | '.join(BOXES) + ' |')
    print('|---|' + '---|' * len(BOXES))
    for col in [reference, *columns]:
        if not (R / f'bay-{col}{suffix}').exists():
            print(f'| {col} | ' + ' | '.join('no take' for _ in BOXES) + ' |')
            continue
        m = means(f'bay-{col}{suffix}')
        mean = m.mean(axis=0)
        cells = [f'{mean[i] / ref[i]:.4f} ({m[0, i] / mean[i]:.4f}, {m[-1, i] / mean[i]:.4f}, {m[:, i].std() / mean[i]:.4f})'
                 for i in range(len(BOXES))]
        print(f'| {col} | ' + ' | '.join(cells) + ' |')
        out.setdefault(kind, {})[col] = {name: {'level': float(mean[i] / ref[i]), 'mean': float(mean[i]),
                                                'first': float(m[0, i] / mean[i]), 'last': float(m[-1, i] / mean[i]),
                                                'spread': float(m[:, i].std() / mean[i])}
                                         for i, name in enumerate(BOXES)}
(Path(__file__).resolve().parent.parent / 'level.json').write_text(json.dumps({'reference': reference, **out}, indent=1))
