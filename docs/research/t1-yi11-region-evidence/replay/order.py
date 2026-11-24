# Tool code of the line orchestrator (t1-yi11), never for src. order.py: where and when the frozen bay's log answers yes at
# a high bar, to explain why the logged order of its draws answers more often than a shuffled order.
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import effect as E  # noqa: E402

for name, log in (('bay, bounce alone', 'r2-main-bay-bounce'), ('bay, full picture', 'r2-main-bay-full'),
                  ('exit-cabin, bounce alone', 'r2-main-exit-bounce'), ('still cabin, bounce alone', 'r2-main-cabin-bounce')):
    draws, x, g, logged = E.replay.load(E.LOGS / log / 'region-none.txt')
    tested, z, plus, v = E.stats(x, g)
    late = draws >= 400
    print(f'{name}: draws {draws[0]} to {draws[-1]}, {x.shape[1]} x {x.shape[2]} anchors')
    for bar in (4, 5, 6):
        hit = tested & (z > bar) & late[:, None, None]
        per_anchor = hit.sum(0)
        per_draw = hit.sum((1, 2))
        order = np.argsort(per_anchor.ravel())[::-1][:6]
        top = ', '.join(f'({i // x.shape[2]},{i % x.shape[2]}): {per_anchor.ravel()[i]}' for i in order if per_anchor.ravel()[i])
        busy = np.nonzero(per_draw)[0]
        # Runs of draws in a row with at least one answer.
        runs = np.split(busy, np.nonzero(np.diff(busy) > 1)[0] + 1) if len(busy) else []
        spans = ', '.join(f'{draws[r[0]]}-{draws[r[-1]]} ({per_draw[r].sum()})' for r in runs[:14])
        print(f'  above {bar}: {int(hit.sum())} anchor-draws at {int((per_anchor > 0).sum())} anchors; the busiest (row,column): count: {top}')
        print(f'    draws with an answer, as runs (answers): {spans}')
    # The level of each anchor's light over the frozen part: the mean of each 33 draws against the mean of all.
    xs = x[late]
    blocks = xs[:len(xs) // 33 * 33].reshape(-1, 33, *x.shape[1:]).mean(1)
    rel = blocks / blocks.mean(0) - 1
    spread = xs.std(0) / np.sqrt(33) / xs.mean(0)
    with np.errstate(divide='ignore', invalid='ignore'):
        over = np.abs(rel) / spread
    print(f'    means of 33 draws against the anchor\'s whole mean, in standard errors of such a mean: median {np.nanmedian(over):.2f}, '
          f'90 % {np.nanpercentile(over, 90):.2f}, largest {np.nanmax(over):.2f} (white noise: 0.67, 1.64, about 4)')
    half = len(xs) // 2
    drift = xs[half:].mean(0) / xs[:half].mean(0) - 1
    se = xs.std(0) / np.sqrt(half) * np.sqrt(2) / xs.mean(0)
    print(f'    second half against first half of the frozen part: median {100 * np.nanmedian(drift):+.3f} %, '
          f'in standard errors: median size {np.nanmedian(np.abs(drift) / se):.2f}, largest {np.nanmax(np.abs(drift) / se):.2f}')
