# Tool code of t0-gbgj. jambsheet.py <out.png> <zoom> <label=run>...: the sheet of the switch's six kept moments
# (scalefilm.py: film frames 150, 180, 220, 355, 380 and 455), one row per moment and one column per run. Each tile is
# the jamb crop (x 1770 to 1808, y 495 to 585 of the 1920 x 1080 output: the lit side faces of the nearest window
# opening on the right wall) above the floor crop (x 0 to 450, y 840 to 1050: the floor's left side), the acceptance
# lead's rectangles, at <zoom> nearest neighbor, with a label. Printed per run and moment, in display-linear luminance
# (the 8-bit sRGB decoded, 0 to 1): the jamb crop's mean, and the floor crop's speck count by the survey's own rule
# (renderer-survey.py, specks: ter-9371's speck mask, groups per 10,000 visible pixels) against a reference without
# specks, the crop's own 5 x 5 median. Also per run: the x of two fixed vertical edges at the first moment
# (filmmotion.edge_x): the back wall's left and right corners, and their distance. Beside <out.png>, it writes
# <out>-jamb.png: the jamb crop alone at 6x nearest neighbor, the same rows and columns. m.sh runs it in $W/w.
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import median_filter
from filmmotion import edge_x, survey
JAMB, FLOOR = (1770, 495, 1808, 585), (0, 840, 450, 1050)
EDGES = (('back wall left corner', (715, 430, 755, 560)), ('back wall right corner', (1165, 430, 1205, 560)))
MOMENTS = ((150, '10 before off'), (180, '20 after off'), (220, '60 after off'), (355, '195 after off'),
           (380, '20 after on'), (455, '95 after on'))
out, zoom = sys.argv[1], int(sys.argv[2])
runs = [a.split('=', 1) for a in sys.argv[3:]]
for label, run in runs:
    y = survey.luminance(Path(f's/{run}/images/scale/cabin-150.png'))
    xs = [edge_x(y, box) for _, box in EDGES]
    print(f"{label}: edges at frame 150: " + ", ".join(f"{n} x {x:.2f}" for (n, _), x in zip(EDGES, xs))
          + f"; distance {xs[1] - xs[0]:.2f} px")
print("jamb mean luminance / floor specks per 10,000 visible pixels")
print(f"{'moment':15s} " + " ".join(f"{label:>20s}" for label, _ in runs))
rows, jrows = [], []
for k, moment in MOMENTS:
    cells, tiles, jtiles = [], [], []
    for label, run in runs:
        p = f's/{run}/images/scale/cabin-{k:03d}.png'
        y = survey.luminance(Path(p))
        jamb = y[JAMB[1]:JAMB[3], JAMB[0]:JAMB[2]]; floor = y[FLOOR[1]:FLOOR[3], FLOOR[0]:FLOOR[2]]
        cells.append(f"{jamb.mean():.4f} / {survey.specks(floor, median_filter(floor, 5)):5.1f}")
        im = Image.open(p).convert('RGB')
        j = im.crop(JAMB).resize(((JAMB[2] - JAMB[0]) * zoom * 2, (JAMB[3] - JAMB[1]) * zoom * 2), Image.NEAREST)
        f = im.crop(FLOOR).resize(((FLOOR[2] - FLOOR[0]) * zoom, (FLOOR[3] - FLOOR[1]) * zoom), Image.NEAREST)
        tile = Image.new('RGB', (max(j.width, f.width), 22 + j.height + 4 + f.height), (40, 40, 40))
        tile.paste(j, (0, 22)); tile.paste(f, (0, 22 + j.height + 4))
        ImageDraw.Draw(tile).text((4, 4), f'{label}, frame {k}: {moment}', fill=(255, 255, 255))
        tiles.append(tile)
        jt = Image.new('RGB', ((JAMB[2] - JAMB[0]) * 6, 22 + (JAMB[3] - JAMB[1]) * 6), (40, 40, 40))
        jt.paste(im.crop(JAMB).resize(((JAMB[2] - JAMB[0]) * 6, (JAMB[3] - JAMB[1]) * 6), Image.NEAREST), (0, 22))
        ImageDraw.Draw(jt).text((4, 4), f'{label}, {k}', fill=(255, 255, 255))
        jtiles.append(jt)
    print(f"{moment:15s} " + " ".join(f"{c:>20s}" for c in cells))
    row = Image.new('RGB', (sum(t.width + 6 for t in tiles), max(t.height for t in tiles)), (40, 40, 40)); x = 0
    for t in tiles: row.paste(t, (x, 0)); x += t.width + 6
    rows.append(row)
    jrow = Image.new('RGB', (sum(t.width + 6 for t in jtiles), jtiles[0].height), (40, 40, 40)); x = 0
    for t in jtiles: jrow.paste(t, (x, 0)); x += t.width + 6
    jrows.append(jrow)
for name, rs in ((out, rows), (out.replace('.png', '-jamb.png'), jrows)):
    sheet = Image.new('RGB', (rs[0].width, sum(r.height + 6 for r in rs)), (40, 40, 40)); y = 0
    for r in rs: sheet.paste(r, (0, y)); y += r.height + 6
    sheet.save(name); print(name)
