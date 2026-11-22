# Tool code of t0-gbgj. edgefaults.py <out.png> <frame>... -- <label=run>...: the faults that stay at a render scale of 0.956
# in the kept frames of the switch "cabin" (s/<run>/images/scale/cabin-<frame>.png). Per run and frame it counts bright
# specks in four boxes: the ceiling (x 500 to 1400, y 0 to 350), the near right opening with its jamb and sill (x 1700 to
# 1920, y 440 to 640), and the floor's left and right sides (x 0 to 700 and 1220 to 1920, y 650 to 1080). A speck is a
# group of 8-connected pixels whose display-linear luminance (the 8-bit sRGB decoded) is at least 1.5 times and at least
# 0.01 above the median of its 7 x 7 neighborhood: a relative rule, so that dim specks count, unlike the survey's rule
# with its absolute floor of +0.03. <out.png> is the opening's box at 2x nearest neighbor, one row per frame, one column
# per run. m.sh runs it in $W/w.
import sys
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import label, median_filter
args = sys.argv[1:]; cut = args.index('--')
out, frames, runs = args[0], [int(k) for k in args[1:cut]], [a.split('=', 1) for a in args[cut + 1:]]
BOXES = {'ceiling': [(500, 0, 1400, 350)], 'opening': [(1700, 440, 1920, 640)], 'floor sides': [(0, 650, 700, 1080), (1220, 650, 1920, 1080)]}
OPENING = BOXES['opening'][0]
def linear(rgb):
    c = rgb / 255.0; c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    return c @ np.array([0.2126, 0.7152, 0.0722])
print('specks per box (groups of pixels), per run and frame')
print(f"{'run':18s} {'frame':>5s} " + ' '.join(f'{b:>12s}' for b in BOXES))
rows = []
for k in frames:
    tiles = []
    for name, run in runs:
        rgb = np.asarray(Image.open(f's/{run}/images/scale/cabin-{k:03d}.png').convert('RGB')).astype(float)
        y = linear(rgb); m = median_filter(y, size=7)
        speck = (y >= 1.5 * m) & (y - m >= 0.01)
        counts = [sum(label(speck[y0:y1, x0:x1], structure=np.ones((3, 3)))[1] for x0, y0, x1, y1 in boxes) for boxes in BOXES.values()]
        print(f'{name:18s} {k:5d} ' + ' '.join(f'{c:12d}' for c in counts))
        x0, y0, x1, y1 = OPENING
        t = Image.fromarray(rgb.astype(np.uint8)).crop(OPENING).resize(((x1 - x0) * 2, (y1 - y0) * 2), Image.NEAREST)
        ImageDraw.Draw(t).text((4, 4), f'{name}, {k}', fill=(255, 80, 80)); tiles.append(t)
    row = Image.new('RGB', (sum(t.width + 6 for t in tiles) - 6, tiles[0].height), (40, 40, 40))
    for i, t in enumerate(tiles): row.paste(t, ((t.width + 6) * i, 0))
    rows.append(row)
sheet = Image.new('RGB', (rows[0].width, sum(r.height + 6 for r in rows) - 6), (40, 40, 40))
for i, r in enumerate(rows): sheet.paste(r, (0, (r.height + 6) * i))
sheet.save(out); print(out)
