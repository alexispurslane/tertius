# Tool code of t1-nyjl: the noise budget of one diffuse sample per region: the standard deviation of one sample X (the
# pre-cap sample, from census.sums: n, sum Xpre, sum Xpre^2 per render pixel), pooled over the region as the root of the
# mean per-pixel variance, over the region's mean of X. Usage: python3 -I budget.py <take dir> <render rects json>
import json, sys
from pathlib import Path
import numpy as np
take = Path(sys.argv[1]); rects = json.loads(sys.argv[2])
m = json.loads((take / 'census.json').read_text())
s = np.fromfile(take / 'census.sums', '<f8').reshape(24, m['Height'], m['Width'])
out = {}
for name, (x0, y0, x1, y1) in rects.items():
    n, S, Q = (s[i][y0:y1, x0:x1] for i in (12, 13, 14))
    mean = S / n; var = Q / n - mean ** 2
    out[name] = {'std of one sample over the mean': float(np.sqrt(var.mean()) / mean.mean()), 'mean X': float(mean.mean())}
print(json.dumps(out, indent=1))
