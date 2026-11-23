# Tool code of ter-pdw8: S2's premise on the lamp hall's burst draw, and the mixing of two surfaces in row 314's pixels.
# Reads the census dumps of draws <a> and <b> (TERTIUS_PROBE_CENSUS_DUMP_AT, apply-rayend4.py and -5.py):
#   hit-diffuse      the hit light's diffuse layer that the rays read (with S1 or S2 the share is already applied)
#   share-diagnosis  per pixel: holdsHistory (1 / 0, -1 sky), the diffuse history's length L, S1's share, S2's share
#   share-state      S1's state: the reference distance (sign: the normal's z), q, the normal's x and y
# In the window of render columns <x0>..<x1> and rows <y0>..<y1>, for each draw: the pixels whose direct part made the
# hit light bright (diffuse hit light above <bright>, default 20) and, for those pixels, the four diagnosis values and
# S1's q; then the same pixels in the other draw. The pixel row of the bright ones is the sill's face in the burst draw.
# Usage: python3 premise.py <take dir> <draw a> <draw b> <x0,y0,x1,y1 render px> [bright]
import json, sys
from pathlib import Path
import numpy as np

take, a, b = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
x0, y0, x1, y1 = map(int, sys.argv[4].split(','))
bright = float(sys.argv[5]) if len(sys.argv) > 5 else 20.0
LUM = np.array([0.2126, 0.7152, 0.0722])


def load(draw):
    sizes = json.loads((take / f'census.d{draw}.dump.json').read_text())
    out = {}
    for name in ('hit-diffuse', 'share-diagnosis', 'share-state'):
        w, h, c = sizes[name]
        out[name] = np.fromfile(take / f'census.d{draw}.{name}.f32', '<f4').reshape(h, w, c)[y0:y1, x0:x1]
    return out


da, db = load(a), load(b)
result = {}
for draw, d, other in ((a, da, db), (b, db, da)):
    lum = d['hit-diffuse'][..., :3] @ LUM
    mask = lum > bright
    r = {'bright_pixels': int(mask.sum())}
    if mask.any():
        ys, xs = np.nonzero(mask)
        r['rows_render'] = sorted({int(y + y0) for y in ys})
        r['columns_render'] = [int(xs.min() + x0), int(xs.max() + x0)]
        r['hit_light_median'] = float(np.median(lum[mask]))
        diag = d['share-diagnosis'][mask]
        r['holds_history_share'] = float((diag[:, 0] > 0.5).mean())
        r['history_length_median'] = float(np.median(diag[:, 1]))
        r['history_length_p10_p90'] = [float(v) for v in np.percentile(diag[:, 1], [10, 90])]
        r['share_s1_median'] = float(np.median(diag[:, 2]))
        r['share_s2_median'] = float(np.median(diag[:, 3]))
        r['s1_q_median'] = float(np.median(d['share-state'][mask][:, 1]))
        o = other['share-diagnosis'][mask]
        r['same_pixels_in_other_draw'] = {
            'hit_light_median': float(np.median(other['hit-diffuse'][..., :3][mask] @ LUM)),
            'holds_history_share': float((o[:, 0] > 0.5).mean()),
            'history_length_median': float(np.median(o[:, 1])),
            'share_s1_median': float(np.median(o[:, 2])), 'share_s2_median': float(np.median(o[:, 3]))}
    result[f'draw {draw}'] = r
print(json.dumps(result, indent=1))
