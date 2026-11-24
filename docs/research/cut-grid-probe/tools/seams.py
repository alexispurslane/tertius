#!/usr/bin/env python3
# Tool code of t1-3er4: the block-phase profile of the cone box (output 640..1279 x 0..359). For each column phase modulo
# 12 output pixels (one 8-pixel block at the render scale 2/3), the mean absolute luma step into that column, over draws
# d0 to d3 and normalized by the mean over all phases; the same for rows. Block borders fall at column phase 8, row phase 0.
import json, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).parent)); from roi import luma
out = {}
for arg in sys.argv[2:]:
    label, take = arg.split("=")
    cone = np.load(Path(take) / "crops.npz")["cone"][0:4]
    px = np.zeros(12); py = np.zeros(12)
    for f in cone:
        g = luma(f.astype(np.float64))
        sx = np.abs(np.diff(g, axis=1)).mean(0); sy = np.abs(np.diff(g, axis=0)).mean(1)
        px += [sx[(np.arange(len(sx)) + 1) % 12 == k].mean() for k in range(12)]
        py += [sy[(np.arange(len(sy)) + 1) % 12 == k].mean() for k in range(12)]
    cx, cy = px / px.mean(), py / py.mean()
    # The resampling of 2 render pixels into 3 output pixels gives every profile a period of 3. So the border phase is
    # compared with the other phases of its own resampling phase: columns 8 against 2, 5 and 11, rows 0 against 3, 6, 9.
    out[label] = {"columns": [round(float(v), 3) for v in cx], "rows": [round(float(v), 3) for v in cy],
                  "column_border_ratio": round(float(cx[8] / cx[[2, 5, 11]].mean()), 3),
                  "row_border_ratio": round(float(cy[0] / cy[[3, 6, 9]].mean()), 3)}
    print(label, out[label]["column_border_ratio"], out[label]["row_border_ratio"])
Path(sys.argv[1]).write_text(json.dumps(out, indent=1))
