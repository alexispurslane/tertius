# Tool code of ter-fe63: the mixture's variance split into the part between the two kinds of draw and the part within
# them, from the census's log of lobe-drawn reflection samples (ramp.py's records). Per pixel and share s: the total
# second moment is the mean of X^2 q_l / q_s; mC, the mean of a lobe draw's sample under the mixture, is the mean of
# X q_l / q_s; mG, the mean of a guided draw's sample, is (mean - (1 - s) mC) / s; between = s (1 - s) (mG - mC)^2;
# within = total variance - between; and the guided draws' share of the second moment. Each is pooled over the region's pixels and divided by the pooled squared mean.
# Usage: python3 -I within.py <take dir> <name=width x height in render pixels>...
import json, sys
from pathlib import Path
import numpy as np
take = Path(sys.argv[1])
fields = [('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'), ('ds', '<f4'),
          ('r6', '<f4', 4), ('r7', '<f4', 4), ('r8', '<f4', 4), ('r9', '<f4', 4),
          ('d14', '<f4', 4), ('d15', '<f4', 4), ('d22', '<f4', 4), ('d25', '<f4', 4)]
out = {}
for k, arg in enumerate(sys.argv[2:]):
    name, size = arg.split('=')
    w, h = map(int, size.split('x')); npix = w * h
    rec = np.fromfile(take / f'census.keep{k}', dtype=np.dtype(fields))
    draws = len(rec) // npix; rec = rec[:draws * npix]
    starts = (rec['r6'][:, 0] != -1.0).reshape(draws, npix)
    X = np.where(starts, rec['r8'][:, 2].reshape(draws, npix), 0.0).astype(np.float64)
    ql = np.exp2(rec['r6'][:, 2].astype(np.float64)).reshape(draws, npix)
    qg = np.where(rec['r6'][:, 3] <= -99, 0.0, np.exp2(rec['r6'][:, 3].astype(np.float64))).reshape(draws, npix)
    has = starts.all(0)
    X, ql, qg = X[:, has], ql[:, has], qg[:, has]
    mean = X.mean(0); sq = float((mean ** 2).mean())
    row = {'pixels': int(has.sum())}
    for s in (0.0, 0.1, 0.125, 0.25, 0.5):
        ratio = np.where(X > 0, ql / np.maximum((1 - s) * ql + s * qg, 1e-30), 0.0)
        total = (X ** 2 * ratio).mean(0) - mean ** 2
        mC = (X * ratio).mean(0)
        between = s * (1 - s) * ((mean - (1 - s) * mC) / s - mC) ** 2 if s > 0 else np.zeros_like(mean)
        row[f's {s}'] = {'total': float(total.mean() / sq), 'between': float(between.mean() / sq),
                         'within': float((total - between).mean() / sq),
                         # The guided draws' part of the second moment: each lobe sample's reweighted moment
                         # X^2 q_l / q_s splits as (1 - s) q_l / q_s for the lobe draws and s q_g / q_s for the guided.
                         'guided share of the second moment': float((X ** 2 * ratio * np.where(X > 0, s * qg / np.maximum(
                             (1 - s) * ql + s * qg, 1e-30), 0.0)).sum() / max((X ** 2 * ratio).sum(), 1e-30)),
                         'mC / mean': float(mC.mean() / mean.mean()), 'mG / mean': float(((mean - (1 - s) * mC) / s).mean() / mean.mean()) if s > 0 else None}
    out[name] = row
(take / 'within-result.json').write_text(json.dumps(out, indent=1))
print(json.dumps(out, indent=1))
