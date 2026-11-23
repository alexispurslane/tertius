# Tool code of ter-8pco's lamp-hall part (for t7-3e22). Reads one census take of rect-lamps-f62 with a keep rectangle
# (encoding "bins", which records each draw's diffuse sample with its hit-distance bin and its pre-cap light, and the
# pixel's reflection sample with its hit distance), and prints for the diffuse and for the reflection samples:
#   - the brightest 0.1 % of the rectangle's samples over the counted draws: their count per draw, their class shares
#     (miss, near hit below 2 m, far hit at 2 m or more) by count and by light, their hit-distance range, and for the
#     diffuse samples their light before and after the caps;
#   - the per-pixel mean over the draws: how many pixels of the rectangle stand more than 25 % above the median of
#     their 7 x 7 neighbors (a speck in the mean, which an accumulator cannot average away);
# and counts the specks of the counter's rule (specks.py) in each kept capture PNG of the take.
# ter-pdw8: the bin byte holds the guided flag in bit 6; the result adds the guided share of the brightest 0.1 % of
# the diffuse samples and of the diffuse samples above 5, against the guided share of all diffuse samples.
# Usage: python3 lamp.py <take dir>
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import median_filter, maximum_filter

take = Path(sys.argv[1])
meta = json.loads((take / 'census.json').read_text())
x0, y0, x1, y1 = meta['Keeps'][0]
rec = np.fromfile(take / 'census.keep0',
                  dtype=np.dtype([('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'), ('ds', '<f4')]))
# ter-pdw8: with several keep rectangles, the counts of each draw follow one another; rectangle 0's are every n-th.
counts = meta['KeepCounts'][0::max(len(meta['Keeps']), 1)]
assert len(set(counts)) == 1, 'the rectangle changed its surface pixels between draws'
per_draw = counts[0]
draws = len(rec) // per_draw
CLASSES = ('miss', 'near', 'far')


def edges(b):
    """The hit-distance range of bin b in meters."""
    return 2.0 ** ((b - 21) / 2), 2.0 ** ((b - 20) / 2)


def brightest(light, cls, distance_lo, distance_hi, extra=None):
    order = np.argsort(light)[::-1][:max(1, len(light) // 1000)]
    top = light[order]
    out = {'samples': int(len(order)), 'per_draw': len(order) / draws, 'threshold': float(top.min()),
           'max': float(top.max()), 'share_of_all_light': float(top.sum() / light.sum()),
           'count_share': {CLASSES[c]: float((cls[order] == c).mean()) for c in range(3)},
           'light_share': {CLASSES[c]: float(top[cls[order] == c].sum() / top.sum()) for c in range(3)}}
    hits = cls[order] > 0
    if hits.any():
        out['hit_distance_m'] = [float(distance_lo[order][hits].min()), float(distance_hi[order][hits].max())]
        out['hit_distance_median_m'] = float(np.median(distance_lo[order][hits]))
    if extra is not None:
        out.update(extra(order))
    return out


guided = (rec['bin'].astype(int) >> 6) & 1
bins = rec['bin'].astype(int) & 63
dcls = np.where(bins == 0, 0, np.where(bins <= 22, 1, 2))
lo = np.where(bins > 0, 2.0 ** ((bins - 21) / 2), 0)
hi = np.where(bins > 0, 2.0 ** ((bins - 20) / 2), 0)
scls = np.where(rec['ds'] <= 0, 0, np.where(rec['ds'] < 2, 1, 2))
result = {'render': meta['Viewport'], 'rect_render': [x0, y0, x1, y1], 'samples_per_draw': per_draw, 'draws': draws,
          'stops': [min(meta['Stops']), max(meta['Stops'])]}
result['diffuse_brightest'] = brightest(
    rec['x'], dcls, lo, hi,
    lambda o: {'after_caps_mean': float(rec['x'][o].mean()), 'before_caps_mean': float(rec['pre'][o].mean()),
               'capped_share': float((rec['pre'][o] > rec['x'][o] * 1.0001).mean())})
order = np.argsort(rec['x'])[::-1][:max(1, len(rec) // 1000)]
above = rec['x'] > 5
result['guided_share'] = {'all': float(guided.mean()), 'brightest_0.1pct': float(guided[order].mean()),
                          'above_5': float(guided[above].mean()) if above.any() else None, 'above_5_count': int(above.sum())}
result['diffuse_all'] = {'count_share': {CLASSES[c]: float((dcls == c).mean()) for c in range(3)},
                         'mean': float(rec['x'].mean()), 'capped_share': float((rec['pre'] > rec['x'] * 1.0001).mean())}
result['reflection_brightest'] = brightest(rec['xs'], scls, rec['ds'], rec['ds'])
result['reflection_all'] = {'count_share': {CLASSES[c]: float((scls == c).mean()) for c in range(3)},
                            'mean': float(rec['xs'].mean())}

# The per-pixel means over the draws, on the rectangle's grid of surface pixels (the same pixels in every draw).
grid = np.zeros((y1 - y0, x1 - x0), bool)
# The probe wrote the samples row by row; pixels without a surface were skipped. Rebuild the mask from a draw's count
# only when the rectangle is all surface, which the ceiling around the lamps is.
if per_draw == (y1 - y0) * (x1 - x0):
    for key in ('x', 'xs'):
        mean = rec[key].reshape(draws, y1 - y0, x1 - x0).mean(axis=0)
        bright = mean > 1.25 * median_filter(mean, size=7)
        result[f'mean_specks_{key}'] = int(bright.sum())
        result[f'mean_max_over_median_{key}'] = float((mean / median_filter(mean, size=7)).max())
else:
    result['mean_specks'] = 'not computed: the rectangle holds pixels without a surface'

# The specks of the counter's rule in each kept capture (output rows 0-500, columns 480-1880, gray of the PNG).
shots = {}
for png in sorted(take.glob('c-s*.png')):
    g = np.asarray(Image.open(png).convert('L'), float)[0:500, 480:1880]
    lamp = maximum_filter(g > 200, size=13)
    shots[png.stem] = int(((g - median_filter(g, size=7) > 12) & ~lamp).sum())
result['capture_specks'] = shots
(take / 'lamp-result.json').write_text(json.dumps(result, indent=1))
print(json.dumps(result, indent=1))
