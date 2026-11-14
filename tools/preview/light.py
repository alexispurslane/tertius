# light.py <truth> <variant...>: the light of each variant's shown still (draw 132) and of its own long reference, per
# surface region, as a share of the truth's long reference, in scene light (the Hable curve inverted per channel).
# The truth is a run with both history clamps off, so its long reference is the plain mean of the samples.
# Regions are polygons drawn by hand on the 1920 x 1080 references; each is cut to the truth's smooth pixels (daub.py).
import sys, numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, binary_erosion

def hable_f(x):
    a, b, c, d, e, f = 0.15, 0.50, 0.10, 0.20, 0.02, 0.30
    return (x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f) - e / f
GRID = np.linspace(0, 40, 400001)
CURVE = hable_f(2.0 * GRID) / hable_f(11.2)

def scene(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(float) / 255
    a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
    a = np.interp(a, CURVE, GRID)
    return a @ [0.2126, 0.7152, 0.0722]

ROOM = {  # bay-sunbeam and docking-bay share the camera
    'walls': [[(0, 0), (295, 0), (480, 600), (520, 1080), (0, 1080)],
              [(295, 0), (1025, 0), (1012, 495), (480, 600)],
              [(1025, 0), (1920, 0), (1920, 1080), (1690, 1080), (1012, 495)]],
    'floor': [[(480, 600), (1012, 495), (1690, 1080), (520, 1080)]],
}
REGIONS = {
    'rect-lamps': {
        'ceiling': [[(0, 0), (1920, 0), (1920, 6), (1185, 413), (735, 413), (0, 6)]],
        'walls': [[(0, 6), (735, 413), (735, 578), (0, 710)], [(1920, 6), (1185, 413), (1185, 578), (1920, 710)],
                  [(735, 413), (1185, 413), (1185, 578), (735, 578)]],
    },
    'bay-sunbeam': ROOM, 'docking-bay': ROOM,
}

def mask(polys):
    im = Image.new('L', (1920, 1080), 0); d = ImageDraw.Draw(im)
    for p in polys:
        d.polygon(p, fill=1)
    return np.asarray(im).astype(bool)

def smooth_of(r):
    rb = gaussian_filter(r, 3); gy, gx = np.gradient(rb)
    return binary_erosion((np.hypot(gx, gy) / np.maximum(rb, 0.01) < 0.02) & (rb > 0.002), iterations=6)

if __name__ == '__main__':
    truth, variants = sys.argv[1], sys.argv[2:]
    for case, regions in REGIONS.items():
        try:
            t = scene(f's/{truth}/images/still/{case}-reference.png')
        except FileNotFoundError:
            continue
        smooth = smooth_of(t)
        for v in variants:
            try:
                s = scene(f's/{v}/images/still/{case}.png'); r = scene(f's/{v}/images/still/{case}-reference.png')
            except FileNotFoundError:
                continue
            cells = []
            for name, polys in regions.items():
                m = mask(polys) & smooth
                cells.append(f"{name} shown {100 * s[m].mean() / t[m].mean():6.1f}% settled {100 * r[m].mean() / t[m].mean():6.1f}%")
            print(f"{case:12s} {v:10s} " + "   ".join(cells))
