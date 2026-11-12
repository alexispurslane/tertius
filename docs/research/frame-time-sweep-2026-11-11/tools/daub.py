# daub.py <out folder> <image name inside each variant folder> <boxes.json> <variants...>: the mottle agent's daub score
# (/private/tmp/t7-3e22/daub.py) on this sweep's stills. A daub is a blotch at the scale of a brush stroke in light that
# is smooth in the reference. Display-linear luminance.
#   rel   = shown / level / reference - 1, with level the median ratio of shown to reference; a 5 x 5 median removes specks
#   band  = gauss(rel, 3 px) - gauss(rel, 36 px)
#   mask  = pixels whose reference is smooth (relative gradient at 3 px under 2 % per pixel), visible (above 0.002),
#           and at least 6 px from the mask's edge
#   daub  = the 90th percentile of |band| over the mask; broad = the same of gauss(rel, 36 px)
# The mottle agent calls a daub score above about 0.010 visible.
import sys, json, numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter, binary_erosion, median_filter
out, image, boxes = sys.argv[1], sys.argv[2], json.load(open(sys.argv[3]))
variants = sys.argv[4:]
def lum(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float64) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]
r = median_filter(np.load(f'{out}/reference-mean.npy').astype(np.float64) @ [0.2126, 0.7152, 0.0722], size=5)
rb = gaussian_filter(r, 3)
gy, gx = np.gradient(rb)
smooth = binary_erosion((np.hypot(gx, gy) / np.maximum(rb, 0.01) < 0.02) & (rb > 0.002), iterations=6)
w, w36 = gaussian_filter(smooth.astype(float), 3), gaussian_filter(smooth.astype(float), 36)
names = ['whole image'] + list(boxes)
print(f'smooth share of the image {smooth.mean():.2f}; of each box: ' + ', '.join(f"{n} {smooth[y0:y1, x0:x1].mean():.2f}" for n, (x0, y0, x1, y1) in boxes.items()))
print('| variant | ' + ' | '.join(f'{n}: daub / broad' for n in names) + ' |'); print('|---|' + '---:|' * len(names))
for v in variants:
    s = lum(f'{out}/{v}/{image}.png')
    level = np.median((gaussian_filter(s, 3) / np.maximum(rb, 1e-3))[smooth])
    rel = np.where(smooth, s / level / np.maximum(r, 0.01) - 1, 0)
    rel = np.where(smooth, median_filter(rel, size=5), 0)
    near, far = gaussian_filter(rel, 3) / np.maximum(w, 1e-3), gaussian_filter(rel, 36) / np.maximum(w36, 1e-3)
    band = near - far
    cells = []
    for n in names:
        m = smooth.copy()
        if n != 'whole image':
            x0, y0, x1, y1 = boxes[n]; m[:] = False; m[y0:y1, x0:x1] = smooth[y0:y1, x0:x1]
        cells.append(f"{np.percentile(np.abs(band[m]), 90):.4f} / {np.percentile(np.abs(far[m]), 90):.4f}" if m.sum() > 50 else '-')
    print(f'| {v} | ' + ' | '.join(cells) + ' |')
