# Tool code of ter-fe63: the replay of a mixture take's reflection samples at other shares. The take draws with the
# mixture q_t = (1 - s_t) q_l + s_t q_g, s_t the take's full share times the ramp at the pixel's roughness (layer 7's w,
# sqrt of the floored alpha; the ramp rises linearly from 0.1 to 0.38). A sample X (layer 8's z, uncapped) gives the
# second moment under the share s as the mean of X^2 q_t / q_s, with q_s ramped the same way. Per region: the relative
# variance (the pooled variance over the pooled squared mean) at each share, and the least of them.
# Usage: python3 -I wreplay.py <take dir> <take share> <name=width x height in render pixels>...
import json, sys
from pathlib import Path
import numpy as np
take = Path(sys.argv[1]); st = float(sys.argv[2])
fields = [('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'), ('ds', '<f4'),
          ('r6', '<f4', 4), ('r7', '<f4', 4), ('r8', '<f4', 4), ('r9', '<f4', 4),
          ('d14', '<f4', 4), ('d15', '<f4', 4), ('d22', '<f4', 4), ('d25', '<f4', 4)]
shares = (0.0, 1 / 32, 1 / 16, 1 / 8, 1 / 4, 1 / 2)
ramp = lambda r: np.clip((r - 0.1) / 0.28, 0.0, 1.0)
out = {}
for k, arg in enumerate(sys.argv[3:]):
    name, size = arg.split('=')
    w, h = map(int, size.split('x')); npix = w * h
    rec = np.fromfile(take / f'census.keep{k}', dtype=np.dtype(fields))
    draws = len(rec) // npix; rec = rec[:draws * npix]
    starts = (rec['r6'][:, 0] != -1.0).reshape(draws, npix)
    X = np.where(starts, rec['r8'][:, 2].reshape(draws, npix), 0.0).astype(np.float64)
    ql = np.exp2(rec['r6'][:, 2].astype(np.float64)).reshape(draws, npix)
    qg = np.where(rec['r6'][:, 3] <= -99, 0.0, np.exp2(rec['r6'][:, 3].astype(np.float64))).reshape(draws, npix)
    rough = rec['r7'][:, 3].reshape(draws, npix)[0]
    has = starts.all(0)
    X, ql, qg, ru = X[:, has], ql[:, has], qg[:, has], ramp(rough[has])
    qt = (1 - st * ru) * ql + st * ru * qg
    mean = X.mean(0); sq = float((mean ** 2).mean())
    row = {'draws': draws, 'pixels': int(has.sum()), 'pixels at the full share': int((ru >= 1).sum()),
           'median sqrt alpha': float(np.median(rough[has]))}
    for s in shares:
        qs = (1 - s * ru) * ql + s * ru * qg
        m2 = (X ** 2 * np.where(X > 0, qt / np.maximum(qs, 1e-30), 0.0)).mean(0)
        row[f's {s:g}'] = float((m2 - mean ** 2).mean() / sq)
    row['least share'] = min(shares, key=lambda s: row[f's {s:g}'])
    out[name] = row
print(json.dumps(out, indent=1))
