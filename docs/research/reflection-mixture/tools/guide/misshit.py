# Tool code of t1-nyjl: where a guided draw that hits another pixel lands, against its target: the distance in render
# pixels between the hit pixel and the target pixel (percentiles), and the share within 2, 4, 8 and 32 pixels.
# Usage: python3 -I misshit.py <take dir> <names> [reflection]
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
    other = ((f & 1) > 0) & ((f & 4) > 0) & ((f & 2) == 0)
    d = np.hypot(rec['d14'][other, 0] - rec['d14'][other, 2], rec['d14'][other, 1] - rec['d14'][other, 3])
    out[name] = {'count': int(other.sum()), 'distance p10/p50/p90 (render px)': [float(np.percentile(d, p)) for p in (10, 50, 90)],
                 **{f'within {r} px': float((d <= r).mean()) for r in (2, 4, 8, 32)}}
print(json.dumps(out, indent=1))
