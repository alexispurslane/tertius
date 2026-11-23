# Tool code (t1-gout, step 3). rampchart.py <out.png> <table>...: the jamb light of ramp.sh's tables against the window's
# aspect error. Each table is one build at one tick; its file name ramp-<build>-t<tick>.txt names the series. The larger
# of the two jamb means is drawn per window; the grey line is the leak level 0.08.
import os, re, sys
from PIL import Image, ImageDraw

W, H, L, B, T = 1100, 560, 70, 60, 40
COLORS = {('main', '100'): (230, 150, 150), ('main', '6380'): (200, 40, 40),
          ('A', '100'): (140, 180, 230), ('A', '6380'): (30, 90, 200)}
series = []
for path in sys.argv[2:]:
    build, tick = re.match(r'ramp-(\w+)-t(\d+)\.txt', os.path.basename(path)).groups()
    rows = []
    for line in open(path):
        p = line.split()
        if len(p) >= 6 and p[1].isdigit():
            rows.append((float(p[3].rstrip('%')), max(float(p[4]), float(p[5]))))
    series.append((build, tick, sorted(rows)))
lo, hi, top = -0.45, 1.35, 0.20
x = lambda e: L + (e - lo) / (hi - lo) * (W - L - 20)
y = lambda v: T + (top - min(v, top)) / top * (H - T - B)
im = Image.new('RGB', (W, H), 'white')
d = ImageDraw.Draw(im)
for v in (0, 0.05, 0.10, 0.15, 0.20):
    d.line([(L, y(v)), (W - 20, y(v))], fill=(232, 232, 232))
    d.text((20, y(v) - 6), f'{v:.2f}', fill='black')
d.line([(L, y(0.08)), (W - 20, y(0.08))], fill=(150, 150, 150), width=2)
for e in (-0.4, -0.2, 0, 0.2, 0.4, 0.6, 0.8, 1.0, 1.2):
    d.text((x(e) - 16, H - B + 8), f'{e:+.1f} %', fill='black')
for build, tick, rows in series:
    c = COLORS.get((build, tick), (0, 0, 0))
    for e, v in rows:
        r = 4 if build == 'main' else 3
        (d.ellipse if build == 'main' else d.rectangle)([x(e) - r, y(v) - r, x(e) + r, y(v) + r], fill=c)
d.text((L, 10), 'Jamb light (larger of the two jamb crops, display-linear) per window, render scale 2/3: '
               'circles main, squares the fix; light tick 100, dark tick 6380; grey line 0.08', fill='black')
d.text((L, H - 24), 'aspect error of the window, (W / H) / (16 / 9) - 1', fill='black')
im.save(sys.argv[1])
