# Tool code of the line orchestrator (t1-yi11), never for src. cluster.py: whether an anchor's bright draws come close
# together in time, in the frozen part of each still's log. A bright draw is one more than `bar` robust spreads above the
# anchor's median. Observed pairs of bright draws `lag` draws apart at one anchor, against the count that the anchor's own
# rate gives for independent draws (n * n / T, summed over the anchors). Then the excursions of today's statistic above a
# bar (runs of tested draws in a row at one anchor), in the logged order against 20 shuffled orders.
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import effect as E  # noqa: E402


def excursions(hit):
    start = hit.copy()
    start[1:] &= ~hit[:-1]
    return int(start.sum())


for name, log in (('bay, bounce alone', 'r2-main-bay-bounce'), ('exit-cabin, bounce alone', 'r2-main-exit-bounce'),
                  ('still cabin, bounce alone', 'r2-main-cabin-bounce')):
    draws, x, g, _ = E.replay.load(E.LOGS / log / 'region-none.txt')
    keep = draws >= 400 - 36
    x, g = x[keep], g[keep]
    T = len(x)
    med = np.median(x, 0)
    mad = 1.4826 * np.median(np.abs(x - med), 0)
    with np.errstate(divide='ignore', invalid='ignore'):
        u = np.where(mad > 0, (x - med) / mad, 0)
    print(name)
    for bar in (3, 5):
        high = u > bar
        n = high.sum(0).astype(float)
        line = f'  bright draws above {bar} robust spreads: {int(high.sum())}; pairs observed / expected at the lag'
        for lag in (1, 2, 3, 5, 9, 18, 36):
            seen = (high[lag:] & high[:-lag]).sum()
            expected = (n * n / T).sum() * (T - lag) / T
            line += f' {lag}: {int(seen)} / {expected:.1f},'
        print(line)
    tested, z, plus, v = E.stats(x, g)
    rng = np.random.default_rng(7)
    shuffled = {bar: [] for bar in (4, 5, 6)}
    ranks = []
    for _ in range(20):
        idx = rng.permuted(np.broadcast_to(np.arange(T)[:, None, None], x.shape), axis=0)
        ts, zs, ps, vs = E.stats(np.take_along_axis(x, idx, 0), g)
        for bar in shuffled:
            shuffled[bar].append(excursions(ts & (zs > bar)))
        ranks.append(excursions(ts & ((vs <= 5) | (vs >= 166))))
    for bar in shuffled:
        s = np.array(shuffled[bar])
        print(f'  excursions of today\'s statistic above {bar}: logged {excursions(tested & (z > bar))}, 20 shuffled orders: '
              f'mean {s.mean():.1f}, least {s.min()}, most {s.max()}')
    s = np.array(ranks)
    print(f'  excursions of the signed rank test (level 7.6e-5): logged {excursions(tested & ((v <= 5) | (v >= 166)))}, '
          f'20 shuffled orders: mean {s.mean():.1f}, least {s.min()}, most {s.max()}; '
          f'expected at the level for independent tests: {7.6e-5 * tested.sum():.1f} answers')
