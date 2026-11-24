# Tool code of t1-cyhy: why guided diffuse draws do not arrive, from the probe's log (65-byte records: the census sample,
# the diffuse record of layers 14 and 15, and layer 22: the target point's depth share u in [0, 1) for a guided draw,
# else -1; the walk's end; the hit pixel's surface point's distance from the ray's line in widths of that pixel's
# footprint at its depth, -1 without a hit). Walk ends: 0 hit, 1 below the geometric plane (not walked), 2 out of steps,
# 3 reached the ray's end (the screen's edge or the ray's end) passing behind no pixel's slab, 4 reached the ray's end
# after passing behind a pixel's slab or past a pixel that faces away.
#   (a) the arriving share of the guided draws in 4 bins of u;
#   (b) the off-line distance of the guided draws that hit another pixel than their target's 3 x 3;
#   (c) the same for the cosine draws that hit;
#   (d) the walk ends of the draws that hit nothing, guided and cosine apart.
# Usage: python3 -I cyhy.py <take dir> <names, comma separated> [reflection]
import json, sys
from pathlib import Path
import numpy as np
take, names = Path(sys.argv[1]), sys.argv[2].split(',')
with_reflection = len(sys.argv) > 3 and sys.argv[3] == 'reflection'
fields = [('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'), ('ds', '<f4')]
if with_reflection:
    fields += [('r6', '<f4', 4), ('r7', '<f4', 4), ('r8', '<f4', 4), ('r9', '<f4', 4)]
fields += [('d14', '<f4', 4), ('d15', '<f4', 4), ('d22', '<f4', 4)]
out = {}
def dist(v):
    return {'count': int(len(v)), 'p50/p90/p99': [float(np.percentile(v, p)) for p in (50, 90, 99)] if len(v) else None,
            'share over 1.5 widths': float((v > 1.5).mean()) if len(v) else None}
for k, name in enumerate(names):
    rec = np.fromfile(take / f'census.keep{k}', dtype=np.dtype(fields))
    f = rec['d15'][:, 2].astype(int) & 7
    g = (f & 1) > 0
    u, end, off = rec['d22'][:, 0], np.round(rec['d22'][:, 1]).astype(int), rec['d22'][:, 2]
    arrive = (f & 2) > 0
    hit = end == 0
    r = {'(a) arriving share by depth share u': {f'{lo:.2f}-{lo + .25:.2f}': [float(arrive[g & (u >= lo) & (u < lo + .25)].mean()),
                                                                              int((g & (u >= lo) & (u < lo + .25)).sum())]
                                                for lo in (0, .25, .5, .75)},
         '(a) all guided draws arriving': float(arrive[g].mean()),
         '(b) guided draws that hit another pixel: off-line distance': dist(off[g & hit & ~arrive]),
         '(b) guided draws that arrive: off-line distance': dist(off[g & arrive]),
         '(c) cosine draws that hit: off-line distance': dist(off[~g & hit])}
    for kind, sel in (('guided', g), ('cosine', ~g)):
        miss = sel & ~hit
        r[f'(d) {kind}: shares of all its draws'] = {
            'hit': float(hit[sel].mean()), 'below the plane': float((end[sel] == 1).mean()),
            'out of steps': float((end[sel] == 2).mean()), 'end, passed behind nothing': float((end[sel] == 3).mean()),
            'end, passed behind a slab or a back face': float((end[sel] == 4).mean())}
    out[name] = r
(take / 'cyhy-result.json').write_text(json.dumps(out, indent=1))
print(json.dumps(out, indent=1))
