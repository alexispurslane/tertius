# Tool code of t1-nyjl: how much of the full picture's noise each signal carries. For each still and each region, the
# residual e = shown - reference of the same mode, in display-linear luminance at output pixels, in units of the full
# picture's reference mean in that region, split by scale:
#   grain = e - gauss(e, 2 px): finer than about 4 output pixels
#   blob  = gauss(e, 3 px) - gauss(e, 36 px): the daub's band
#   all   = e
# each as a standard deviation over the region. Usage:
#   python3 -I parts.py <boxes.json> <full reference.npy> <label=png:reference.npy>...
import json, sys
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter
boxes = json.load(open(sys.argv[1])); W = [0.2126, 0.7152, 0.0722]
full = np.load(sys.argv[2]).astype(np.float64) @ W
def lin(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float64) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ W
out = {}
for arg in sys.argv[3:]:
    label, rest = arg.split('=', 1); png, ref = rest.split(':')
    e = lin(png) - np.load(ref).astype(np.float64) @ W
    grain = e - gaussian_filter(e, 2); blob = gaussian_filter(e, 3) - gaussian_filter(e, 36)
    out[label] = {b: {k: float(v[y0:y1, x0:x1].std() / full[y0:y1, x0:x1].mean()) for k, v in
                      (('all', e), ('grain', grain), ('blob', blob))} for b, (x0, y0, x1, y1) in boxes.items()}
print(json.dumps(out, indent=1))
