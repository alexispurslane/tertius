# Tool code of t1-nyjl: why a guided draw does not arrive. Of a region's guided draws: the share that arrive (flag 2),
# that hit another pixel than the 3 x 3 around their target (flag 4 without 2: something stands between, or the walk
# stopped short), and that hit nothing (no flag 4: the target lies below the pixel's geometric plane, so the ray traces
# nothing, or the walk left the screen or ran out of steps). Usage: python3 -I blocked.py <take dir> <names> [reflection]
import json, sys
from pathlib import Path
import numpy as np
take, names = Path(sys.argv[1]), sys.argv[2].split(',')
with_reflection = len(sys.argv) > 3 and sys.argv[3] == 'reflection'
meta = json.loads((take / 'census.json').read_text())
fields = [('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'), ('ds', '<f4')]
if with_reflection:
    fields += [('r6', '<f4', 4), ('r7', '<f4', 4), ('r8', '<f4', 4), ('r9', '<f4', 4)]
fields += [('d14', '<f4', 4), ('d15', '<f4', 4)]
out = {}
for k, name in enumerate(names):
    rec = np.fromfile(take / f'census.keep{k}', dtype=np.dtype(fields))
    f = rec['d15'][:, 2].astype(int)
    g = (f & 1) > 0
    out[name] = {'arrive': float(((f & 2) > 0)[g].mean()),
                 'hit another pixel': float((((f & 4) > 0) & ((f & 2) == 0))[g].mean()),
                 'hit nothing (below the plane, or the walk ended)': float(((f & 4) == 0)[g].mean())}
print(json.dumps(out, indent=1))
