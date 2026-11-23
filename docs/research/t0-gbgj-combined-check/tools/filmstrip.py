# Tool code of t0-gbgj. filmstrip.py <out.png> <run M2> <run X2>: five frames (20, 60, 120, 150, 175) of two kept films
# (s/<run>/frames, in the survey's order), M2 left and X2 right, each at 1/3 scale with its label. m.sh runs it in $W/w.
import os, sys
from PIL import Image, ImageDraw
out, runs = sys.argv[1], sys.argv[2:4]
rows = []
for k in (20, 60, 120, 150, 175):
    row = Image.new('RGB', (1290, 360))
    for i, (label, run) in enumerate(zip(('M2', 'X2'), runs)):
        names = sorted(f for f in os.listdir(f's/{run}/frames') if f.endswith('.png'))
        im = Image.open(f's/{run}/frames/' + names[k]).convert('RGB').resize((640, 360))
        ImageDraw.Draw(im).text((6, 6), f'{label} {k}', fill=(255, 80, 80))
        row.paste(im, (650 * i, 0))
    rows.append(row)
sheet = Image.new('RGB', (1290, 370 * len(rows) - 10))
for i, r in enumerate(rows): sheet.paste(r, (0, 370 * i))
sheet.save(out); print(out)
