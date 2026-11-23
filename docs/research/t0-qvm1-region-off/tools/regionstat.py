# Tool code of the region take (t0-qvm1), its second step. regionstat.py <out.json> <label|region log|kind>...: the region
# test's statistic per tested anchor-draw, from t0-3vwx's region log (scratch-region-log.patch: per draw a line
# "<draw since the process start> <draw index> <anchors> <valid> <moved> <ring width>" in region-*.txt, and per draw and
# anchor three floats in region-*.f32: the region's light, its count of valid draws, and the test's answer). A log file
# that several processes appended holds one block per process: its draw count starts again at 0.
# The test (region_drift.glsl of 05a27262d): at an anchor with 36 valid draws in a row, the newer window is this draw and
# the 17 before it, the older window the 18 before those. Per tested anchor-draw this tool computes
#   d    = |mean of the newer window - mean of the older window| / the older mean: the drift's relative size;
#   stat = |the same difference| / (the older window's standard deviation (n - 1) * sqrt(2 / 18)): the test's statistic,
#          which the shader compares with 3;
# and checks stat > 3 against the shader's own answer. The kinds:
#   still: every fire is false;
#   switch: the lamps go off at draw 193 and on at 393; draws 69 to 192 are a still (false fires); the 40 draws from each
#           switch are the true fires' window;
#   film:<draw>: the light starts to change at that draw (the survey's motion and lag films: after its still of 33
#           draws, the film's first frame), the 40 draws from it are the true fires' window; draws before it are a still;
#   walk: the fires after the walk's stop are listed apart (no light changes; the regions become valid after the stop).
# A fourth field "|<n>" keeps only the n-th process of the log (1 for the first), such as the film of a survey run whose
# other processes are its exposure and reference stills.
# It writes every tested anchor-draw's d, stat and answer, per run and window, to <out.json>, for regionrules.py.
import json
import sys
from pathlib import Path
import numpy as np

W = 18


def processes(path):
    lines = [l.split() for l in open(path)]
    raw = np.fromfile(str(path)[:-4] + '.f32', dtype='<f4')
    blocks, at = [], 0
    for line in lines:
        anchors = int(line[2])
        frame = raw[at:at + 3 * anchors].reshape(anchors, 3)
        at += 3 * anchors
        if int(line[0]) == 0:
            blocks.append([])
        blocks[-1].append((int(line[0]), frame))
    return blocks


out = {}
for spec in sys.argv[2:]:
    label, path, kind, *pick = spec.split('|')
    runs = []
    blocks = processes(Path(path))
    if pick:
        blocks = [blocks[int(pick[0]) - 1]]
    for block in blocks:
        draws = np.array([d for d, _ in block])
        light = np.stack([f[:, 0] for _, f in block])        # [draw, anchor]
        valid = np.stack([f[:, 1] for _, f in block])
        moved = np.stack([f[:, 2] for _, f in block]) != 0
        tested = valid >= 2 * W
        rows = []
        for t in range(2 * W - 1, len(draws)):
            for a in np.nonzero(tested[t])[0]:
                newer = light[t - W + 1:t + 1, a]
                older = light[t - 2 * W + 1:t - W + 1, a]
                diff = abs(newer.mean() - older.mean())
                spread = older.std(ddof=1)
                stat = diff / (spread * np.sqrt(2 / W)) if spread > 0 else (np.inf if diff > 0 else 0.0)
                rel = diff / abs(older.mean()) if older.mean() != 0 else np.inf
                rows.append((int(draws[t]), int(a), float(rel), float(min(stat, 1e9)), bool(moved[t, a])))
        runs.append(rows)
    out[label] = {'kind': kind, 'runs': runs}
    all_rows = [r for rows in runs for r in rows]
    agree = sum((r[3] > 3) == r[4] for r in all_rows)
    fires = sum(r[4] for r in all_rows)
    print(f'{label}: {len(runs)} process(es), {len(all_rows)} tested anchor-draws, {fires} fires, the offline test agrees on '
          f'{agree} ({100 * agree / max(len(all_rows), 1):.3f} %)', flush=True)
Path(sys.argv[1]).write_text(json.dumps(out))
