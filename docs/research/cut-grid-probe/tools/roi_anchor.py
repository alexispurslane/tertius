# Tool code of t1-3er4: the grid measure on ter-fe63's committed strip pics/real-cut-nose-strip-1x.png (rows "main" and
# "branch", columns d0, d1, d2, d5 of 426 pixels), with six high passes: x - Gaussian(sigma 1..4) and x - box(7, 13).
# Prints the x/y averaged autocorrelation of the ROI (x 12..159, y 20..239 of each 420 x 330 crop) at lags 3, 6 and 12.
# roi.py uses sigma 3. Usage: python3 -I roi_anchor.py <strip png>
import sys
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter, uniform_filter

im = np.asarray(Image.open(sys.argv[1]).convert("RGB")).astype(np.float64)
luma = (im[..., 0] * 299 + im[..., 1] * 587 + im[..., 2] * 114) / 1000


def corr(a, b):
    a = a - a.mean()
    b = b - b.mean()
    return float((a * b).sum() / np.sqrt((a * a).sum() * (b * b).sum()))


def ac(hp, lag):
    return 0.5 * (corr(hp[:, :-lag], hp[:, lag:]) + corr(hp[:-lag, :], hp[lag:, :]))


for row, top in (("main", 42), ("branch", 420)):
    for col, d in enumerate((0, 1, 2, 5)):
        crop = luma[top:top + 330, col * 426:col * 426 + 420]
        roi_slice = (slice(20, 240), slice(12, 160))
        out = []
        for name, hpf in (
            ("g1", lambda g: g - gaussian_filter(g, 1)),
            ("g2", lambda g: g - gaussian_filter(g, 2)),
            ("g3", lambda g: g - gaussian_filter(g, 3)),
            ("g4", lambda g: g - gaussian_filter(g, 4)),
            ("b7", lambda g: g - uniform_filter(g, 7)),
            ("b13", lambda g: g - uniform_filter(g, 13)),
        ):
            hp = hpf(crop)[roi_slice]
            out.append(f"{name}: " + " ".join(f"{ac(hp, l):.3f}" for l in (3, 6, 12)))
        print(row, f"d{d}", " | ".join(out))
