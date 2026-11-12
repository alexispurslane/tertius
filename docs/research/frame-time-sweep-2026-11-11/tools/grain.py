# grain.py <out folder> <view> <boxes.json> [variants...]: the reflection prototypes against the long reference.
#   reference = the mean of out/reference/ref-*.png in display-linear light (every pixel traces its own reflection ray)
#   rel       = shown / reference - 1 per pixel, in display-linear luminance
#   grain     = RMS of rel - gauss(rel, 2 px): error finer than about 4 output pixels
#   daub      = 90th percentile of |gauss(rel, 3 px) - gauss(rel, 36 px)|: the band of /private/tmp/t7-3e22/daub.py
#   specks    = pixels per 10,000 whose shown light is above 1.5 times the reference plus 0.01
#   flicker   = mean over the box of the per-pixel standard deviation over the film's frames, over the reference
#   boil      = the same after gauss(frame, 3 px): how much the soft blobs move from frame to frame
#   level     = mean of rel: the change of the mean light
# boxes.json: {"name": [x0, y0, x1, y1], ...} in output pixels.
import sys, json, glob, numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter
out, view, boxes = sys.argv[1], sys.argv[2], json.load(open(sys.argv[3]))
variants = sys.argv[4:]
def lin_rgb(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float64) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
def lum(a): return a @ [0.2126, 0.7152, 0.0722]
frames = sorted(glob.glob(f'{out}/reference/ref-*.png'))
frames = frames[len(frames) // 2:]   # the later half: the history keeps growing, so the later frames are the cleaner ones
ref_rgb = sum(lin_rgb(p) for p in frames) / len(frames)
np.save(f'{out}/reference-mean.npy', ref_rgb.astype(np.float32))
enc = np.where(ref_rgb <= 0.0031308, ref_rgb * 12.92, 1.055 * ref_rgb ** (1 / 2.4) - 0.055)
Image.fromarray((enc * 255 + 0.5).astype(np.uint8)).save(f'{out}/reference-mean.png')
ref = lum(ref_rgb)
print(f'reference: {len(frames)} frames')
rows = []
for v in variants:
    s = lum(lin_rgb(f'{out}/{v}/{view}.png'))
    film = np.stack([lum(lin_rgb(p)) for p in sorted(glob.glob(f'{out}/{v}/{view}film-*.png'))])
    rel = s / np.maximum(ref, 0.004) - 1
    fine = rel - gaussian_filter(rel, 2)
    band = gaussian_filter(rel, 3) - gaussian_filter(rel, 36)
    sd = film.std(axis=0) / np.maximum(ref, 0.004)
    soft = np.stack([gaussian_filter(f, 3) for f in film]).std(axis=0) / np.maximum(ref, 0.004)
    for name, (x0, y0, x1, y1) in boxes.items():
        b = (slice(y0, y1), slice(x0, x1))
        rows.append(dict(variant=v, box=name, grain=float(np.sqrt((fine[b] ** 2).mean())),
            daub=float(np.percentile(np.abs(band[b]), 90)),
            specks=float(1e4 * (s[b] > 1.5 * ref[b] + 0.01).mean()),
            flicker=float(sd[b].mean()), boil=float(soft[b].mean()), level=float(rel[b].mean())))
json.dump(rows, open(f'{out}/grain-{view}.json', 'w'), indent=1)
names = list(boxes)
for metric in ('grain', 'daub', 'specks', 'flicker', 'boil', 'level'):
    print(f'\n{metric}')
    print('| variant | ' + ' | '.join(names) + ' |'); print('|---|' + '---:|' * len(names))
    for v in variants:
        print(f'| {v} | ' + ' | '.join(f"{next(r[metric] for r in rows if r['variant'] == v and r['box'] == n):.4f}" for n in names) + ' |')
