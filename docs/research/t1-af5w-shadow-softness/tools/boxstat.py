"""Tool code of t1-af5w. boxstat.py <take folder> <view> <x> <y> <size> [output width]: the probe's edge pixels inside a
box given in output pixels (1920 wide by default), per light: the pixels, and the median gap, rendered width and true
width (m)."""
import sys
import numpy as np
take, view = sys.argv[1], sys.argv[2]
x, y, n = map(int, sys.argv[3:6])
ow = int(sys.argv[6]) if len(sys.argv) > 6 else 1920
w, h, _ = map(int, open(f'{take}/{view}/edge.txt').read().split())
k = w / ow
sl = (slice(int(y * k), int((y + n) * k)), slice(int(x * k), int((x + n) * k)))
a = np.fromfile(f'{take}/{view}/edge-a.f32', np.float32).reshape(h, w, 4)[sl].reshape(-1, 4)
b = np.fromfile(f'{take}/{view}/edge-b.f32', np.float32).reshape(h, w, 4)[sl].reshape(-1, 4)
out = []
for lid in np.unique(a[:, 3]):
    if lid == 0:
        continue
    m = a[:, 3] == lid
    angle = b[m, 2] if lid < 0 else b[m, 2] / np.maximum(b[m, 1], 1e-6)
    true = a[m, 0] * angle
    name = 'sun' if lid < 0 else f'lamp {int(lid - 1)}'
    out.append(f'{name}: {int(m.sum())} px, gap {np.median(a[m, 0]):.3f} m, rendered {np.median(a[m, 2]):.3f} m, true {np.median(true):.4f} m')
print(f'{view} box x {x} y {y} size {n}: ' + ('; '.join(out) if out else 'no edge pixels'))
