# levelchart.py <out.png> <reference result> <label=variant result>...: the cabin switch's center region r11 over the film
# frames 140 to 459, as % of the reference's control in the same lamp state (lamps off from frame 160, on from 360).
# Drawn with Pillow, because the dev shell has no matplotlib.
import json, sys
from PIL import Image, ImageDraw
out, ref = sys.argv[1], json.load(open(sys.argv[2]))["series"]
W, H, L, T, B = 1300, 520, 70, 20, 60
LO, HI, T0, T1 = 50, 150, 140, 459
COLORS = [(40, 40, 40), (200, 60, 60), (40, 110, 200), (230, 150, 20), (120, 170, 60), (150, 70, 190)]
im = Image.new("RGB", (W, H), "white"); d = ImageDraw.Draw(im)
x = lambda t: L + (t - T0) / (T1 - T0) * (W - L - 20)
y = lambda v: T + (HI - v) / (HI - LO) * (H - T - B)
for v in range(LO, HI + 1, 10):
    d.line([(L, y(v)), (W - 20, y(v))], fill=(225, 225, 225) if v != 100 else (120, 120, 120)); d.text((8, y(v) - 6), f"{v} %", fill="black")
for t in range(T0, T1 + 1, 20):
    d.text((x(t) - 10, H - B + 6), str(t), fill="black")
for t, c in ((160, (140, 140, 140)), (360, (140, 140, 140)), (220, (200, 200, 200)), (420, (200, 200, 200))):
    d.line([(x(t), T), (x(t), H - B)], fill=c)
d.text((L, H - 28), "film frame. Lamps off at 160, on at 360; light lines 60 draws after each switch. r11 light as % of the reference's control.", fill="black")
for i, arg in enumerate(sys.argv[3:]):
    label, path = arg.split("=", 1)
    sw = json.load(open(path))["series"]["switch"]
    pts = [(x(t), y(max(LO, min(HI, 100 * sw[t][5] / ref["off" if 160 <= t < 360 else "on"][t][5])))) for t in range(T0, T1 + 1)]
    d.line(pts, fill=COLORS[i % len(COLORS)], width=2)
    d.rectangle([W - 230, T + 10 + 18 * i, W - 214, T + 22 + 18 * i], fill=COLORS[i % len(COLORS)]); d.text((W - 208, T + 10 + 18 * i), label, fill="black")
im.save(out); print(out)
