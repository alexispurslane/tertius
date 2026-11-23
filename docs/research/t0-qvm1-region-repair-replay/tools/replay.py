# Tool code of the region repair's replay (t0-qvm1). replay.py <out folder>: the candidate rules of the region drift test,
# replayed on logged region lights, CPU only. Nothing is built. The terms are those of the ticket's note of 2026-11-23:
#   x(t)  the region's light of draw t at one anchor (the log's r channel: the mean luminance of the counted taps of its
#         17 x 17 blocks, over the exposure scale, so it does not depend on the exposure);
#   W     18 draws, the length of each window;
#   P     the jitter's phase count at the log's render size: 18 at the scale 2/3, 25 at 1072 x 603;
#   D(t)  (1 / W) * sum for i from 0 to W - 1 of (x(t - i) - x(t - i - P)): the drift;
#   s1(t) the standard deviation (n - 1) of the older window x(t - P - W + 1) ... x(t - P);
#   a(t)  s1 * sqrt(2 / W), the assumed spread;
#   m(t)  the root of m2, the measured spread: m2 = m2 + (min(D * D, 9 * b * b) - m2) / N at every tested draw, m2 = 0
#         after a restart, b the spread in use at the anchor's tested draw before (a at its first tested draw);
#   bar   3 times the spread in use; cap c(t) the longest history length that the anchor allows in the next draw, 8 to 32.
# An anchor is tested at a draw where its valid draws in a row reach P + W. The candidates:
#   none  no rule: every cap 32;
#   T     today: |D| > 3 a gives a cap of 8 (T18 and T25 name P where the log allows both);
#   E<k>  b = max(a, m) with N = k * (P + W), |D| > 3 b gives 8;
#   C<p>  u = |D| - 3 a, cap n = (p * W * s1 / (sqrt(2) * u)) ^ (2/3) between 8 and 32, 32 where u <= 0;
#   EC<p> u = |D| - 3 b with E's b at N = 8 * (P + W), the same cap;
#   K6    |D| > 3 a at this tested draw and the 5 tested draws before it in a row gives 8.
# The model history of a render block (8 x 8 pixels): its length L grows by 1 per draw to 32 from 1 at the run's first draw,
# and takes the smallest cap of the block's anchors of the draw before (the anchors whose 17 x 17 region holds the block,
# up to 9). Its light y follows the logged light of the block's nearest anchor: y = y + (x - y) / L, at the anchor's valid
# draws. The logs and the cases are listed in CASES below.
import gzip
import json
import math
import sys
from pathlib import Path

import numpy as np

W = 18
STEP = 8          # AnchorStepBlocks, and the region's radius in blocks
FULL = 32.0
FLOOR = 8.0
OLD = Path(__file__).resolve().parents[2] / 't0-qvm1-region-off' / 'logs'
LOGS = Path(__file__).resolve().parents[1] / 'logs'
RUNS = Path('/private/tmp/t0-5tyj/r2/runs')


def floats(txt):
    f32 = txt.with_suffix('.f32')
    if f32.exists():
        return np.fromfile(f32, dtype='<f4')
    with gzip.open(str(f32) + '.gz') as g:
        return np.frombuffer(g.read(), dtype='<f4')


def load(txt, process=None):
    """One process of a region log: draws, x, g (valid draws) and the logged answer, each [draw, row, column] of the ring."""
    lines = [l.split() for l in open(txt)]
    raw = floats(Path(txt))
    blocks, at = [], 0
    for line in lines:
        anchors, width = int(line[2]), int(line[5])
        frame = raw[at:at + 3 * anchors].reshape(anchors // width, width, 3)
        at += 3 * anchors
        if int(line[0]) == 0:
            blocks.append([])
        blocks[-1].append((int(line[0]), frame))
    block = blocks[process - 1] if process else max(blocks, key=len)
    draws = np.array([d for d, _ in block])
    frames = np.stack([f for _, f in block]).astype(np.float64)
    return draws, frames[..., 0], frames[..., 1], frames[..., 2] != 0


def runs_in_a_row(g):
    """The anchor's valid draws in a row, counted past the ring's 36."""
    r = np.zeros_like(g)
    r[0] = g[0]
    for t in range(1, len(g)):
        grows = (g[t] > 0) & ((g[t] == g[t - 1] + 1) | ((g[t] == 2 * W) & (g[t - 1] == 2 * W)))
        r[t] = np.where(grows, r[t - 1] + 1, g[t])
    return r


def statistics(x, g, P):
    """Per draw and anchor: tested, D, s1 and a."""
    T = len(x)
    r = runs_in_a_row(g)
    tested = r >= P + W
    D = np.zeros_like(x)
    s1 = np.zeros_like(x)
    for t in range(P + W - 1, T):
        newer = x[t - W + 1:t + 1]
        older = x[t - P - W + 1:t - P + 1]
        D[t] = newer.mean(0) - older.mean(0)
        s1[t] = older.std(0, ddof=1)
    tested[:P + W - 1] = False
    return tested, D, s1, s1 * math.sqrt(2 / W)


def measured(tested, D, a, N):
    """E's spread in use b and its measured spread m per draw and anchor (m after this draw's update)."""
    m2 = np.zeros(D.shape[1:])
    before = np.full(D.shape[1:], np.nan)
    b = np.zeros_like(D)
    m = np.zeros_like(D)
    for t in range(len(D)):
        on = tested[t]
        m2 = np.where(on, m2, 0.0)
        before = np.where(on, before, np.nan)
        b[t] = np.where(on, np.maximum(a[t], np.sqrt(m2)), 0.0)
        clip = np.where(np.isnan(before), a[t], before)
        m2 = np.where(on, m2 + (np.minimum(D[t] ** 2, 9 * clip ** 2) - m2) / N, m2)
        before = np.where(on, b[t], before)
        m[t] = np.sqrt(m2)
    return b, m


def continuous(D, s1, spread, p):
    u = np.abs(D) - 3 * spread
    with np.errstate(divide='ignore', invalid='ignore'):
        n = (p * W * s1 / (math.sqrt(2) * u)) ** (2 / 3)
    return np.where(u > 0, np.clip(np.nan_to_num(n, nan=FLOOR), FLOOR, FULL), FULL)


def candidates(x, g, P, ps=(1, 2, 4), extra_p=()):
    """Each candidate's caps [draw, row, column], with T's answers and the statistics for the measures."""
    tested, D, s1, a = statistics(x, g, P)
    stat = np.where(a > 0, np.abs(D) / np.where(a > 0, a, 1), np.where(np.abs(D) > 0, np.inf, 0))
    today = tested & (stat > 3)
    caps = {'none': np.full_like(x, FULL), 'T': np.where(today, FLOOR, FULL)}
    spans, measures = {}, {}
    for k in (4, 8, 16):
        b, m = measured(tested, D, a, k * (P + W))
        spans[k], measures[k] = b, m
        caps[f'E{k}'] = np.where(tested & (np.abs(D) > 3 * b), FLOOR, FULL)
    for p in (*ps, *extra_p):
        caps[f'C{p:g}'] = np.where(tested, continuous(D, s1, a, p), FULL)
        caps[f'EC{p:g}'] = np.where(tested, continuous(D, s1, spans[8], p), FULL)
    run = np.zeros(x.shape[1:])
    k6 = np.zeros_like(today)
    for t in range(len(x)):
        run = np.where(today[t], run + 1, 0)
        k6[t] = run >= 6
    caps['K6'] = np.where(k6, FLOOR, FULL)
    return caps, dict(tested=tested, D=D, s1=s1, a=a, stat=stat, today=today, spans=spans, measures=measures)


def block_grid(width, height, rows, columns):
    """For a render size: per block its up to 9 anchors (flat ring indices, -1 for none) and its nearest anchor."""
    bx, by = (width + 7) // 8, (height + 7) // 8
    last = ((bx - 1) // STEP, (by - 1) // STEP)
    ys, xs = np.mgrid[0:by, 0:bx]
    near = np.clip(np.rint(ys / STEP), 0, last[1]).astype(int) * columns + np.clip(np.rint(xs / STEP), 0, last[0]).astype(int)
    holders = np.full((by, bx, 9), -1)
    first_x = np.maximum((xs - STEP + STEP - 1) // STEP, 0)
    first_y = np.maximum((ys - STEP + STEP - 1) // STEP, 0)
    for c in range(9):
        ax, ay = first_x + c % 3, first_y + c // 3
        ok = (ax <= np.minimum((xs + STEP) // STEP, last[0])) & (ay <= np.minimum((ys + STEP) // STEP, last[1]))
        holders[..., c] = np.where(ok, ay * columns + ax, -1)
    return holders, near


def model(caps, x, g, holders, near):
    """The model history of every block: L and y per draw [draw, block row, block column]."""
    T = len(caps)
    flat_caps = caps.reshape(T, -1)
    flat_x = x.reshape(T, -1)
    flat_g = g.reshape(T, -1)
    padded = np.concatenate([flat_caps, np.full((T, 1), FULL)], 1)       # index -1 reads 32
    L = np.zeros((T, *near.shape))
    y = np.zeros((T, *near.shape))
    length = np.ones(near.shape)
    light = flat_x[0][near]
    seen = flat_g[0][near] > 0
    for t in range(T):
        if t > 0:
            cap = padded[t - 1][holders].min(-1)
            length = np.minimum(np.minimum(length + 1, FULL), cap)
        valid = flat_g[t][near] > 0
        target = flat_x[t][near]
        light = np.where(valid & ~seen, target, light)
        seen |= valid
        light = np.where(valid, light + (target - light) / length, light)
        L[t], y[t] = length, light
    return L, y


def write(out, name, payload):
    (out / f'{name}.json').write_text(json.dumps(payload, indent=1))


if __name__ == '__main__':
    print('replay.py is driven by measure.py')
