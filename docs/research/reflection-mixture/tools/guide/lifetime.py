# Tool code of t1-nyjl: the lifetime of the diffuse blobs. For capture k of a take in the diffuse mode, B_k = the blob
# band gauss(e, 3 px) - gauss(e, 36 px) of e = shown - reference (display-linear luminance, output pixels), the
# reference being the mean of captures 30 to 99 of the 100-capture diffuse take. Per region, the correlation over the
# region's pixels of B_k with B_(k+n), averaged over the pairs with k and k + n in the given captures, for n = 1, 2, 3, 6,
# 12 captures (33 to 396 draws). The reference shares 1/70 of each capture it holds, which adds about 0.014 to every lag.
# Usage: python3 -I lifetime.py <reference.npy> <boxes.json> <label=take dir:first-last>...
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter
W = [0.2126, 0.7152, 0.0722]
ref = np.load(sys.argv[1]).astype(np.float64) @ W
boxes = json.loads(Path(sys.argv[2]).read_text())
def lin(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float64) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ W
out = {}
for arg in sys.argv[3:]:
    label, rest = arg.split('=', 1); d, rng = rest.split(':'); a, b = map(int, rng.split('-'))
    B = {}
    for k in range(a, b + 1):
        # The absolute residual: a relative one, shown / reference - 1, takes huge values at near-black pixels outside a
        # region, and the 36-pixel Gaussian carries them into it (the back wall's constant band of the first version).
        e = lin(Path(d) / f'c-s{k:02d}.png') - ref
        B[k] = gaussian_filter(e, 3) - gaussian_filter(e, 36)
    row = {}
    for name, (x0, y0, x1, y1) in boxes.items():
        lags = {}
        for n in (1, 2, 3, 6, 12):
            cs = [np.corrcoef(B[k][y0:y1, x0:x1].ravel(), B[k + n][y0:y1, x0:x1].ravel())[0, 1]
                  for k in range(a, b + 1 - n)]
            lags[f'{n} ({33 * n} draws)'] = [float(np.mean(cs)), len(cs)] if cs else None
        row[name] = lags
    out[label] = row
print(json.dumps(out, indent=1))
