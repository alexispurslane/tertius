# Tool code of ter-8pco (the ray census). Reads one take of census.py and prints, per region, the shares of the mean
# and of the variance of the diffuse sample's luminance X by class (miss, near hit below 2 m, far hit at 2 m or more), after the
# caps (as the accumulator receives it) and before them, with the check against the slow diffuse history. With a keep
# file it adds the class shares of the brightest 0.1 % of that rectangle's samples. Writes <take>/census-result.json and the
# per-pixel maps <take>/map-miss.png and <take>/map-far.png (share of the mean per render pixel, over the 400 draws).
# Usage: python3 analyze.py <take dir> <regions.json> <case>
#   regions.json: {"<case>": [{"name": ..., "rect": [x0, y0, x1, y1] in output pixels, x1 and y1 exclusive,
#                                "light": "<what lights it>", "keep": <index of TERTIUS_PROBE_CENSUS_KEEP or null>}]}
import json, sys
from pathlib import Path
import numpy as np

take, regions_path, case = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
meta = json.loads((take / 'census.json').read_text())
w, h = meta['Width'], meta['Height']
vw, vh = meta['Viewport']
raw = np.fromfile(take / 'census.sums', dtype='<f8').reshape(24, h, w)
slow = np.fromfile(take / 'census.slow', dtype='<f4').reshape(h, w)
CLASSES = ['miss', 'near', 'far']


def shares(s, mask):
    n, S, Q = s[0][mask], s[1][mask], s[2][mask]
    A = [s[3 + 3 * c][mask] for c in range(3)]
    B = [s[4 + 3 * c][mask] for c in range(3)]
    N = [s[5 + 3 * c][mask] for c in range(3)]
    mean = S / n
    var = Q / n - mean ** 2
    cov = [B[c] / n - (A[c] / n) * mean for c in range(3)]
    total_var = var.sum()
    return {
        'pixels': int(mask.sum()),
        'mean_luminance': float(mean.mean()),
        'mean_variance': float(var.mean()),
        'count_share': {CLASSES[c]: float(N[c].sum() / n.sum()) for c in range(3)},
        'mean_share': {CLASSES[c]: float(A[c].sum() / S.sum()) for c in range(3)},
        'variance_share': {CLASSES[c]: float(cov[c].sum() / total_var) for c in range(3)},
    }


regions = json.loads(Path(regions_path).read_text())[case]
result = {'case': case, 'render': [vw, vh], 'draws': [meta['DrawFirst'], meta['DrawEnd']],
          'stops': [min(meta['Stops']), max(meta['Stops'])], 'regions': []}
for region in regions:
    x0, y0, x1, y1 = region['rect']
    rx0, ry0, rx1, ry1 = x0 * vw // 1920, y0 * vh // 1080, x1 * vw // 1920, y1 * vh // 1080
    mask = np.zeros((h, w), bool)
    mask[ry0:ry1, rx0:rx1] = True
    mask &= raw[0] > 0
    entry = {'name': region['name'], 'light': region['light'], 'rect_output': region['rect'],
             'rect_render': [rx0, ry0, rx1, ry1], 'after_caps': shares(raw[:12], mask),
             'before_caps': shares(raw[12:], mask)}
    census_mean = float((raw[1][mask] / raw[0][mask]).mean())
    entry['check'] = {'census_mean': census_mean, 'slow_history_mean': float(slow[mask].mean()),
                      'ratio': census_mean / float(slow[mask].mean())}
    if region.get('keep') is not None:
        if meta.get('Encoding') == 'bins':
            raw_keep = np.fromfile(take / f"census.keep{region['keep']}",
                                   dtype=np.dtype([('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'), ('ds', '<f4')]))
            rec = np.zeros(len(raw_keep), dtype=np.dtype([('x', '<f4'), ('pre', '<f4'), ('c', 'u1')]))
            rec['x'], rec['pre'] = raw_keep['x'], raw_keep['pre']
            rec['c'] = np.where((raw_keep['bin'] & 63) == 0, 0, np.where((raw_keep['bin'] & 63) <= 22, 1, 2))  # ter-pdw8: bit 6 is the guided flag
        else:
            rec = np.fromfile(take / f"census.keep{region['keep']}", dtype=np.dtype([('x', '<f4'), ('pre', '<f4'), ('c', 'u1')]))
        for key in ('x', 'pre'):
            order = np.argsort(rec[key])[::-1]
            top = order[:max(1, len(rec) // 1000)]
            light = rec[key][top]
            entry[f'brightest_0.1pct_{key}'] = {
                'samples': int(len(top)), 'of': int(len(rec)), 'threshold': float(light.min()),
                'share_of_their_light': {CLASSES[c]: float(light[rec['c'][top] == c].sum() / light.sum()) for c in range(3)},
                'share_of_all_light_in_rect': float(light.sum() / rec[key].sum()),
            }
    result['regions'].append(entry)
(take / 'census-result.json').write_text(json.dumps(result, indent=1))

# Maps: the share of the mean per render pixel, over the viewport, 0 black to 1 white; no surface is dark blue.
from PIL import Image
n = raw[0][:vh, :vw]
S = raw[1][:vh, :vw]
for c, name in ((0, 'miss'), (2, 'far')):
    share = np.where(S > 0, raw[3 + 3 * c][:vh, :vw] / np.maximum(S, 1e-30), 0.0)
    rgb = np.repeat((np.clip(share, 0, 1) * 255).astype(np.uint8)[..., None], 3, axis=2)
    rgb[n == 0] = (0, 0, 80)
    Image.fromarray(rgb).save(take / f'map-{name}.png')
print(json.dumps(result, indent=1))
