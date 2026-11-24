# Tool code of t1-nyjl: the 8-bit rounding's own share of parts.py's numbers. For a reference mean R (linear RGB), the
# residual that rounding alone leaves, e = lin(round(srgb(R) * 255) / 255) - R in luminance, split as parts.py splits a
# still's residual (all, grain, blob), each as a standard deviation over the region in units of the full picture's
# reference mean there. A still's number at or below this band is the rounding, not noise.
# Usage: python3 -I band.py <boxes.json> <full reference.npy> <label=reference.npy>...
import json, sys
import numpy as np
from scipy.ndimage import gaussian_filter
W = [0.2126, 0.7152, 0.0722]
boxes = json.load(open(sys.argv[1])); full = np.load(sys.argv[2]).astype(np.float64) @ W
def lin(a):
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
out = {}
for arg in sys.argv[3:]:
    label, path = arg.split('=', 1)
    rgb = np.load(path).astype(np.float64)
    enc = np.where(rgb <= 0.0031308, rgb * 12.92, 1.055 * np.clip(rgb, 0, 1) ** (1 / 2.4) - 0.055)
    e = lin(np.round(np.clip(enc, 0, 1) * 255) / 255) @ W - rgb @ W
    grain = e - gaussian_filter(e, 2); blob = gaussian_filter(e, 3) - gaussian_filter(e, 36)
    out[label] = {b: {k: float(v[y0:y1, x0:x1].std() / full[y0:y1, x0:x1].mean()) for k, v in
                      (('all', e), ('grain', grain), ('blob', blob))} for b, (x0, y0, x1, y1) in boxes.items()}
print(json.dumps(out, indent=1))
