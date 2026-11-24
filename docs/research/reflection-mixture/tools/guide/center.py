# Tool code of t1-cyhy: frame.params.z against a render pixel's angle. At pixels near the image's center and at points
# across the image, the angle between the eye's rays through neighboring render pixels (from the dump's surface points,
# whose directions are the eye's rays), over frame.params.z. Usage: python3 -I center.py <take dir> <dump draw>
import json, sys
from pathlib import Path
import numpy as np
take, dd = Path(sys.argv[1]), int(sys.argv[2])
sizes = json.loads((take / f'census.d{dd}.dump.json').read_text())
def dumped(n):
    w, h, c = sizes[n]; return np.fromfile(take / f'census.d{dd}.{n}.f32', np.float32).reshape(h, w, c)
P = dumped('record-17')[..., :3].astype(np.float64); r9 = dumped('record-9')
pz = float(np.median(r9[..., 3][r9[..., 3] > 0]))
m = json.loads((take / 'census.json').read_text()); vw, vh = m['Viewport']
V = P / np.maximum(np.linalg.norm(P, axis=-1, keepdims=True), 1e-12)
out = {'pixel angle frame.params.z': pz, 'viewport': [vw, vh]}
for fx, fy in ((0.5, 0.5), (0.25, 0.5), (0.1, 0.5), (0.5, 0.1)):
    x, y = int(fx * vw), int(fy * vh)
    a = np.arccos(np.clip((V[y, x] * V[y, x + 1]).sum(), -1, 1))
    b = np.arccos(np.clip((V[y, x] * V[y + 1, x]).sum(), -1, 1))
    out[f'pixel {x},{y}: across / down neighbor angle over params.z'] = [float(a / pz), float(b / pz)]
print(json.dumps(out, indent=1))
