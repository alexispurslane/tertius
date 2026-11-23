# Tool code of the line orchestrator (t1-yi11), never for src: the effect candidates of the region drift test with replay.py's
# block model at a cap of 128 draws. CPU only. The model history of a block: its length L grows by 1 per draw to the cap and
# takes the smallest cap of its anchors of the draw before; its light y follows the nearest anchor's logged light at 1 / L.
import math
import sys
from pathlib import Path

import numpy as np

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / 't0-qvm1-region-repair-replay' / 'tools'))
import replay  # noqa: E402
sys.path.insert(0, str(Path(__file__).resolve().parent))
import effect as E  # noqa: E402

W = 18
LOGS = REPO / 't0-qvm1-region-repair-replay' / 'logs'
OLD = REPO / 't0-qvm1-region-off' / 'logs'
WIDTH, HEIGHT = 1280, 720


def caps_of(x, g, cap):
    replay.FULL = float(cap)
    caps, info = replay.candidates(x, g, W, ps=(1,))
    tested, stat, D, s1, a = info['tested'], info['stat'], info['D'], info['s1'], info['a']
    out = {k: caps[k] for k in ('none', 'T', 'C1', 'K6', 'CK3')}
    for k in (4, 5):
        out[f'T{k}'] = np.where(tested & (stat > k), replay.FLOOR, cap)
        u = np.abs(D) - k * a
        with np.errstate(divide='ignore', invalid='ignore'):
            n = (W * s1 / (math.sqrt(2) * u)) ** (2 / 3)
        out[f'C1 at {k}'] = np.where(tested & (u > 0), np.clip(np.nan_to_num(n, nan=replay.FLOOR), replay.FLOOR, cap), cap)
    runs = info['runs']
    for k in (3, 12):
        out[f'K{k}'] = np.where(runs >= k, replay.FLOOR, cap)
    for k in (6, 12):
        out[f'CK{k}'] = np.where(runs >= k, replay.continuous(D, s1, a, 1), cap)
    # The sign and signed rank tests of the 18 differences of draws of the same jitter phase (yi11_effect.py).
    t2, z, plus, v = E.stats(x, g)
    f = E.fires(t2, z, plus, v)
    with np.errstate(divide='ignore', invalid='ignore'):
        drift = (W * s1 / (math.sqrt(2) * np.abs(D))) ** (2 / 3)
    drift = np.clip(np.nan_to_num(drift, nan=replay.FLOOR), replay.FLOOR, cap)
    for key in ('S17', 'S18', 'R0.0001'):
        out[key] = np.where(f[key], replay.FLOOR, cap)
        out[key + ' drift'] = np.where(f[key], drift, cap)
    out['fires'] = {k: (np.where(c < cap, 1, 0).sum(0) / np.maximum(tested.sum(0), 1)) for k, c in out.items()}
    return out


def run(name, path, cap, first, change, process=None):
    draws, x, g, _ = replay.load(path, process)
    rows, columns = x.shape[1:]
    holders, near = replay.block_grid(WIDTH, HEIGHT, rows, columns)
    caps = caps_of(x, g, cap)
    surface = (g.reshape(len(g), -1)[:, near] > 0).mean(0) >= 0.5
    keep = draws >= first
    # The truth's stand-in at a block: the nearest anchor's light as a centered mean of 19 draws.
    flat = x.reshape(len(x), -1)[:, near]
    kernel = np.ones(19) / 19
    smooth = np.apply_along_axis(lambda s: np.convolve(s, kernel, mode='same'), 0, flat)
    inner = keep & (np.arange(len(x)) >= 9) & (np.arange(len(x)) < len(x) - 9)
    print(f'{name}, model cap {cap}, draws {first} to {draws[-1]}, {int(surface.sum())} surface blocks')
    fires = caps.pop('fires')
    for key, c in caps.items():
        busy = np.sort(fires[key].ravel())[::-1]
        L, y = replay.model(c, x, g, holders, near)
        short = (L[keep][:, surface] < 0.75 * cap).mean()
        noise = (np.sqrt(cap / L[keep][:, surface]) - 1).mean()
        line = f'    {key:9s} blocks under 3/4 of the cap {100 * short:6.2f} %, added noise {noise:.4f}, mean length {L[keep][:, surface].mean():6.1f}'
        line += f', cuts per tested anchor-draw: all {100 * fires[key].mean():.3f} %, busiest anchor {100 * busy[0]:.2f} %'
        if change:
            with np.errstate(divide='ignore', invalid='ignore'):
                err = np.abs(y[inner][:, surface] - smooth[inner][:, surface]) / np.maximum(smooth[inner][:, surface], 1e-9)
            line += f', model light off the smoothed log by {100 * np.nanmean(err):.2f} % (mean), {100 * np.nanpercentile(err, 90):.2f} % (90 %)'
        print(line)


if __name__ == '__main__':
    run('frozen bay, bounce alone', LOGS / 'r2-main-bay-bounce' / 'region-none.txt', 128, 400, False)
    run('frozen exit-cabin, bounce alone', LOGS / 'r2-main-exit-bounce' / 'region-none.txt', 128, 400, False)
    run('frozen still cabin, bounce alone', LOGS / 'r2-main-cabin-bounce' / 'region-none.txt', 128, 400, False)
    run('rect-bay film (g-bay, process 3)', OLD / 'g-bay' / 'region-none.txt', 128, 36, True, 3)
    run('bay-sun film (g-lag, process 1)', OLD / 'g-lag' / 'region-none.txt', 128, 33, True, 1)
    run('fair cabin switch, with the rule', LOGS / 'switch-rule' / 'region-193-393.txt', 128, 150, True)
