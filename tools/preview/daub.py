# daub.py: a score for daubs, blotches at the scale of a brush stroke in light that should be smooth, apart from the slow
# drift of a light that still settles. From a survey still and its long reference (display-linear luminance, 1920 x 1080):
#   (REF=<variant> takes that variant's reference, such as the truth run with both clamps off, for every variant)
#   rel   = shown / level / reference - 1, the relative error, with level the median ratio (as the survey's blotch)
#   (a 5 x 5 median of rel removes specks first)
#   band  = gauss(rel, 3 px) - gauss(rel, 36 px): only the error between about 2 and 24 render pixels across
#   mask  = pixels whose reference is smooth: its relative gradient at 3 px is under 2 % per output pixel, away from
#           edges of geometry and of shadows, and visible (reference above 0.002)
#   daub  = the 90th percentile of |band| over the mask; broad = the same of gauss(rel, 36 px): broad error in one still,
#           a permanent bias of the light's shape as well as slow settling (settling needs the capture sequence)
import sys, os, json, numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter, binary_erosion, median_filter
def lin(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(float) / 255
    a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
    return a @ [0.2126, 0.7152, 0.0722]
for v, case in (x.split(':') for x in sys.argv[1:]):
    s = lin(f's/{v}/images/still/{case}.png'); r = lin(f's/{__import__("os").environ.get("REF", v)}/images/still/{case}-reference.png')
    if os.environ.get('REF'):
        # The truth reference holds single fireflies that 20 captures do not average away; a 5 x 5 median removes them
        # from the anchor (they are below the daub band's scale; their light stays in stillsig's linear means).
        r = median_filter(r, size=5)
    rb = gaussian_filter(r, 3)
    gy, gx = np.gradient(rb)
    smooth = (np.hypot(gx, gy) / np.maximum(rb, 0.01) < 0.02) & (rb > 0.002)
    smooth = binary_erosion(smooth, iterations=6)
    level = np.median((gaussian_filter(s, 3) / np.maximum(rb, 1e-3))[smooth])
    rel = np.where(smooth, s / level / np.maximum(r, 0.01) - 1, 0)
    # A 5 x 5 median first, so that a speck of a few pixels adds nothing to the daub band.
    rel = np.where(smooth, median_filter(rel, size=5), 0)
    w = gaussian_filter(smooth.astype(float), 3); w36 = gaussian_filter(smooth.astype(float), 36)
    near = gaussian_filter(rel, 3) / np.maximum(w, 1e-3); far = gaussian_filter(rel, 36) / np.maximum(w36, 1e-3)
    band = near - far
    # Per surface as well (light.py's hand-drawn regions): a cloud on a small share of the mask can hide under the whole p90.
    from light import REGIONS, mask as region_mask
    parts = "".join(f"  {name} {np.percentile(np.abs(band[smooth & region_mask(polys)]), 90):.4f}"
                    for name, polys in REGIONS.get(case, {}).items() if (smooth & region_mask(polys)).any())
    print(f"{v:10s} {case:12s} daub {np.percentile(np.abs(band[smooth]), 90):.4f}  broad {np.percentile(np.abs(far[smooth]), 90):.4f}  smooth share {smooth.mean():.2f}{parts}")
