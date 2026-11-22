# scenelight.py <truth> <variant...>: light.py's measure in scene light, from scene.py's runs. For each region of
# light.py, cut to the smooth pixels of the truth's long mean, and for the whole image, the shown still and the long
# mean of each variant as a share of the truth's long mean. No tone curve lies between the renderer and these means.
import sys, numpy as np
from light import REGIONS, mask, smooth_of

truth, variants = sys.argv[1], sys.argv[2:]
for case, regions in REGIONS.items():
    try:
        t = np.load(f's/{truth}/scene/{case}-long.npy')
    except FileNotFoundError:
        continue
    smooth = smooth_of(t / np.percentile(t[t > 0], 99))
    for v in variants:
        try:
            s = np.load(f's/{v}/scene/{case}-shown.npy'); r = np.load(f's/{v}/scene/{case}-long.npy')
        except FileNotFoundError:
            continue
        cells = [f"{name} shown {100 * s[m].mean() / t[m].mean():6.1f}% settled {100 * r[m].mean() / t[m].mean():6.1f}%"
                 for name, m in ((name, mask(polys) & smooth) for name, polys in regions.items())]
        cells.append(f"whole image {100 * r.mean() / t.mean():6.1f}%")
        print(f"{case:12s} {v:16s} " + "   ".join(cells))
