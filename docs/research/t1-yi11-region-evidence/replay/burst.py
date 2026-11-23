# Tool code of the line orchestrator (t1-yi11), never for src. burst.py: the region's light around the frozen stills' answers
# above 6 standard deviations, as each draw's distance from the anchor's median in units of its robust spread
# (1.4826 times the median absolute deviation over the frozen part).
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import effect as E  # noqa: E402

CASES = (('still cabin, bounce alone', 'r2-main-cabin-bounce', ((5, 16, 690), (6, 15, 690))),
         ('bay, bounce alone', 'r2-main-bay-bounce', ((5, 4, 722), (8, 6, 556), (10, 7, 740), (1, 17, 587))))
for name, log, places in CASES:
    draws, x, g, _ = E.replay.load(E.LOGS / log / 'region-none.txt')
    tested, z, plus, v = E.stats(x, g)
    late = draws >= 400
    med = np.median(x[late], 0)
    mad = 1.4826 * np.median(np.abs(x[late] - med), 0)
    u = (x - med) / mad
    print(name)
    for row, column, first in places:
        at = int(np.nonzero(draws == first)[0][0])
        series = u[at - 22:at + 20, row, column]
        zs = z[at - 22:at + 20, row, column]
        print(f'  anchor ({row},{column}), draws {first - 22} to {first + 19}: light in robust spreads from the median:')
        print('    ' + ' '.join(f'{s:+.1f}' for s in series))
        print('    statistic: ' + ' '.join(f'{s:.1f}' for s in zs))
    # How often a draw far above the median is followed by another one: bursts.
    high = (u > 3) & late[:, None, None]
    pairs = (high[1:] & high[:-1]).sum()
    n = high.sum()
    rate = n / (late.sum() * x.shape[1] * x.shape[2])
    print(f'  draws more than 3 robust spreads above the median: {int(n)} ({100 * rate:.3f} % of the anchor-draws; a normal law gives 0.135 %); '
          f'followed at once by another: {int(pairs)} ({100 * pairs / max(n, 1):.1f} %; independent draws give {100 * rate:.3f} %)')
    for lag in (1, 2, 3, 5, 9, 18):
        both = (high[lag:] & high[:-lag]).sum()
        print(f'    a second such draw {lag} draws later: {100 * both / max(n, 1):.1f} %')
    # Shared between neighbor anchors in the same draw.
    side = (high[:, :, 1:] & high[:, :, :-1]).sum()
    print(f'    the right neighbor anchor is high in the same draw: {100 * side / max(n, 1):.1f} %')
