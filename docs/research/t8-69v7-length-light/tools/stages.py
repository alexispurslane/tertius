# stages.py <truth> <variant...>: the stage means of the diffuse signal (TERTIUS_X_STAGES of the scratch probe) of each
# variant, as a share of the truth's, per region of light.py: this draw's sample, the slow history at the draw's end, and
# the shown diffuse light. Each is the mean luminance of the demodulated, pre-exposed light over the counted draws. The
# regions are cut to the smooth pixels of the truth's scene-light long mean (scene.py), read at the render pixel centers.
import sys, numpy as np
from light import REGIONS, mask, smooth_of

STAGES = ("sample", "slow", "shown")

def means(v):
    w, h = (int(x) for x in open(f's/{v}/stages/draws.txt').readline().split()[2:4])
    return np.fromfile(f's/{v}/stages/stages.f32', dtype='<f4').reshape(h, w, 3)

if __name__ == '__main__':
    truth, variants = sys.argv[1], sys.argv[2:]
    t = np.load(f's/{truth}/scene/rect-lamps-long.npy')
    smooth = smooth_of(t / np.percentile(t[t > 0], 99))
    T = means(truth); h, w = T.shape[:2]
    ys = ((2 * np.arange(h) + 1) * 1080 // (2 * h))[:, None]; xs = ((2 * np.arange(w) + 1) * 1920 // (2 * w))[None, :]
    masks = {name: (mask(polys) & smooth)[ys, xs] for name, polys in REGIONS['rect-lamps'].items()}
    for v in variants:
        V = means(v)
        n = open(f's/{v}/stages/draws.txt').read().split()[-3]
        cells = [f"{name} " + " ".join(f"{s} {100 * V[..., k][m].mean() / T[..., k][m].mean():6.1f}%" for k, s in enumerate(STAGES))
                 for name, m in masks.items()]
        print(f"{v:18s} ({n} draws) " + "   ".join(cells))
