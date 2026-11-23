# Tool code of t0-5tyj. walkchart.py <out.png> <label=walk result>...: the walk cabin-walk after the camera stops, one
# panel per region that the walk changes (r01, r10, r11, r12, r20 of the 3 x 3 grid, index 1 + 3 * row + column), with
# each run's mean light of the region over the 200 draws after the stop as % of its control (the same ticks reached
# without a walk). The grey band is the survey's settle tolerance of 5 %, and each run's settle draw is marked with a
# tick on the 100 % line. Drawn with Pillow, because the dev shell has no matplotlib.
import json
import sys
from PIL import Image, ImageDraw

out, runs = sys.argv[1], [a.split('=', 1) for a in sys.argv[2:]]
results = [(label, json.load(open(path))) for label, path in runs]
REGIONS = {1: 'r01 (top middle)', 3: 'r10 (middle left)', 4: 'r11 (center)', 5: 'r12 (middle right)', 6: 'r20 (bottom left)'}
COLORS = [(40, 40, 40), (200, 60, 60), (40, 110, 200), (230, 150, 20)]
PW, PH, L, T, B, LO, HI = 560, 300, 60, 30, 40, 96, 112
im = Image.new('RGB', (PW * 3, PH * 2 + 40), 'white')
d = ImageDraw.Draw(im)
for i, (r, name) in enumerate(REGIONS.items()):
    ox, oy = (i % 3) * PW, (i // 3) * PH
    n = len(results[0][1]['after'])
    x = lambda k: ox + L + k / (n - 1) * (PW - L - 20)
    y = lambda v: oy + T + (HI - v) / (HI - LO) * (PH - T - B)
    d.rectangle([x(0), y(105), x(n - 1), y(max(95, LO))], fill=(232, 232, 232))
    for v in range(LO, HI + 1, 2):
        d.line([x(0), y(v), x(n - 1), y(v)], fill=(200, 200, 200) if v != 100 else (120, 120, 120))
        d.text((ox + 20, y(v) - 6), f'{v}%', fill='black')
    for k in range(0, n, 50):
        d.text((x(k) - 6, oy + PH - B + 4), str(k), fill='black')
    d.text((ox + L, oy + 8), f'{name}: draws after the stop', fill='black')
    for j, (label, res) in enumerate(results):
        pts = [(x(k), y(max(LO, min(HI, 100 * res['after'][k][r] / res['control'][k][r])))) for k in range(n)]
        d.line(pts, fill=COLORS[j], width=2)
        s = res['regionDraws'][r]
        if s:
            d.line([x(s), y(100) - 8, x(s), y(100) + 8], fill=COLORS[j], width=3)
for j, (label, res) in enumerate(results):
    d.line([20 + 260 * j, PH * 2 + 20, 60 + 260 * j, PH * 2 + 20], fill=COLORS[j], width=3)
    d.text((66 + 260 * j, PH * 2 + 14), f"{label}: slowest region {res['settleDraws']} draws", fill='black')
im.save(out)
print(out, im.size)
