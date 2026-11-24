# Tool code of ter-fe63: the reflection kind's guided draws by cause, from a census keep of 145-byte records (layer 25:
# x 1 for a guided draw, y the walk's end (0 hit, 1 below the start pixel's geometric plane, 2 out of steps, 3 and 4 the
# ray's end), z 1 where the hit counts for the target, w the hit's view depth over the target's). Classes of the guided
# draws: own plane (y = 1), arrives and counts (z = 1), blocked (a hit that does not count, nearer than the target:
# w < 1), box miss (every other: a hit beyond the target that does not count, or a walk that ends without a hit). The
# share of each class, and each class's share of the reflection part's second moment (layer 8's z, uncapped). A block's
# ray is shared by its block's pixels, so each record counts once per pixel that weighs it.
# Usage: python3 -I causes.py <take dir> <name>...
import json, sys
from pathlib import Path
import numpy as np
fields = [('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'), ('ds', '<f4'),
          ('r6', '<f4', 4), ('r7', '<f4', 4), ('r8', '<f4', 4), ('r9', '<f4', 4),
          ('d14', '<f4', 4), ('d15', '<f4', 4), ('d22', '<f4', 4), ('d25', '<f4', 4)]
out = {}
for k, name in enumerate(sys.argv[2:]):
    r = np.fromfile(Path(sys.argv[1]) / f'census.keep{k}', dtype=np.dtype(fields))
    c = r['d25']; started = c[:, 0] >= 0
    guided = started & (c[:, 0] == 1)
    X2 = r['r8'][:, 2].astype(np.float64) ** 2
    own = guided & (np.round(c[:, 1]) == 1)
    counts = guided & ~own & (c[:, 2] == 1)
    blocked = guided & ~own & ~counts & (np.round(c[:, 1]) == 0) & (c[:, 3] >= 0) & (c[:, 3] < 1)
    miss = guided & ~own & ~counts & ~blocked
    n = guided.sum()
    out[name] = {'pixel-draws that start a reflection ray': int(started.sum()), 'guided share of them': float(n / max(started.sum(), 1)),
                 'classes, share of guided draws': {k2: float(v.sum() / max(n, 1)) for k2, v in
                     (('own plane', own), ('box miss', miss), ('blocked', blocked), ('arrives and counts', counts))},
                 'share of the reflection part second moment': {k2: float(X2[v].sum() / max(X2[started].sum(), 1e-30)) for k2, v in
                     (('arriving guided draws', counts), ('lobe draws', started & ~guided))}}
print(json.dumps(out, indent=1))
