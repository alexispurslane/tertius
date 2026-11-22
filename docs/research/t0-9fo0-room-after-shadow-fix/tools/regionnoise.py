# Tool code of t0-9fo0, part B. regionnoise.py <log path without extension> <first,last>...: how many of the region test's
# fires noise alone explains, from one per-anchor log of scratch-region-log.patch (read by regionlog.py).
#   1. It replays the test of region_drift.glsl on the logged light of every anchor: at each draw with 36 valid draws, the
#      newer window is the last 18 draws up to this one and the older window the 18 before, and the test fires where
#      |mean(newer) - mean(older)| > 3 * s(older) * sqrt(2 / 18), with s the older window's sample standard deviation.
#      It prints whether the replay gives the logged answer at every tested anchor-draw.
#   2. Per span of draw indices: the share of fires of the replay, counted from the span's 36th draw so that each test sees
#      only the span's draws; the same with each anchor's draws in a random order within each jitter phase (20 orders:
#      the draws of one phase, draw index modulo 18, trade places only with each other); the same in a fully random order
#      (20 orders); and the same on Gaussian noise (20 draws of noise with the span's shape). A random order keeps each
#      anchor's spread of light and removes any trend. The order within the phases also keeps one draw of each phase in
#      every window of 18, so that the jitter cycle still cancels in each window's mean, as the test's window length
#      intends (region_drift.glsl). The fully random order breaks that, which adds the cycle to the difference of the
#      means and makes it fire more often. A test whose spread comes from 18 draws fires on Gaussian noise more often than
#      the 0.27 % of a known spread, because the estimate of the spread scatters.
#   3. Per span, the scatter of each anchor's light from draw to draw (its standard deviation over its mean) as the median
#      and the 10th to 90th percentile over the anchors, and the anchors that fire most, with their share in the real order
#      and in one random order within the phases, and their scatter. The shares count from the span's 36th draw.
#   4. Per span and anchor, the trend of the light: the slope of the 18-draw means over the span, in % per 100 draws, with
#      its t value, and the change from the span's first half to its second half. A span with fewer than 8 such means gets
#      no trend line, because a t value of so few means says little.
# Run it through m.sh.
import sys
import numpy as np
import regionlog

spans = [tuple(int(v) for v in s.split(',')) for s in sys.argv[2:]]
index, light, tested, moved, wide = regionlog.load(sys.argv[1])
CYCLE = 18
rng = np.random.default_rng(1)


def test(series):
    fires = np.zeros(series.shape, dtype=bool)
    for t in range(35, series.shape[0]):
        older, newer = series[t - 35:t - 17], series[t - 17:t + 1]
        s = np.sqrt(np.maximum((older ** 2).sum(0) - older.sum(0) ** 2 / 18, 0) / 17)
        fires[t] = np.abs(newer.mean(0) - older.mean(0)) > 3 * s * np.sqrt(2 / 18)
    return fires


def shuffled(series, phases=None):
    columns = []
    for a in range(series.shape[1]):
        order = np.arange(series.shape[0])
        for group in ([order] if phases is None else [np.where(phases == p)[0] for p in range(CYCLE)]):
            order[group] = rng.permutation(group)
        columns.append(series[order, a])
    return np.stack(columns, axis=1)


replay = test(light)
print(f'replay: {(replay[tested] == moved[tested]).mean() * 100:.3f} % of the {tested.sum()} tested anchor-draws give the logged '
      f'answer; fires logged {(moved & tested).sum()}, replayed {(replay & tested).sum()}')
for first, last in spans:
    rows = (index >= first) & (index <= last)
    series, ok, phases = light[rows], tested[rows][35:], index[rows] % CYCLE
    real = test(series)[35:] & ok
    within = [(test(shuffled(series, phases))[35:] & ok).sum() / ok.sum() for _ in range(20)]
    orders = [(test(shuffled(series))[35:] & ok).sum() / ok.sum() for _ in range(20)]
    noise = [test(rng.normal(1.0, 0.1, size=series.shape))[35:].mean() for _ in range(20)]
    print(f'draws {first} to {last}, tests from draw {first + 35}: real order {100 * real.sum() / ok.sum():.2f} %, random order '
          f'within the phases {100 * np.mean(within):.2f} % (spread {100 * np.std(within):.2f}), fully random order '
          f'{100 * np.mean(orders):.2f} % (spread {100 * np.std(orders):.2f}), Gaussian noise {100 * np.mean(noise):.2f} %')
    per_real = real.sum(0) / np.maximum(ok.sum(0), 1)
    per_order = (test(shuffled(series, phases))[35:] & ok).sum(0) / np.maximum(ok.sum(0), 1)
    k = series.shape[0] // 18
    means = series[:k * 18].reshape(k, 18, -1).mean(axis=1)
    rel = means / np.where(means.mean(axis=0) > 0, means.mean(axis=0), 1) - 1
    t = np.arange(k) * 18.0 - (k - 1) * 9.0
    slope = (t[:, None] * rel).sum(0) / (t ** 2).sum()
    se = np.sqrt(((rel - slope * t[:, None]) ** 2).sum(0) / (k - 2) / (t ** 2).sum())
    halves = means[k // 2:].mean(0) / np.where(means[:k // 2].mean(0) > 0, means[:k // 2].mean(0), 1) - 1
    live = ok.any(axis=0)
    mean = series.mean(0)
    scatter = series.std(0) / np.where(mean > 0, mean, 1)
    print(f'    scatter from draw to draw: median {100 * np.median(scatter[live]):.1f} %, 10th to 90th percentile '
          f'{100 * np.percentile(scatter[live], 10):.1f} to {100 * np.percentile(scatter[live], 90):.1f} %')
    tval = np.where(se > 0, slope / np.where(se > 0, se, 1), 0)
    if k >= 8:
        print(f'    trend over the span: anchors with |t| > 3: {np.mean(np.abs(tval[live]) > 3) * 100:.1f} %, median |half change| '
              f'{100 * np.median(np.abs(halves[live])):.2f} %')
    else:
        print(f'    trend over the span: not measured, the span holds only {k} means of 18 draws')
    for a in np.argsort(per_real)[::-1][:6]:
        print(f'    anchor ({a % wide}, {a // wide}): real {100 * per_real[a]:.1f} %, random order within the phases {100 * per_order[a]:.1f} %, '
              f'slope {100 * 100 * slope[a]:+.2f} % per 100 draws (t {tval[a]:+.1f}), half change {100 * halves[a]:+.2f} %, '
              f'scatter {100 * scatter[a]:.1f} %')
