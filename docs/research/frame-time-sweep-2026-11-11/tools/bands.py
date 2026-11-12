# bands.py <out folder> <image name inside each variant folder> <boxes.json> <variants...>: the error against the long
# reference, split by the size of its features. rel = shown / reference - 1 in display-linear luminance.
#   fine   = rel - gauss(rel, 1 px):               features under about 3 output pixels (single render pixels)
#   grain  = gauss(rel, 1 px) - gauss(rel, 3 px):  3 to 9 output pixels (the 2 x 2 blocks and small clumps)
#   clouds = gauss(rel, 3 px) - gauss(rel, 12 px): 9 to 36 output pixels (cloud-like blotches)
#   broad  = gauss(rel, 12 px):                    wider than 36 output pixels, with the mean (bias)
# Each number is the root mean square over the box, in percent of the reference.
import sys, json, numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter
out, image, boxes = sys.argv[1], sys.argv[2], json.load(open(sys.argv[3]))
variants = sys.argv[4:]
def lum(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float64) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]
ref = np.load(f'{out}/reference-mean.npy').astype(np.float64) @ [0.2126, 0.7152, 0.0722]
res = {}
for v in variants:
    rel = lum(f'{out}/{v}/{image}.png') / np.maximum(ref, 0.004) - 1
    g1, g3, g12 = gaussian_filter(rel, 1), gaussian_filter(rel, 3), gaussian_filter(rel, 12)
    parts = {'fine': rel - g1, 'grain': g1 - g3, 'clouds': g3 - g12, 'broad': g12}
    for name, (x0, y0, x1, y1) in boxes.items():
        for band, a in parts.items():
            res[(v, name, band)] = 100 * float(np.sqrt((a[y0:y1, x0:x1] ** 2).mean()))
json.dump([dict(variant=v, box=b, band=k, rms=r) for (v, b, k), r in res.items()], open(f'{out}/bands-{image}.json', 'w'), indent=1)
for name in boxes:
    print(f'\n{name} (percent of the reference)')
    print('| variant | fine | grain | clouds | broad |'); print('|---|---:|---:|---:|---:|')
    for v in variants:
        print(f'| {v} | ' + ' | '.join(f"{res[(v, name, b)]:.2f}" for b in ('fine', 'grain', 'clouds', 'broad')) + ' |')
