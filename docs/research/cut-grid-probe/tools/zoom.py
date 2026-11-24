#!/usr/bin/env python3
# Tool code of t1-3er4: a 2 x nearest zoom of one region of the nose crop (x 120..299, y 110..249 of the 1:1 crop, output
# pixels 870..1049 x 110..249) per take, at draws d0, d1, d2 and d5. Usage: zoom.py <out png> <label>=<take dir> ...
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
out = Path(sys.argv[1]); takes = [a.split("=") for a in sys.argv[2:]]
draws, (x0, x1, y0, y1), Z = [0, 1, 2, 5], (120, 300, 110, 250), 2
w, h = (x1 - x0) * Z, (y1 - y0) * Z
cv = Image.new("RGB", (70 + len(draws) * (w + 6), len(takes) * (h + 20)), "white"); dr = ImageDraw.Draw(cv)
for r, (label, take) in enumerate(takes):
    nose = np.load(Path(take) / "crops.npz")["nose"]
    dr.text((4, r * (h + 20) + h // 2), label, fill="black")
    for c, d in enumerate(draws):
        im = Image.fromarray(np.ascontiguousarray(nose[d][y0:y1, x0:x1])).resize((w, h), Image.NEAREST)
        cv.paste(im, (70 + c * (w + 6), r * (h + 20) + 16)); dr.text((70 + c * (w + 6), r * (h + 20) + 2), f"{label} d{d}", fill="black")
cv.save(out)
