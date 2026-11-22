# Tool code of t0-gumm. histmap.py <out.png> <label|history dump|still png>...: the map of the slow diffuse history's
# length in a settled view (scratch-history-dump.patch: float32 per render pixel, row by row, the file name ends in
# -<width>x<height>.f32), beside the still of the same draw scaled to the render size. One row per label. The bands, in
# draws: under 8 (red), 8 to under 16 (orange), 16 to under 24 (yellow), 24 to under 31.5 (light blue), and 31.5 or more
# (dark grey, the cap of 32; the length is a float, and the reprojection's weights leave a settled pixel a little under
# 32). Sky pixels (a length of 0 with no surface) count in "under 8". It prints the share of the pixels in each band.
import re
import sys
import numpy as np
from PIL import Image, ImageDraw

out, specs = sys.argv[1], [s.split('|') for s in sys.argv[2:]]
BANDS = [(0, 8, (220, 50, 50), 'under 8'), (8, 16, (240, 150, 40), '8 to 16'), (16, 24, (240, 220, 60), '16 to 24'),
         (24, 31.5, (120, 180, 230), '24 to 31.5'), (31.5, 1e9, (60, 60, 60), '31.5 or more')]
rows = []
for label, dump, still in specs:
    w, h = map(int, re.search(r'-(\d+)x(\d+)\.f32$', dump).groups())
    L = np.fromfile(dump, dtype=np.float32).reshape(h, w)
    rgb = np.zeros((h, w, 3), np.uint8)
    shares = []
    for lo, hi, colour, name in BANDS:
        m = (L >= lo) & (L < hi)
        rgb[m] = colour
        shares.append(f'{name}: {100 * m.mean():.1f} %')
    print(f'{label}: mean {L.mean():.2f} draws; ' + ', '.join(shares))
    pic = Image.open(still).convert('RGB').resize((w, h), Image.LANCZOS)
    row = Image.new('RGB', (2 * w + 10, h + 24), 'white')
    row.paste(pic, (0, 24)); row.paste(Image.fromarray(rgb), (w + 10, 24))
    d = ImageDraw.Draw(row)
    d.text((4, 4), f'{label}: the still', fill='black')
    d.text((w + 14, 4), f'{label}: slow diffuse history length. ' + '; '.join(shares), fill='black')
    rows.append(row)
legend = Image.new('RGB', (rows[0].width, 28), 'white'); d = ImageDraw.Draw(legend)
for i, (_, _, colour, name) in enumerate(BANDS):
    d.rectangle([10 + 170 * i, 8, 26 + 170 * i, 22], fill=colour); d.text((32 + 170 * i, 9), f'{name} draws', fill='black')
sheet = Image.new('RGB', (rows[0].width, sum(r.height for r in rows) + legend.height), 'white')
y = 0
for r in rows:
    sheet.paste(r, (0, y)); y += r.height
sheet.paste(legend, (0, y))
sheet.save(out)
print(out, sheet.size)
