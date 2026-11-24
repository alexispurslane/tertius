# Tool code of t1-nyjl: whether a constant blob band is the 8-bit quantization of the shown image. Per region: the
# correlation of capture k's blob band B_k (as lifetime.py) with the blob band of the reference's own quantization,
# Q = lin(round(srgb(reference) * 255) / 255) / reference - 1, and the standard deviations of both bands.
# Usage: python3 -I quant.py <reference.npy> <boxes.json> <take dir> <captures first-last>
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter
W = [0.2126, 0.7152, 0.0722]
refrgb = np.load(sys.argv[1]).astype(np.float64); ref = refrgb @ W
boxes = json.loads(Path(sys.argv[2]).read_text()); d = Path(sys.argv[3]); a, b = map(int, sys.argv[4].split('-'))
def lin(a):
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
enc = np.where(refrgb <= 0.0031308, refrgb * 12.92, 1.055 * np.clip(refrgb, 0, 1) ** (1 / 2.4) - 0.055)
qref = lin(np.round(enc * 255) / 255) @ W
band = lambda r: gaussian_filter(r, 3) - gaussian_filter(r, 36)
Q = band(qref - ref)  # absolute, as lifetime.py
out = {}
for name, (x0, y0, x1, y1) in boxes.items():
    cs, sb = [], []
    for k in range(a, b + 1):
        s = lin(np.asarray(Image.open(d / f'c-s{k:02d}.png').convert('RGB')).astype(np.float64) / 255) @ W
        B = band(s - ref)[y0:y1, x0:x1]
        cs.append(np.corrcoef(B.ravel(), Q[y0:y1, x0:x1].ravel())[0, 1]); sb.append(B.std())
    out[name] = {'corr of B_k with the quantization band': float(np.mean(cs)), 'std of B_k': float(np.mean(sb) / ref[y0:y1, x0:x1].mean()),
                 'std of the quantization band': float(Q[y0:y1, x0:x1].std() / ref[y0:y1, x0:x1].mean())}
print(json.dumps(out, indent=1))
