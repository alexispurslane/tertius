# Tool code of ter-pdw8: the level of V2's footprint formula that the far hits read, per region. RayCensus
# (apply-rayend2.py) counts, per render pixel over the counted draws, every diffuse sample whose hit lies at 2 m or more
# (class far) by the level level = clamp(round(log2(footprint)), 0, top), and every reflection sample with a hit by its
# level (<prefix>.levels: 16 float32 arrays for the far diffuse hits, then 16 for the reflection hits, each width * height,
# row by row). The level is the formula's in every variant, also where the variant reads level 0.
# Per region (output pixels of regions.json, scaled to the render size): the share of the hits at each level, and the
# mean level.
# Usage: python3 levels.py <take dir> <regions.json> <case>
import json, sys
from pathlib import Path
import numpy as np

take = Path(sys.argv[1])
regions = json.loads(Path(sys.argv[2]).read_text())[sys.argv[3]]
meta = json.loads((take / 'census.json').read_text())
w, h = meta['Width'], meta['Height']
vw, vh = meta['Viewport']
a = np.fromfile(take / 'census.levels', '<f4').reshape(32, h, w)
out = {}
for r in regions:
    x0, y0, x1, y1 = r['rect']
    sl = (slice(None), slice(y0 * vh // 1080, y1 * vh // 1080), slice(x0 * vw // 1920, x1 * vw // 1920))
    counts = a[sl].sum(axis=(1, 2))
    row = {}
    for name, c in (('diffuse_far', counts[:16]), ('reflection', counts[16:])):
        total = c.sum()
        if total == 0:
            row[name] = None
            continue
        share = c / total
        row[name] = {'hits': int(total), 'mean_level': round(float((np.arange(16) * share).sum()), 2),
                     'share_by_level': {int(i): round(float(s), 3) for i, s in enumerate(share) if s >= 0.005}}
    out[r['name']] = row
print(json.dumps(out, indent=1))
