# Tool code of the line orchestrator (t1-yi11), never for src. ratio.py: the variance ratio of the region's light at the
# test's own scale, in the frozen part of each still's log. For windows of n draws that do not overlap: n times the variance
# of the window means over the variance of the draws, as the mean over the anchors. White noise gives 1. Above 1, the means
# of two windows differ by more than the spread inside a window says, and today's test answers yes more often than its bar
# claims. Under 1 it answers less often. With 40 shuffled orders of the whole picture as the white reference. The light is
# clipped at 4 robust spreads from the median, so that rare bright draws do not carry the ratio, and given unclipped too.
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import effect as E  # noqa: E402


def ratio(v, n):
    T = len(v) // n * n
    means = v[:T].reshape(-1, n, v.shape[1]).mean(1)
    return float((n * means.var(0, ddof=1) / v[:T].var(0, ddof=1)).mean())


for name, log in (('bay, bounce alone', 'r2-main-bay-bounce'), ('exit-cabin, bounce alone', 'r2-main-exit-bounce'),
                  ('still cabin, bounce alone', 'r2-main-cabin-bounce')):
    draws, x, g, _ = E.replay.load(E.LOGS / log / 'region-none.txt')
    keep = draws >= 400
    x = x[keep][:, g[keep].min(0) > 0]
    med = np.median(x, 0)
    mad = 1.4826 * np.median(np.abs(x - med), 0)
    u = np.clip((x - med) / mad, -4, 4)
    rng = np.random.default_rng(5)
    print(name)
    for label, v in (('clipped', u), ('as logged', x)):
        for n in (6, 18, 36):
            real = ratio(v, n)
            mixed = np.sort([ratio(v[rng.permutation(len(v))], n) for _ in range(40)])
            print(f'  {label:9s} windows of {n:2d} draws: {real:.3f}; 40 shuffled orders: median {np.median(mixed):.3f}, '
                  f'least {mixed[0]:.3f}, most {mixed[-1]:.3f}')
