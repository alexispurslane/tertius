# Tool code of t1-nyjl: the blotch's size. For each still and each census region of the bay (output pixels):
#   rel = shown / reference - 1, in display-linear luminance, at output pixels, with no blur. The reference is the
#         independent one of reference.py.
#   the radial autocorrelation of rel over the region (its mean removed; zero-padded FFT, each lag divided by its
#   overlap), its half width at half maximum in output pixels and in render pixels (output / 1.5 at the scale 2/3), the
#   autocorrelation at the lags 1, 2, 4, 8, 16, 32 and 64 output pixels, and rel's standard deviation.
# A white grain falls to near 0 at lag 1, so the half width can be under one pixel while a slower part remains; the
# lags show both. Usage: python3 -I blotch.py <reference.npy> <boxes.json> <label=png or label=dir:first-last>...
#   label=dir:a-b takes the mean, in linear light, of captures a to b of the take dir (a late mean).
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image
ref = np.load(sys.argv[1]).astype(np.float64) @ [0.2126, 0.7152, 0.0722]
boxes = json.loads(Path(sys.argv[2]).read_text())
def lin(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float64) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
def load(spec):
    if ':' in spec:
        d, r = spec.split(':'); a, b = map(int, r.split('-'))
        return sum(lin(Path(d) / f'c-s{i:02d}.png') for i in range(a, b + 1)) / (b - a + 1)
    return lin(spec)
LAGS = (1, 2, 4, 8, 16, 32, 64)
out = {}
for arg in sys.argv[3:]:
    label, spec = arg.split('=', 1)
    shown = load(spec) @ [0.2126, 0.7152, 0.0722]
    rows = {}
    for name, (x0, y0, x1, y1) in boxes.items():
        rel = shown[y0:y1, x0:x1] / np.maximum(ref[y0:y1, x0:x1], 1e-4) - 1
        rel = rel - rel.mean()
        h, w = rel.shape
        F = np.fft.rfft2(rel, s=(2 * h, 2 * w))
        ac = np.fft.irfft2(F * np.conj(F), s=(2 * h, 2 * w))
        ones = np.fft.rfft2(np.ones_like(rel), s=(2 * h, 2 * w))
        cnt = np.fft.irfft2(ones * np.conj(ones), s=(2 * h, 2 * w))
        ac = ac / np.maximum(np.round(cnt), 1)
        ac = ac / ac[0, 0]
        yy = np.fft.fftfreq(2 * h, 1 / (2 * h)); xx = np.fft.fftfreq(2 * w, 1 / (2 * w))
        r = np.hypot(yy[:, None], xx[None, :])
        lim = min(h, w) // 2
        radial = np.array([ac[(r >= k - 0.5) & (r < k + 0.5)].mean() for k in range(lim)])
        below = np.nonzero(radial < 0.5)[0]
        if len(below) and below[0] > 0:
            k = below[0]; hw = k - 1 + (radial[k - 1] - 0.5) / (radial[k - 1] - radial[k])
        else:
            hw = float('nan')
        # The half width of the slow part: where the autocorrelation falls to half of its value at lag 2 (past the grain).
        slow = np.nonzero(radial[2:] < 0.5 * radial[2])[0]
        hw_slow = float(slow[0] + 2) if len(slow) else float('nan')
        rows[name] = {'rel std': float(rel.std()), 'half width (output px)': float(hw), 'half width (render px)': float(hw / 1.5),
                      'slow part: lag where it falls to half of lag 2 (output px)': hw_slow,
                      'autocorrelation at lags ' + ','.join(map(str, LAGS)): [float(radial[k]) if k < lim else None for k in LAGS]}
    out[label] = rows
print(json.dumps(out, indent=1))
