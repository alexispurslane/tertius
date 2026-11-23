# Tool code of ter-pdw8: measure 6, the shown picture with the unchanged denoiser, and the crops.
# Each take keeps 20 continued captures c-s00 .. c-s19, 33 draws apart (draws 33 .. 660). In display-linear luminance
# (the sRGB code undone), per variant:
#   own reference = the mean of captures c-s08 .. c-s19 (draws 297 .. 660) of the same take
#   shown         = capture c-s09 (draw 330); the second V0 picture is c-s10 of V0 (draw 363), 33 draws later, which
#                   is not a multiple of the jitter's phase count, for the spread between two V0 pictures
#   rel           = shown / own reference - 1 over the visible pixels (own reference above 0.002)
#   grain         = RMS of rel - gauss(rel, 2 px): error finer than about 4 output pixels
#   daub          = 90th percentile of |gauss(rel, 3 px) - gauss(rel, 36 px)|, the band of t7-3e22's daub score
#   boil          = mean over the box of the per-pixel standard deviation of gauss(capture, 3 px) over c-s08 .. c-s19,
#                   over the own reference: how much the soft blobs move from capture to capture
#   level         = mean of own reference / V0's reference - 1 over the box: the variant's bias against V0
#   spread        = 'v0 second picture' (capture c-s10, 33 draws later) for grain and daub, and 'v0 boil first
#                   half' / 'second half' (boil over c-s08 .. c-s13 and c-s14 .. c-s19) for boil. A second V0 run
#                   that shifts only the census's counted draws renders the same pictures.
# Every measure is per box (the regions of regions.json for the case, output pixels).
# Crops: for each box of <crops.json>, the shown still of each variant at 1:1 beside V0's reference, left to right:
# V0's reference, V0, V1, V2, V3, 4 px white between.
# Usage: python3 pics.py <takes dir> <prefix, e.g. lamp1072> <case> <regions.json> <crops.json> <out dir>
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter

takes, prefix, case = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
regions = json.loads(Path(sys.argv[4]).read_text())[case]
crops = json.loads(Path(sys.argv[5]).read_text())
out = Path(sys.argv[6])
out.mkdir(parents=True, exist_ok=True)
REF = range(8, 20)


def lin(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(np.float64) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)


def lum(a):
    return a @ [0.2126, 0.7152, 0.0722]


variants = [v for v in ('v0', 'v1', 'v2', 'v3') if (takes / f'{prefix}-{v}' / 'c-s19.png').exists()]
data = {}
for v in variants:
    t = takes / f'{prefix}-{v}'
    caps = [lum(lin(t / f'c-s{i:02d}.png')) for i in REF]
    data[v] = {'ref': np.mean(caps, axis=0), 'caps': caps, 'shown': lum(lin(t / 'c-s09.png')),
               'shown_rgb': np.asarray(Image.open(t / 'c-s09.png').convert('RGB'))}
    data[v]['ref_rgb'] = None
v0 = data['v0']
# V0's reference in display code for the crops.
v0_rgb = np.mean([lin(takes / f'{prefix}-v0' / f'c-s{i:02d}.png') for i in REF], axis=0)
v0_code = (np.where(v0_rgb <= 0.0031308, v0_rgb * 12.92, 1.055 * v0_rgb ** (1 / 2.4) - 0.055) * 255 + 0.5).astype(np.uint8)


def measures(shown, ref, caps, box, base):
    x0, y0, x1, y1 = box
    sl = (slice(y0, y1), slice(x0, x1))
    vis = ref > 0.002
    rel = np.where(vis, shown / np.maximum(ref, 1e-6) - 1, 0)
    fine = rel - gaussian_filter(rel, 2)
    band = np.abs(gaussian_filter(rel, 3) - gaussian_filter(rel, 36))
    blurred = np.std([gaussian_filter(c, 3) for c in caps], axis=0) / np.maximum(gaussian_filter(ref, 3), 1e-6)
    m = vis[sl]
    return {'grain': float(np.sqrt(np.mean(fine[sl][m] ** 2))), 'daub': float(np.percentile(band[sl][m], 90)),
            'boil': float(np.mean(blurred[sl][m])),
            'level': float(ref[sl][m].mean() / base[sl][m].mean() - 1)}


table = {}
for r in regions:
    box = r['rect']
    row = {v: measures(data[v]['shown'], data[v]['ref'], data[v]['caps'], box, v0['ref']) for v in variants}
    row['v0 second picture'] = measures(lum(lin(takes / f'{prefix}-v0' / 'c-s10.png')), v0['ref'], v0['caps'], box,
                                        v0['ref'])
    # The spread of boil within V0: boil over captures c-s08 .. c-s13 and over c-s14 .. c-s19, each against its own mean.
    for half, part in (('v0 boil first half', v0['caps'][:6]), ('v0 boil second half', v0['caps'][6:])):
        row[half] = measures(v0['shown'], np.mean(part, axis=0), part, box, v0['ref'])
    table[r['name']] = row
(out / f'{prefix}-pictures.json').write_text(json.dumps(table, indent=1))
for name, row in table.items():
    print(name)
    for v, m in row.items():
        print(f'  {v:18s} grain {m["grain"]:.4f} daub {m["daub"]:.4f} boil {m["boil"]:.4f} level {m["level"]:+.4f}')

for name, (x0, y0, x1, y1) in crops.items():
    parts = [v0_code[y0:y1, x0:x1]] + [data[v]['shown_rgb'][y0:y1, x0:x1] for v in variants]
    gap = np.full((y1 - y0, 4, 3), 255, np.uint8)
    row = parts[0]
    for p in parts[1:]:
        row = np.concatenate([row, gap, p], axis=1)
    Image.fromarray(row).save(out / f'{prefix}-crop-{name}.png')
