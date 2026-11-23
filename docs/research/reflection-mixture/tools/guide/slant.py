# Tool code of t1-cyhy: the scale of the off-line distance. (1) Whether the footprint width r * pixelAngle matches the
# spacing of neighboring render pixels' surface points across the eye's ray: the median of |P(x+1) - P(x)| *
# |n . v| / (r * pixelAngle) over the frame, n the shading normal and v the view direction (1 if the pixel angle is a
# render pixel's). (2) The off-line distance times |n . v| at the hit pixel (the frozen dump's normal and point): the
# distance in widths of the pixel's footprint along its surface rather than across the eye's ray.
# Usage: python3 -I slant.py <take dir> <dump draw> <names>
import json, sys
from pathlib import Path
import numpy as np
take, dd, names = Path(sys.argv[1]), int(sys.argv[2]), sys.argv[3].split(',')
sizes = json.loads((take / f'census.d{dd}.dump.json').read_text())
def dumped(n):
    w, h, c = sizes[n]; return np.fromfile(take / f'census.d{dd}.{n}.f32', np.float32).reshape(h, w, c)
P = dumped('record-17'); N = dumped('record-19'); r9 = dumped('record-9')
pz = float(np.median(r9[..., 3][r9[..., 3] > 0]))
R = P[..., 3]; V = P[..., :3] / np.maximum(np.linalg.norm(P[..., :3], axis=-1, keepdims=True), 1e-9)
slant = np.abs((N[..., :3] * V).sum(-1))
ok = (R[:, :-1] > 0) & (R[:, 1:] > 0) & (np.abs(R[:, :-1] - R[:, 1:]) < 0.02 * R[:, :-1])
step = np.linalg.norm(P[:, 1:, :3] - P[:, :-1, :3], axis=-1) * slant[:, :-1] / np.maximum(R[:, :-1] * pz, 1e-9)
out = {'pixel angle': pz, 'median neighbor spacing * slant / (r * angle)': float(np.median(step[ok]))}
fields = [('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'), ('ds', '<f4'), ('d14', '<f4', 4), ('d15', '<f4', 4), ('d22', '<f4', 4)]
for k, name in enumerate(names):
    rec = np.fromfile(take / f'census.keep{k}', dtype=np.dtype(fields))
    f = rec['d15'][:, 2].astype(int) & 7; g = (f & 1) > 0; arrive = (f & 2) > 0
    end = np.round(rec['d22'][:, 1]).astype(int); hit = end == 0
    hx, hy = rec['d14'][:, 0].astype(int), rec['d14'][:, 1].astype(int)
    row = {}
    for lab, sel in (('guided, another pixel', g & hit & ~arrive), ('guided, arrive', g & arrive), ('cosine, hit', ~g & hit)):
        idx = np.nonzero(sel)[0][::7]
        s = slant[hy[idx], hx[idx]]; v = rec['d22'][idx, 2] * s
        row[lab] = {'slant p10/p50/p90': [float(np.percentile(s, p)) for p in (10, 50, 90)],
                    'off-line * slant p50/p90/p99': [float(np.percentile(v, p)) for p in (50, 90, 99)],
                    'share over 1.5': float((v > 1.5).mean())}
    out[name] = row
print(json.dumps(out, indent=1))
