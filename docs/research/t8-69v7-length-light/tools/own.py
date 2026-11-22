# own.py <variant...>: for each variant, the stage means against its own sample mean, per region: the slow history over
# the sample, and the shown light over the sample. In a still whose steps are all linear, both are 100 %.
import sys, numpy as np
from light import REGIONS, mask, smooth_of
from stages import means

for v in sys.argv[1:]:
    t = np.load(f's/{v}/scene/rect-lamps-long.npy')
    smooth = smooth_of(t / np.percentile(t[t > 0], 99))
    V = means(v); h, w = V.shape[:2]
    ys = ((2 * np.arange(h) + 1) * 1080 // (2 * h))[:, None]; xs = ((2 * np.arange(w) + 1) * 1920 // (2 * w))[None, :]
    cells = []
    for name, polys in REGIONS['rect-lamps'].items():
        m = (mask(polys) & smooth)[ys, xs]
        s = V[..., 0][m].mean()
        cells.append(f"{name} slow/sample {100 * V[..., 1][m].mean() / s:6.1f}% shown/sample {100 * V[..., 2][m].mean() / s:6.1f}%")
    print(f"{v:18s} " + "   ".join(cells))
