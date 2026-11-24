# Tool code of t1-nyjl: each region's mean linear luminance in a still's capture 19 and over its 20 captures, against
# the first take named. Usage: python3 -I means.py <boxes.json> <take dir>...
import json, sys
import numpy as np
from PIL import Image
boxes = json.load(open(sys.argv[1]))
def lin(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float64) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]
base = None; out = {}
for t in sys.argv[2:]:
    last = lin(f'{t}/c-s19.png'); allm = sum(lin(f'{t}/c-s{i:02d}.png') for i in range(20)) / 20
    m = {b: (last[y0:y1, x0:x1].mean(), allm[y0:y1, x0:x1].mean()) for b, (x0, y0, x1, y1) in boxes.items()}
    base = base or m
    out[t.rsplit('/', 1)[-1]] = {b: [float(v[0] / base[b][0]), float(v[1] / base[b][1]), float(v[1])] for b, v in m.items()}
print(json.dumps(out, indent=1))
