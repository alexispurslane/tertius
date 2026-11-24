# Tool code of the line orchestrator (t1-yi11), never for src. spread.py: whether the spread of an anchor's light changes
# over time in the frozen part of each still's log. a(t) = the size of the draw's distance from the anchor's median, in
# robust spreads, clipped at 4 so that a rare bright draw does not carry the measure. Its autocorrelation per lag, as the
# median over the anchors and for the 10 % of the anchors with the largest value at lag 1. Then the ratio of the spreads of
# the newer and the older window of 18 draws, logged order against one shuffled order. Then a(t) against the count of
# anchors that the logged test answered yes at in the 30 draws before.
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import effect as E  # noqa: E402

W = 18
for name, log in (('bay, bounce alone', 'r2-main-bay-bounce'), ('exit-cabin, bounce alone', 'r2-main-exit-bounce'),
                  ('still cabin, bounce alone', 'r2-main-cabin-bounce')):
    draws, x, g, logged = E.replay.load(E.LOGS / log / 'region-none.txt')
    keep = draws >= 400
    fired = logged[keep]
    x = x[keep]
    ok = (g[keep].min(0) > 0)
    x = x[:, ok]
    fired_ok = fired[:, ok]
    T = len(x)
    med = np.median(x, 0)
    mad = 1.4826 * np.median(np.abs(x - med), 0)
    a = np.minimum(np.abs(x - med) / mad, 4.0)
    c = a - a.mean(0)
    var = (c * c).mean(0)
    print(f'{name}: {x.shape[1]} anchors, {T} draws')
    first = (c[1:] * c[:-1]).mean(0) / var
    top = first >= np.percentile(first, 90)
    line = '  autocorrelation of the size of the deviation, median over the anchors / mean of the top tenth at lag 1:'
    for lag in (1, 2, 5, 9, 18, 36, 72):
        r = (c[lag:] * c[:-lag]).mean(0) / var
        line += f' {lag}: {np.median(r):+.3f} / {r[top].mean():+.3f},'
    print(line)
    rng = np.random.default_rng(3)

    def ratios(v):
        out = []
        for t in range(2 * W - 1, T, 6):
            newer = v[t - W + 1:t + 1].std(0, ddof=1)
            older = v[t - 2 * W + 1:t - W + 1].std(0, ddof=1)
            out.append(np.log(newer / older))
        return np.abs(np.concatenate(out))

    idx = rng.permuted(np.broadcast_to(np.arange(T)[:, None], x.shape), axis=0)
    real, mixed = ratios(x), ratios(np.take_along_axis(x, idx, 0))
    print(f'  size of log(newer spread / older spread): logged median {np.median(real):.3f}, 90 % {np.percentile(real, 90):.3f}, 99 % {np.percentile(real, 99):.3f}; '
          f'shuffled {np.median(mixed):.3f}, {np.percentile(mixed, 90):.3f}, {np.percentile(mixed, 99):.3f}')
    # The picture-wide count of yes answers in the 30 draws before, against the mean size of the deviation in a draw.
    per_draw = fired.sum((1, 2)).astype(float)
    recent = np.convolve(per_draw, np.ones(30), mode='full')[:T]
    recent = np.concatenate([[0], recent[:-1]])
    size = a.mean(1)
    r = np.corrcoef(recent[60:], size[60:])[0, 1]
    print(f'  correlation over the draws between the yes answers of the 30 draws before (mean {recent[60:].mean():.1f}) and the mean size of the deviation: {r:+.3f}')
