# Tool code of t0-5tyj (ticket t4f-71qp; from levelchart.py of t0-mzhr). switchchart.py <out.png> <size log>
# <label=switch result>...: the switch cabin over the film frames 140 to 459, in three panels. Top: the center region
# r11, middle: the corner region r00, each as % of the same run's own control in the same lamp state ("off" from frame
# 160, "on" from 360), so that a run at another render scale is set against controls at its own scale. Bottom: the render
# scale of each draw of the switch film from the size log (scratch-dynamic-scale.patch, size-193-393.txt; film frame k
# is draw 33 + k). Drawn with Pillow, because the dev shell has no matplotlib.
import json
import sys
from PIL import Image, ImageDraw

out, size_log, runs = sys.argv[1], sys.argv[2], [a.split('=', 1) for a in sys.argv[3:]]
W, PH, SH, L, T0, T1, STILL = 1300, 360, 160, 70, 140, 459, 33
COLORS = [(40, 40, 40), (200, 60, 60), (40, 110, 200), (150, 150, 150), (230, 150, 20), (120, 170, 60), (150, 70, 190)]
im = Image.new('RGB', (W, 2 * PH + SH + 60), 'white')
d = ImageDraw.Draw(im)
x = lambda t: L + (t - T0) / (T1 - T0) * (W - L - 20)


def panel(top, index, name, lo, hi):
    y = lambda v: top + 20 + (hi - v) / (hi - lo) * (PH - 50)
    for v in range(lo, hi + 1, 10):
        d.line([(L, y(v)), (W - 20, y(v))], fill=(225, 225, 225) if v != 100 else (120, 120, 120))
        d.text((8, y(v) - 6), f'{v} %', fill='black')
    for t in (160, 360, 220, 420):
        d.line([(x(t), top + 20), (x(t), top + PH - 30)], fill=(140, 140, 140) if t in (160, 360) else (205, 205, 205))
    d.text((L, top + 4), f'{name} as % of its own control. Lamps off at film frame 160, on at 360; light lines 60 draws '
                         f'after each switch. Grey band: 5 %.', fill='black')
    d.rectangle([x(T0), y(105), x(T1), y(95)], outline=(190, 190, 190))
    for i, (label, path) in enumerate(runs):
        series = json.load(open(path))['series']
        sw = series['switch']
        pts = [(x(t), y(max(lo, min(hi, 100 * sw[t][index] / series['off' if 160 <= t < 360 else 'on'][t][index]))))
               for t in range(T0, T1 + 1)]
        d.line(pts, fill=COLORS[i % len(COLORS)], width=2)
        d.rectangle([W - 260, top + 30 + 18 * i, W - 244, top + 42 + 18 * i], fill=COLORS[i % len(COLORS)])
        d.text((W - 238, top + 30 + 18 * i), label, fill='black')


panel(0, 5, 'r11 (center)', 60, 150)
panel(PH, 1, 'r00 (top left corner)', 60, 150)
top = 2 * PH
scales = [float(line.split()[3]) for line in open(size_log)]
y = lambda v: top + 20 + (1.0 - v) / 0.5 * (SH - 30)
for v in (0.5, 0.667, 1.0):
    d.line([(L, y(v)), (W - 20, y(v))], fill=(225, 225, 225))
    d.text((8, y(v) - 6), f'{v:.3f}', fill='black')
d.text((L, top + 4), f'render scale of each draw of the dynamic run ({size_log.split("/")[-2]}); a change of the render '
                     f'size restarts the region test, which needs 36 draws at one size', fill='black')
pts = [(x(t), y(scales[STILL + t])) for t in range(T0, T1 + 1) if STILL + t < len(scales)]
d.line(pts, fill=(200, 60, 60), width=1)
for t in range(T0, T1 + 1, 20):
    d.text((x(t) - 10, top + SH + 4), str(t), fill='black')
d.text((L, top + SH + 24), 'film frame (draw = 33 + frame)', fill='black')
im.save(out)
print(out, im.size)
