# Tool code of t1-nyjl: the split of the guided draws' variance into arrival and spread. Per pixel, at the share s by
# reweighting (X_s = X q / q_s): p = the share of its guided draws that arrive (the walk hits a pixel of the 3 x 3
# around the target and the sample counts: the probe's flag 2), mA and vA the mean and variance of X_s over the
# arriving guided draws. The guided draws' variance is p (1 - p) mA^2 (arrival) + p vA (spread), since a draw that does
# not arrive brings 0. Pooled over the region, each as a share of the guided draws' variance and as a factor against
# today's total variance of one sample (s = 0.25, the "within guided" part of step 1 is s times the guided variance).
# Usage: python3 -I arrival.py <take dir> <names, comma separated> [reflection]
import json, sys
from pathlib import Path
import numpy as np
take, names = Path(sys.argv[1]), sys.argv[2].split(',')
with_reflection = len(sys.argv) > 3 and sys.argv[3] == 'reflection'
meta = json.loads((take / 'census.json').read_text())
fields = [('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'), ('ds', '<f4')]
if with_reflection:
    fields += [('r6', '<f4', 4), ('r7', '<f4', 4), ('r8', '<f4', 4), ('r9', '<f4', 4)]
fields += [('d14', '<f4', 4), ('d15', '<f4', 4)]
dtype = np.dtype(fields)
nk = len(meta['Keeps'])
report = {}
for k, name in enumerate(names):
    n_pix = meta['KeepCounts'][k]
    rec = np.fromfile(take / f'census.keep{k}', dtype=dtype)
    draws = len(rec) // n_pix
    rec = rec[:draws * n_pix].reshape(draws, n_pix)
    X = rec['pre'].astype(np.float64)
    qc = np.exp2(rec['d15'][..., 0].astype(np.float64))
    qg = np.where(rec['d15'][..., 1] <= -99, 0.0, np.exp2(rec['d15'][..., 1].astype(np.float64)))
    flags = rec['d15'][..., 2].astype(int)
    guided = (flags & 1) > 0
    arrives = guided & ((flags & 2) > 0)
    q = 0.75 * qc + 0.25 * qg
    var_today = ((X * X).mean(0) - X.mean(0) ** 2).sum()
    region = {'guided draws that arrive': float(arrives.sum() / guided.sum()),
              'arriving guided draws with X > 0': float((arrives & (X > 0)).sum() / max(arrives.sum(), 1)),
              'non-arriving guided draws with X > 0 (must be 0)': int((guided & ~arrives & (X > 0)).sum())}
    nG = guided.sum(0)
    nA = arrives.sum(0)
    for s in (0.25, 0.5):
        qs = (1 - s) * qc + s * qg
        Xs = np.where(qs > 0, X * q / np.where(qs > 0, qs, 1), 0)
        with np.errstate(invalid='ignore', divide='ignore'):
            p = nA / nG
            mA = np.where(arrives, Xs, 0).sum(0) / nA
            vA = np.where(arrives, Xs * Xs, 0).sum(0) / nA - mA ** 2
            mG = np.where(guided, Xs, 0).sum(0) / nG
            vG = np.where(guided, Xs * Xs, 0).sum(0) / nG - mG ** 2
        ok = (nA > 1) & (nG > 1)
        arrival = p * (1 - p) * mA ** 2
        spread = p * vA
        region[f'share {s}'] = {
            'arrival share of the guided variance': float(arrival[ok].sum() / (arrival + spread)[ok].sum()),
            'spread share of the guided variance': float(spread[ok].sum() / (arrival + spread)[ok].sum()),
            'check: (arrival + spread) / guided variance': float((arrival + spread)[ok].sum() / vG[ok].sum()),
            's * arrival, factor against today': float(s * arrival[ok].sum() / var_today),
            's * spread, factor against today': float(s * spread[ok].sum() / var_today)}
    report[name] = region
(take / 'arrival-result.json').write_text(json.dumps(report, indent=1))
print(json.dumps(report, indent=1))
