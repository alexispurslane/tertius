"""Tool code of t1-af5w. edgemap.py <take folder> <view> <out.png>: the probe's edge pixels of one view, colored by the
rendered width over the true width on a log scale: red where today's ramp is wider than the true penumbra (too soft),
blue where it is narrower (too sharp), white where they agree within a factor of 2; brighter for the sun's edges, and
a grey background where no ramp lies."""
import sys
import numpy as np
from PIL import Image
take, view, out = sys.argv[1:4]
w, h, _ = map(int, open(f'{take}/{view}/edge.txt').read().split())
a = np.fromfile(f'{take}/{view}/edge-a.f32', np.float32).reshape(h, w, 4)
b = np.fromfile(f'{take}/{view}/edge-b.f32', np.float32).reshape(h, w, 4)
ids = a[..., 3]
sun = ids < 0
angle = np.where(sun, b[..., 2], b[..., 2] / np.maximum(b[..., 1], 1e-6))
ratio = a[..., 2] / np.maximum(a[..., 0] * angle, 1e-6)
t = np.clip(np.log2(np.maximum(ratio, 1e-6)) / 6, -1, 1)  # -1 at 1/64, +1 at 64
img = np.full((h, w, 3), 60, np.uint8)
m = ids != 0
col = np.zeros((h, w, 3))
col[..., 0] = np.where(t > 0, 255, 255 * (1 + t))
col[..., 2] = np.where(t < 0, 255, 255 * (1 - t))
col[..., 1] = 255 * (1 - np.abs(t))
img[m] = col[m].astype(np.uint8)
Image.fromarray(img).save(out)
