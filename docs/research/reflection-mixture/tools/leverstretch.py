# Tool code of t1-nyjl: the diffuse lever's blobs made visible. As levercrops.py, but each crop shows its display-linear
# luminance against its own 36 px Gaussian blur, rel = img / blur - 1, stretched as 0.5 + 12 rel. The blur is taken over
# the whole image, so a crop's edge sees its surround. Usage: python3 -I leverstretch.py <out dir>
import sys
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter
G = '/private/tmp/t1-wl7c/guide/stills'; F = '/private/tmp/t1-wl7c/fe63'
takes = [('cap 32, all writers (main)', f'{G}/bay-diffuse-0.25'), ('cap 128, all writers', f'{F}/d128-on'),
         ('cap 128, region evidence off', f'{F}/d128-noregion')]
regions = {'floor': ((600, 650, 1200, 950), (0.0108, 0.0093, 0.0041)),
           'back-wall': ((560, 420, 980, 560), (0.0096, 0.0075, 0.0040)),
           'right-wall': ((1300, 100, 1800, 500), (0.0065, 0.0063, 0.0022))}
lin = lambda a: np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]
imgs = {t: lin(np.asarray(Image.open(f'{t}/c-s19.png').convert('RGB')) / 255) for _, t in takes}
rels = {t: v / np.maximum(gaussian_filter(v, 36), 1e-6) - 1 for t, v in imgs.items()}
for name, (box, blobs) in regions.items():
    w, h = box[2] - box[0], box[3] - box[1]
    s = Image.new('RGB', (3 * (w + 8) - 8, h + 34), 'white'); d = ImageDraw.Draw(s)
    for i, ((label, t), b) in enumerate(zip(takes, blobs)):
        r = rels[t][box[1]:box[3], box[0]:box[2]]
        s.paste(Image.fromarray((np.clip(0.5 + 12 * r, 0, 1) * 255).astype(np.uint8)).convert('RGB'), (i * (w + 8), 34))
        d.text((i * (w + 8) + 4, 3), f'diffuse alone, {label}', fill='black')
        d.text((i * (w + 8) + 4, 17), f'blob {b:.4f}; x12 around its 36 px blur', fill='black')
    s.save(f'{sys.argv[1]}/lever-{name}-x12.png')
