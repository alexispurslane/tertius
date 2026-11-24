# Tool code of the line orchestrator (t1-yi11), never for src. order2.py: the fair form of the order check. effect.py and
# cluster.py shuffled each anchor's draws on their own, which also breaks the tie between neighbor anchors (their regions
# share half of their taps, so one bright draw is bright at several anchors at once). Here one shuffled order of the draws
# serves the whole picture, 40 times. Counted: the tested anchor-draws above each bar of today's statistic, and of the signed
# rank test at the level 7.6e-5, in the frozen part of each still's log.
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import effect as E  # noqa: E402

for name, log in (('bay, bounce alone', 'r2-main-bay-bounce'), ('exit-cabin, bounce alone', 'r2-main-exit-bounce'),
                  ('still cabin, bounce alone', 'r2-main-cabin-bounce')):
    draws, x, g, _ = E.replay.load(E.LOGS / log / 'region-none.txt')
    keep = draws >= 400 - 36
    x, g = x[keep], g[keep]
    T = len(x)

    def counts(v):
        tested, z, plus, rank = E.stats(v, g)
        out = [int((tested & (z > bar)).sum()) for bar in (3, 4, 5, 6)]
        out.append(int((tested & ((rank <= 5) | (rank >= 166))).sum()))
        return out

    real = counts(x)
    rng = np.random.default_rng(11)
    mixed = np.array([counts(x[rng.permutation(T)]) for _ in range(40)])
    print(name)
    for k, label in enumerate(('above 3', 'above 4', 'above 5', 'above 6', 'signed rank')):
        s = np.sort(mixed[:, k])
        place = int((mixed[:, k] >= real[k]).sum())
        print(f'  {label:11s} logged {real[k]:5d}; 40 shuffled orders of the whole picture: median {int(np.median(s))}, '
              f'least {s[0]}, most {s[-1]}; shuffles at or above the logged count: {place} of 40')
