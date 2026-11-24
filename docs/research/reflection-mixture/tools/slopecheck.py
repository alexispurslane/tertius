# Scratch check of ter-fe63: the w^2 slope (lobe minus guided) against the replay's derivative at the take's share, both
# in units of the pooled mean squared and of the mean of the per-pixel mean squared.
import json, sys
from pathlib import Path
import numpy as np
take = Path(sys.argv[1]); st = float(sys.argv[2])
fields = [('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'), ('ds', '<f4'),
          ('r6', '<f4', 4), ('r7', '<f4', 4), ('r8', '<f4', 4), ('r9', '<f4', 4),
          ('d14', '<f4', 4), ('d15', '<f4', 4), ('d22', '<f4', 4), ('d25', '<f4', 4)]
for k, arg in enumerate(sys.argv[3:]):
    name, size = arg.split('='); w, h = map(int, size.split('x')); npix = w * h
    rec = np.fromfile(take / f'census.keep{k}', dtype=np.dtype(fields)); draws = len(rec) // npix; rec = rec[:draws * npix]
    starts = (rec['r6'][:, 0] != -1.0).reshape(draws, npix)
    X = np.where(starts, rec['r8'][:, 2].reshape(draws, npix), 0.0).astype(np.float64)
    ql = np.exp2(rec['r6'][:, 2].astype(np.float64)).reshape(draws, npix)
    qg = np.where(rec['r6'][:, 3] <= -99, 0.0, np.exp2(rec['r6'][:, 3].astype(np.float64))).reshape(draws, npix)
    g = (rec['d25'][:, 0] == 1).reshape(draws, npix)
    has = starts.all(0); X, ql, qg, g = X[:, has], ql[:, has], qg[:, has], g[:, has]
    qt = (1 - st) * ql + st * qg
    pooled = X.mean() ** 2; perpix = (X.mean(0) ** 2).mean()
    deriv = (X ** 2 * np.where(X > 0, (ql - qg) / np.maximum(qt, 1e-30), 0.0)).mean()
    split = (X[~g] ** 2).mean() - (X[g] ** 2).mean()
    print(name, json.dumps({'derivative / pooled mean^2': deriv / pooled, 'lobe-guided split / pooled mean^2': split / pooled,
          'derivative / per-pixel mean^2': deriv / perpix, 'per-pixel over pooled': perpix / pooled}))
