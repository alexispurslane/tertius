# Tool code of the region repair's replay (t0-qvm1). pmeasure.py <out json>: the factor p of the continuous cap, measured
# per frozen still from queue.sh's bounce runs (r2-main-<view>-bounce: the bounce light alone, at a fixed exposure, the
# captures holding the 24-bit log2 luminance of the HDR image after FSR and before the tone curve). Terms:
#   pixel scatter  per output pixel, the standard deviation (n - 1) of its luminance over the captures v-s01 to v-s27,
#                  over its mean. The captures lie 33 draws apart, so at a history of 32 draws they are nearly
#                  independent. A capture counts for a pixel only where the model history of the pixel's render block
#                  under today's rule (replay.py, T) is 32 draws at the capture's shown draw (33 k + 32), so that the short
#                  histories do not inflate it; a pixel needs 10 counted captures, and a history in the dump of draw 890.
#   region s1      per anchor, the median over its tested draws of s1 over the older window's mean (replay.statistics).
#   p              per anchor: the median pixel scatter over the output pixels of the anchor's 17 x 17 blocks, times
#                  sqrt(2 * 32), over the region s1. A history of 32 draws then holds p * s1 / sqrt(64) of noise.
# It prints, per still, the 10th, 50th and 90th percentile of p over the anchors, and writes them with the medians.
import importlib.util
import json
import re
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import replay as R  # noqa: E402

ROOT = HERE.parents[3]
spec = importlib.util.spec_from_file_location('survey', ROOT / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)

VIEWS = {'exit-cabin': ('exit', 18), 'lamp hall': ('lamp', 25), 'bay': ('bay', 18), 'still cabin': ('cabin', 18)}
result = {}
for name, (view, P) in VIEWS.items():
    run = R.RUNS / f'r2-main-{view}-bounce'
    if not (run / 'region').exists():
        print(f'{name}: no bounce run, skipped')
        continue
    draws, x, g, moved = R.load(run / 'region' / 'region-none.txt')
    path = next((run / 'dump').glob('history-890-*.f32'))
    w, h = map(int, re.search(r'-(\d+)x(\d+)\.f32$', path.name).groups())
    L890 = np.fromfile(path, dtype=np.float32).reshape(h, w)
    rows, columns = x.shape[1:]
    holders, near = R.block_grid(w, h, rows, columns)
    caps, st = R.candidates(x, g, P, ps=())
    Lm, _ = R.model(caps['T'], x, g, holders, near)
    frames = [survey.probe_luminance(run / 'frames' / f'v-s{k:02d}.png') for k in range(1, 28)]
    H, Wd = frames[0].shape
    stack = np.stack(frames)                                            # [capture, y, x]
    ry = np.minimum(((np.arange(H) + 0.5) * h / H).astype(int), h - 1)
    rx = np.minimum(((np.arange(Wd) + 0.5) * w / Wd).astype(int), w - 1)
    by, bx = ry // 8, rx // 8
    full = np.stack([Lm[np.searchsorted(draws, 33 * k + 32)] >= 31.5 for k in range(1, 28)])   # [capture, block y, block x]
    keep = full[:, by[:, None], bx[None, :]]                            # [capture, y, x]
    surface = (L890[ry[:, None], rx[None, :]] > 0)
    n = keep.sum(0)
    vals = np.where(keep, stack, np.nan)
    with np.errstate(all='ignore'):
        rel = np.nanstd(vals, 0, ddof=1) / np.nanmean(vals, 0)
    rel = np.where(surface & (n >= 10) & np.isfinite(rel), rel, np.nan)
    tested = st['tested']
    with np.errstate(all='ignore'):
        older_mean = np.zeros_like(x)
        for t in range(P + R.W - 1, len(x)):
            older_mean[t] = x[t - P - R.W + 1:t - P + 1].mean(0)
        s1rel = np.where(tested, st['s1'] / np.abs(older_mean), np.nan)
    ps = []
    for ay in range(rows):
        for ax in range(columns):
            if not tested[:, ay, ax].any():
                continue
            s = np.nanmedian(s1rel[:, ay, ax])
            ys = (by >= 8 * ay - 8) & (by <= 8 * ay + 8)
            xs = (bx >= 8 * ax - 8) & (bx <= 8 * ax + 8)
            pix = rel[np.ix_(ys, xs)]
            if not np.isfinite(pix).any() or not s > 0:
                continue
            ps.append(float(np.nanmedian(pix) * np.sqrt(2 * 32) / s))
    q = [float(v) for v in np.percentile(ps, (10, 50, 90))]
    counted = int(np.isfinite(rel).sum())
    result[name] = {'p': q, 'anchors': len(ps), 'pixels': counted, 'pixelsAll': int(surface.sum())}
    print(f'{name}: p over {len(ps)} anchors: 10 % {q[0]:.2f}, 50 % {q[1]:.2f}, 90 % {q[2]:.2f}; '
          f'{counted} of {int(surface.sum())} surface pixels counted; median pixel scatter {np.nanmedian(rel):.4f}, '
          f'median region s1 {np.nanmedian(s1rel):.4f}')
Path(sys.argv[1]).write_text(json.dumps(result, indent=1))
