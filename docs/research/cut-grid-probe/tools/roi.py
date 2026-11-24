#!/usr/bin/env python3
# Tool code of t1-3er4: the grid measure. The ROI is x = 12..159, y = 20..239 of the nose 1:1 crop (NOSE_BOX of ter-fe63's
# analyze.py, output pixels 750..1169 x 0..329), 148 x 220 output pixels. The 601 luma of the whole crop loses its
# Gaussian blur of sigma 3 (the high pass), then the ROI's correlation with itself moved by a lag along x and along y,
# averaged. A 3 x 3 mean of independent samples 4 render pixels apart gives 6/9 = 0.667 at lag 6 output pixels.
import json, sys
from pathlib import Path
import numpy as np
from scipy.ndimage import gaussian_filter

def luma(rgb): return (rgb[..., 0] * 299.0 + rgb[..., 1] * 587.0 + rgb[..., 2] * 114.0) / 1000.0
def corr(a, b):
    a = a - a.mean(); b = b - b.mean(); return float((a * b).sum() / np.sqrt((a * a).sum() * (b * b).sum()))
def ac(hp, lag): return 0.5 * (corr(hp[:, :-lag], hp[:, lag:]) + corr(hp[:-lag, :], hp[lag:, :]))
ROI = (slice(20, 240), slice(12, 160))
def measure(nose_frame, sigma=3):
    g = luma(nose_frame.astype(np.float64))
    hp = (g - gaussian_filter(g, sigma))[ROI]
    return {f"lag{l}": round(ac(hp, l), 3) for l in (3, 6, 12)} | {"roi_mean_luma": round(float(g[ROI].mean()), 2)}
if __name__ == "__main__":
    out = {}
    for take in sys.argv[1:]:
        nose = np.load(Path(take) / "crops.npz")["nose"]
        out[Path(take).name] = {f"d{d}": measure(nose[d]) for d in (0, 1, 2, 3, 5, 10, 29)}
        print(Path(take).name, " | ".join(f"d{d} " + " ".join(f"{v}" for v in m.values()) for d, m in out[Path(take).name].items() if d in ("d0","d1","d2","d5")))
    print(json.dumps(out))
