"""Tool code of t1-af5w. findedge.py <take folder> <view> <sun|lamp> <least gap m> <greatest gap m>: the box of 96 x 96
probe pixels that holds the most edge pixels of the sun (or of any lamp) whose gap lies in the range, with the count,
the light, the median gap, rendered width and true width there. The probe's pixels are at the render size; the box is
also given at the output size (times the output over the render width)."""
import sys
import numpy as np
take, view, kind, lo, hi = sys.argv[1], sys.argv[2], sys.argv[3], float(sys.argv[4]), float(sys.argv[5])
out_w = int(sys.argv[6]) if len(sys.argv) > 6 else 1920
w, h, _ = map(int, open(f'{take}/{view}/edge.txt').read().split())
a = np.fromfile(f'{take}/{view}/edge-a.f32', np.float32).reshape(h, w, 4)
b = np.fromfile(f'{take}/{view}/edge-b.f32', np.float32).reshape(h, w, 4)
ids = a[..., 3]
m = (ids < 0 if kind == 'sun' else ids > 0) & (a[..., 0] >= lo) & (a[..., 0] <= hi)
B = 96
c = np.cumsum(np.cumsum(np.pad(m.astype(np.int64), ((1, 0), (1, 0))), 0), 1)
s = c[B:, B:] - c[:-B, B:] - c[B:, :-B] + c[:-B, :-B]
y, x = np.unravel_index(np.argmax(s), s.shape)
box = m[y:y + B, x:x + B]
sel = (slice(y, y + B), slice(x, x + B))
angle = np.where(ids < 0, b[..., 2], b[..., 2] / np.maximum(b[..., 1], 1e-6))
true = a[..., 0] * angle
k = out_w / w
lights = np.unique(ids[sel][box])
print(f'{view} {kind} gap {lo}-{hi}: {int(s[y, x])} px in render box x {x}-{x + B} y {y}-{y + B}; output box x {int(x * k)}-{int((x + B) * k)} y {int(y * k)}-{int((y + B) * k)};'
      f' lights {lights.tolist()}, gap {np.median(a[..., 0][sel][box]):.3f} rendered {np.median(a[..., 2][sel][box]):.3f} true {np.median(true[sel][box]):.4f}')
