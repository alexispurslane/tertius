# Tool code of ter-pdw8: where S2's share falls below 1 and how much direct light it holds back, from one census dump
# (TERTIUS_PROBE_CENSUS_DUMP_AT) of a V0 take (the diagnosis layer is written in every variant):
#   share-diagnosis      holdsHistory (1 / 0, -1 sky), the diffuse history's length L, S1's share, S2's share
#   hit-diffuse-c-direct-mean  the direct part's running mean (rgb = S / C, alpha = C), as D's stand-in in a still
#   hit-diffuse          the hit light that a diffuse ray reads (V0: D plus the history part)
# Per region of regions.json (output px scaled to the render size): the share of the surface pixels with S2's share
# below 1, and the direct light that S2 holds back there, sum((1 - share) D) / sum(hit light), in the hit light that
# the region's pixels send (the pixels of the region itself, not of what lights it). The same over the whole frame, and
# a map PNG: the hit light in gray, the pixels with S2's share below 1 in red (brighter for a smaller share).
# Usage: python3 sharemap.py <take dir> <draw> <regions.json> <case> <out png>
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image

take, draw = Path(sys.argv[1]), sys.argv[2]
regions = json.loads(Path(sys.argv[3]).read_text())[sys.argv[4]]
meta = json.loads((take / 'census.json').read_text())
vw, vh = meta['Viewport']
sizes = json.loads((take / f'census.d{draw}.dump.json').read_text())
LUM = np.array([0.2126, 0.7152, 0.0722])


def load(name):
    w, h, c = sizes[name]
    return np.fromfile(take / f'census.d{draw}.{name}.f32', '<f4').reshape(h, w, c)[:vh, :vw]


diag, direct, hit = load('share-diagnosis'), load('hit-diffuse-c-direct-mean'), load('hit-diffuse')
surface = diag[..., 0] >= 0
share = np.where(surface, diag[..., 3], 1.0)
d = (direct[..., :3] @ LUM) * direct[..., 3]
light = hit[..., :3] @ LUM
held = (1 - share) * d


def summary(sl):
    s = surface[sl]
    return {'surface_pixels': int(s.sum()), 'share_below_1': float((share[sl][s] < 0.999).mean()) if s.any() else None,
            'direct_held_back_over_hit_light': float(held[sl][s].sum() / max(light[sl][s].sum(), 1e-9)),
            'history_length_median': float(np.median(diag[..., 1][sl][s])) if s.any() else None}


def below(sl):
    # Among the surface pixels with S2's share below 1: how many fail the surface test (no history at the pixel), and
    # the history lengths of those that hold one.
    m = surface[sl] & (share[sl] < 0.999)
    if not m.any():
        return None
    holds = diag[..., 0][sl][m] > 0.5
    lengths = diag[..., 1][sl][m][holds]
    return {'pixels': int(m.sum()), 'fail_surface_test': float(1 - holds.mean()),
            'length_of_holders_p10_p50_p90': [float(v) for v in np.percentile(lengths, [10, 50, 90])] if holds.any() else None}


out = {'whole frame': summary((slice(None), slice(None))), 'below 1, whole frame': below((slice(None), slice(None)))}
for r in regions:
    x0, y0, x1, y1 = r['rect']
    out[r['name']] = summary((slice(y0 * vh // 1080, y1 * vh // 1080), slice(x0 * vw // 1920, x1 * vw // 1920)))
print(json.dumps(out, indent=1))
gray = np.clip(np.log2(np.maximum(light, 1e-4) / max(np.median(light[surface]), 1e-4)) / 8 + 0.6, 0, 1)
rgb = np.stack([gray] * 3, axis=-1)
low = surface & (share < 0.999)
rgb[low] = np.stack([0.4 + 0.6 * (1 - share[low]), 0.1 * np.ones(low.sum()), 0.1 * np.ones(low.sum())], axis=-1)
Image.fromarray((rgb * 255).astype(np.uint8)).resize((vw * 2, vh * 2), Image.NEAREST).save(sys.argv[5])

# S2' (apply-rayend6.py): the share from the surface test alone, 1 where the pixel holds a history, else 1 / phases.
phases = min(int(8 * (1920 / vw) ** 2), int(meta['DrawEnd']))
fails = surface & (diag[..., 0] < 0.5)
s3 = {'surface_pixels': int(surface.sum()), 'share_below_1': float(fails.sum() / max(surface.sum(), 1)),
      'pixels_below_1': int(fails.sum()), 'direct_held_back_over_hit_light':
      float(((1 - 1 / phases) * d)[fails].sum() / max(light[surface].sum(), 1e-9))}
# Where they lie: the share of them with a neighbor (8-connected) at a view distance more than 5 % off, a silhouette.
dist = np.zeros_like(light)
w_, h_, c_ = sizes['share-state'] if 'share-state' in sizes else (0, 0, 0)
ys, xs = np.nonzero(fails)
if len(ys):
    state = load('share-state')
    ref = np.abs(state[..., 0])
    edge = 0
    for y, x in zip(ys, xs):
        win = ref[max(y - 1, 0):y + 2, max(x - 1, 0):x + 2]
        c = ref[y, x]
        edge += bool(np.any(np.abs(win - c) > 0.05 * max(c, 1e-6)))
    s3['on_a_depth_edge'] = edge / len(ys)
print(json.dumps({"S2' (surface test alone)": s3}, indent=1))
rgb2 = np.stack([gray] * 3, axis=-1)
rgb2[fails] = [1.0, 0.1, 0.1]
Image.fromarray((rgb2 * 255).astype(np.uint8)).resize((vw * 2, vh * 2), Image.NEAREST).save(
    sys.argv[5].replace('.png', '-s2prime.png'))
