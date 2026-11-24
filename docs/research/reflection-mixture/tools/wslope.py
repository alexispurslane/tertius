# Tool code of ter-fe63: the slope of the reflection kind's variance in the guided share, from a real take with the cause
# record (145-byte keeps). For each sample, w = the reflection part's luminance as the mixture weighed it, uncapped (layer
# 8's z: the sample's light times the pixel's response over the mixture density and the specular factor); a draw that
# brings nothing has w = 0. Per region: the count and the mean of w^2 over the lobe draws and over the guided draws
# (layer 25's x), and their difference, which is the slope of one draw's variance in the share.
# Usage: python3 -I wslope.py <take dir> <name>...
import json, sys
from pathlib import Path
import numpy as np
fields = [('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'), ('ds', '<f4'),
          ('r6', '<f4', 4), ('r7', '<f4', 4), ('r8', '<f4', 4), ('r9', '<f4', 4),
          ('d14', '<f4', 4), ('d15', '<f4', 4), ('d22', '<f4', 4), ('d25', '<f4', 4)]
out = {}
for k, name in enumerate(sys.argv[2:]):
    r = np.fromfile(Path(sys.argv[1]) / f'census.keep{k}', dtype=np.dtype(fields))
    started = r['d25'][:, 0] >= 0
    guided = started & (r['d25'][:, 0] == 1); lobe = started & ~guided
    w2 = r['r8'][:, 2].astype(np.float64) ** 2
    mean = r['r8'][started, 2].astype(np.float64).mean()
    out[name] = {'lobe draws': int(lobe.sum()), 'guided draws': int(guided.sum()),
                 'mean w^2, lobe, over mean^2': float(w2[lobe].mean() / mean ** 2),
                 'mean w^2, guided, over mean^2': float(w2[guided].mean() / mean ** 2) if guided.any() else None,
                 'slope (lobe - guided), over mean^2': float((w2[lobe].mean() - w2[guided].mean()) / mean ** 2) if guided.any() else None}
print(json.dumps(out, indent=1))
