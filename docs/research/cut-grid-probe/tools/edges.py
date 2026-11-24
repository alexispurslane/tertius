#!/usr/bin/env python3
# Tool code of t1-3er4: the edge-leak check at the ceiling lamp of the nose crop. The lamp is the largest part of the
# pixels in the top 120 rows whose luma is above 150 and whose colour moves by at most 2 from d0 to d29 in the baseline. The band is the ceiling pixels within 1..6 output pixels of the lamp (a dilation), the second band
# 7..18 pixels away. A leak of the lamp's light through the fix would raise the first band against the second, and the
# count of band pixels above luma 150. Draws a 3 x zoom of the lamp's corner region at d0 and d1.
# Usage: edges.py <out dir> <label>=<take dir> ...
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import binary_dilation, label as components
sys.path.insert(0, str(Path(__file__).parent)); from roi import luma
out = Path(sys.argv[1]); takes = [a.split("=") for a in sys.argv[2:]]
ref = np.load(Path(takes[0][1]) / "crops.npz")["nose"]
# The emitter's own pixels keep their colour from d0 to d29, while the lit ceiling around them settles.
steady = (np.abs(ref[0:30].astype(np.int16) - ref[29].astype(np.int16)).max(axis=(0, 3)) <= 2) & (luma(ref[29].astype(np.float64)) > 150)
steady[120:] = False
parts, count = components(steady)
lamp = parts == 1 + int(np.argmax(np.bincount(parts.ravel())[1:]))  # the largest steady bright part: the ceiling lamp
near = binary_dilation(lamp, iterations=6) & ~lamp
far = binary_dilation(lamp, iterations=18) & ~binary_dilation(lamp, iterations=6)
res = {}
for label, take in takes:
    nose = np.load(Path(take) / "crops.npz")["nose"]
    res[label] = {}
    for d in (0, 1, 2, 3):
        g = luma(nose[d].astype(np.float64))
        res[label][f"d{d}"] = {"band_1_6_mean": round(float(g[near].mean()), 2), "band_7_18_mean": round(float(g[far].mean()), 2),
                               "band_1_6_above_150": int((g[near] > 150).sum()), "lamp_mean": round(float(g[lamp].mean()), 2)}
    print(label, res[label])
(out / "edge-leak.json").write_text(json.dumps(res, indent=1))
ys, xs = np.nonzero(lamp); x0, x1 = max(xs.min() - 30, 0), min(xs.min() + 90, ref.shape[2]); y0, y1 = max(ys.max() - 30, 0), min(ys.max() + 50, ref.shape[1])
Z = 3; w, h = (x1 - x0) * Z, (y1 - y0) * Z
cv = Image.new("RGB", (70 + 2 * (w + 6), len(takes) * (h + 20)), "white"); dr = ImageDraw.Draw(cv)
for r, (label, take) in enumerate(takes):
    nose = np.load(Path(take) / "crops.npz")["nose"]; dr.text((4, r * (h + 20) + h // 2), label, fill="black")
    for c, d in enumerate((0, 1)):
        cv.paste(Image.fromarray(np.ascontiguousarray(nose[d][y0:y1, x0:x1])).resize((w, h), Image.NEAREST), (70 + c * (w + 6), r * (h + 20) + 16))
        dr.text((70 + c * (w + 6), r * (h + 20) + 2), f"{label} d{d}, lamp corner, nose crop x {x0}..{x1 - 1}, y {y0}..{y1 - 1}", fill="black")
cv.save(out / "edge-lamp-3x.png")
