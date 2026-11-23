# Tool code of ter-pdw8: checks the hit light's mip atlas of a V2 dump (TERTIUS_PROBE_CENSUS_DUMP=1) against the means
# that numpy computes from level 0 by the mip shader's rule (the mean light of the covered children, weighted by their
# coverage). Prints per level the largest difference over the largest value, and both means.
# Usage: python3 atlascheck.py <take dir> <render width> <render height>
import json, sys
import numpy as np
T, w, h = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
iw, ih, ch = json.load(open(T + '/census.dump.json'))['hit-diffuse']
a = np.fromfile(T + '/census.hit-diffuse.f32', '<f4').reshape(ih, iw, 4)
B, H = (2 * iw) // 3, ih - 16
L, A = a[..., :3] @ [0.2126, 0.7152, 0.0722], a[..., 3]
prev, top, rows = (L[:h, :w], A[:h, :w]), 0, []
k = 1
while (max(w, h) + (1 << (k - 1)) - 1) >> (k - 1) > 1:
    hk, wk = (h + (1 << k) - 1) >> k, (w + (1 << k) - 1) >> k
    ph, pw = prev[0].shape
    pl, pa, pv = (np.zeros((hk * 2, wk * 2)) for _ in range(3))
    pl[:ph, :pw], pa[:ph, :pw], pv[:ph, :pw] = prev[0] * prev[1], prev[1], 1
    s = lambda x: x.reshape(hk, 2, wk, 2).sum((1, 3))
    exp_l = np.where(s(pa) > 0, s(pl) / np.maximum(s(pa), 1e-9), 0)
    got_l, got_a = L[top:top + hk, B:B + wk], A[top:top + hk, B:B + wk]
    rows.append((k, float(np.abs(got_l - exp_l).max() / max(exp_l.max(), 1e-6)), float(got_l.mean()), float(exp_l.mean())))
    prev, top, k = (got_l, got_a), top + ((H + (1 << k) - 1) >> k), k + 1
for k, err, g, e in rows:
    print(f'level {k}: largest difference {err:.4f} of the largest value; mean {g:.4f}, expected {e:.4f}')
