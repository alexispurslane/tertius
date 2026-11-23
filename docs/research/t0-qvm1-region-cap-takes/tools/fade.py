# Tool code of the region cap's takes (t0-qvm1). fade.py: the mean display-linear light of the floor, the ceiling and the
# left wall of the still cabin's frozen film (film.py still-cabin, draws 0 to 300, +3.83 stops) per draw, relative to the
# mean of draws 280 to 300, on main (pm) and the cap (pc). Run it in the dev shell.
import numpy as np
from pathlib import Path
from PIL import Image
F = Path('/private/tmp/t0-qvm1c/films')
def lin(a):
    a = a / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
regions = {'floor': (slice(820, 1060), slice(300, 1620)), 'ceiling': (slice(40, 200), slice(300, 1620)), 'left wall': (slice(250, 600), slice(60, 300))}
res = {}
for b in ('pm', 'pc'):
    files = sorted((F / f'{b}-still-cabin').glob('*.png'))
    series = {k: [] for k in regions}
    for f in files:
        a = np.asarray(Image.open(f).convert('L'), np.float32)
        y = lin(a)
        for k, (ry, rx) in regions.items():
            series[k].append(y[ry, rx].mean())
    res[b] = {k: np.array(v) for k, v in series.items()}
for k in regions:
    for b in ('pm', 'pc'):
        s = res[b][k]; last = s[280:].mean()
        pts = ' '.join(f'{d}:{100*(s[d]/last-1):+.2f}' for d in (20, 40, 60, 90, 130, 160, 200, 250))
        print(f'{k:9s} {b}: relative to draws 280-300 (%): {pts}')
np.save('/private/tmp/t0-qvm1c/pics/cabin-fade.npy', {b: {k: v for k, v in res[b].items()} for b in res}, allow_pickle=True)
