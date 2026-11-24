# Tool code of the line orchestrator (t1-yi11), never for src: effect statistics of the region drift test, replayed on the logged
# region lights of t0-qvm1's records. CPU only, nothing is built.
#   x(t)   the region's light of draw t at one anchor (the log's r channel).
#   W      18 draws, the length of each window.
#   d_i    x(t - i) - x(t - i - W) for i = 0 .. W - 1: the 18 differences of draws of the same jitter phase.
#   T<k>   today's statistic z = |mean d| / (s1 * sqrt(2 / W)) above k (T3 is today's test).
#   S<k>   the sign test: at least k of the 18 differences have the same sign.
#   R<a>   Wilcoxon's signed rank test of the 18 differences at the two-sided level a.
# A tested anchor-draw: the anchor's valid draws in a row reach 2 W.
import math
import sys
from pathlib import Path

import numpy as np

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / 't0-qvm1-region-repair-replay' / 'tools'))
import replay  # noqa: E402

W = 18
LOGS = REPO / 't0-qvm1-region-repair-replay' / 'logs'
OLD = REPO / 't0-qvm1-region-off' / 'logs'


def signed_rank_null(n):
    """The exact null distribution of the signed rank sum of n differences: counts[v] of the 2^n sign patterns."""
    counts = np.zeros(n * (n + 1) // 2 + 1)
    counts[0] = 1
    for rank in range(1, n + 1):
        shifted = np.zeros_like(counts)
        shifted[rank:] = counts[:-rank]
        counts = counts + shifted
    return counts / counts.sum()


NULL = signed_rank_null(W)
TOTAL = W * (W + 1) // 2


def signed_rank_bar(level):
    """The largest v such that P(V <= v or V >= TOTAL - v) <= level, or -1."""
    tail = np.cumsum(NULL)
    ok = np.nonzero(2 * tail <= level)[0]
    return int(ok.max()) if len(ok) else -1


def stats(x, g):
    T = len(x)
    r = replay.runs_in_a_row(g)
    tested = r >= 2 * W
    tested[:2 * W - 1] = False
    z = np.zeros_like(x)
    plus = np.zeros_like(x)
    v = np.zeros_like(x)
    for t in range(2 * W - 1, T):
        newer = x[t - W + 1:t + 1]
        older = x[t - 2 * W + 1:t - W + 1]
        d = newer - older
        s1 = older.std(0, ddof=1)
        a = s1 * math.sqrt(2 / W)
        D = d.mean(0)
        with np.errstate(divide='ignore', invalid='ignore'):
            z[t] = np.where(a > 0, np.abs(D) / a, np.where(np.abs(D) > 0, np.inf, 0))
        plus[t] = (d > 0).sum(0)
        ranks = np.abs(d).argsort(0).argsort(0) + 1
        v[t] = np.where(d > 0, ranks, 0).sum(0)
    return tested, z, plus, v


def fires(tested, z, plus, v):
    out = {}
    for k in (3, 4, 5, 6, 8):
        out[f'T{k}'] = tested & (z > k)
    for k in (16, 17, 18):
        out[f'S{k}'] = tested & ((plus >= k) | (plus <= W - k))
    for level in (1e-3, 1e-4, 1e-5):
        bar = signed_rank_bar(level)
        out[f'R{level:g}'] = tested & ((v <= bar) | (v >= TOTAL - bar))
    return out


def autocorrelation(x, lags):
    c = x - x.mean(0)
    var = (c * c).mean(0)
    return {lag: float(np.median((c[lag:] * c[:-lag]).mean(0) / np.where(var > 0, var, 1))) for lag in lags}


def frozen(name, path, first):
    draws, x, g, logged = replay.load(path)
    keep = draws >= first - 2 * W
    x, g = x[keep], g[keep]
    tested, z, plus, v = stats(x, g)
    f = fires(tested, z, plus, v)
    n = tested.sum()
    rng = np.random.default_rng(1)
    shuffled = x.copy()
    for _ in range(1):
        idx = rng.permuted(np.broadcast_to(np.arange(len(x))[:, None, None], x.shape), axis=0)
        shuffled = np.take_along_axis(x, idx, 0)
    ts, zs, ps, vs = stats(shuffled, g)
    fs = fires(ts, zs, ps, vs)
    valid = g.min(0) > 0
    ac = autocorrelation(x[:, valid], (1, 2, 9, 18, 36, 72))
    print(f'{name}: {int(n)} tested anchor-draws from draw {first}; median autocorrelation of x at lags 1, 2, 9, 18, 36, 72: '
          + ', '.join(f'{ac[l]:+.3f}' for l in ac))
    print('  fires per tested anchor-draw, logged order / shuffled order of each anchor\'s draws:')
    for key in f:
        print(f'    {key:8s} {100 * f[key].sum() / n:7.3f} % / {100 * fs[key].sum() / max(ts.sum(), 1):7.3f} %')
    return f, tested


def change(name, path, process=None):
    draws, x, g, logged = replay.load(path, process)
    tested, z, plus, v = stats(x, g)
    f = fires(tested, z, plus, v)
    n = tested.sum()
    print(f'{name}: {int(n)} tested anchor-draws, draws {draws[0]} to {draws[-1]}')
    base = f['T3']
    anchors = base.any(0)
    for key in f:
        share = f[key].sum() / n
        any_fire = f[key].any(0)
        # The first fire per anchor, against today's first fire, over the anchors that today's test fires at.
        first_t = np.where(base.any(0), base.argmax(0), -1)
        first_k = np.where(any_fire, f[key].argmax(0), 10 ** 6)
        both = anchors & any_fire
        delay = (first_k - first_t)[both]
        print(f'    {key:8s} fires {100 * share:6.2f} % of the tested anchor-draws; of the {int(anchors.sum())} anchors that '
              f'today\'s test fires at, {int(both.sum())} fire; their first fire comes '
              f'{np.median(delay) if len(delay) else float("nan"):.0f} draws later (median), '
              f'{np.percentile(delay, 90) if len(delay) else float("nan"):.0f} (90 %)')


if __name__ == '__main__':
    print('signed rank bars at 1e-3, 1e-4, 1e-5:', [signed_rank_bar(l) for l in (1e-3, 1e-4, 1e-5)], 'of', TOTAL)
    frozen('bay, bounce alone', LOGS / 'r2-main-bay-bounce' / 'region-none.txt', 400)
    frozen('exit-cabin, bounce alone', LOGS / 'r2-main-exit-bounce' / 'region-none.txt', 400)
    frozen('still cabin, bounce alone', LOGS / 'r2-main-cabin-bounce' / 'region-none.txt', 400)
    frozen('still cabin, region evidence off, full', LOGS / 'r2-noreg-cabin-full' / 'region-none.txt', 400)
    change('rect-bay film (g-bay, process 3)', OLD / 'g-bay' / 'region-none.txt', 3)
    change('bay-sun film (g-lag, process 1)', OLD / 'g-lag' / 'region-none.txt', 1)
