# Tool code of the region take (t0-qvm1). surfaces/light.py: the surface regions of the still exit-cabin, for the daub
# per surface. design-x-acceptance-2026-11-14/tools/daub.py reads REGIONS and mask from a module named light; this
# folder goes first on PYTHONPATH, so daub.py reads these regions and keeps its own formula. The polygons are drawn by
# hand on the 1920 x 1080 still of xfd (s-QD); daub.py cuts each to the truth's smooth pixels, so window openings and
# edges inside a polygon do not count.
import numpy as np
from PIL import Image, ImageDraw

REGIONS = {
    'exit-cabin': {
        'ceiling': [[(480, 0), (1920, 0), (1920, 180), (1360, 280), (700, 280)]],
        'walls': [[(240, 340), (660, 340), (640, 1080), (240, 1080)], [(1240, 340), (1920, 260), (1920, 960), (1400, 600)]],
        'floor': [[(640, 1080), (660, 500), (1200, 490), (1400, 600), (1920, 1020), (1920, 1080)]],
    },
}


def mask(polys):
    im = Image.new('L', (1920, 1080), 0)
    d = ImageDraw.Draw(im)
    for p in polys:
        d.polygon(p, fill=1)
    return np.asarray(im).astype(bool)
