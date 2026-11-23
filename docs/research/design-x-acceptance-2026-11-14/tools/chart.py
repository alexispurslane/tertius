# chart.py <out.png> <title> <main.json> <branch.json> <fixed.json>: the cabin lamp switch, light over draws, for the whole image
# and the center region: each build's switch run (thick) and its two controls (thin), as a share of the fixed build's
# lamps-on control at the same draw. Drawn with Pillow, because the dev shell has no matplotlib.
import json, sys
from PIL import Image, ImageDraw, ImageFont
out, title = sys.argv[1], sys.argv[2]
names = ["main", "branch before the fix", "branch with the fix"]
colors = {"main": (120, 120, 120), "branch before the fix": (217, 95, 2), "branch with the fix": (27, 120, 55)}
series = {n: json.load(open(p))["series"] for n, p in zip(names, sys.argv[3:6])}
ref = series["branch with the fix"]["on"]
W, H, PW, PH, L, T = 1500, 640, 640, 480, 80, 70
im = Image.new("RGB", (W, H), "white"); d = ImageDraw.Draw(im)
try:
    font = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 17); small = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 14)
except OSError:
    font = small = ImageFont.load_default()
d.text((L, 12), title, fill="black", font=font)
x0, x1 = 100, 459
for panel, (k, name) in enumerate(((0, "whole image"), (5, "center region of the image"))):
    ox = L + panel * (PW + 90)
    vals = [s[r][t][k] / ref[t][k] for s in series.values() for r in ("switch", "on", "off") for t in range(x0, x1 + 1)]
    lo, hi = min(vals) - 0.02, max(vals) + 0.02
    X = lambda t: ox + (t - x0) / (x1 - x0) * PW
    Y = lambda v: T + PH - (v - lo) / (hi - lo) * PH
    d.rectangle([ox, T, ox + PW, T + PH], outline="black")
    v = int(lo * 20) / 20
    while v <= hi:
        if v >= lo:
            d.line([ox, Y(v), ox + PW, Y(v)], fill=(225, 225, 225)); d.text((ox - 46, Y(v) - 8), f"{v:.2f}", fill="black", font=small)
        v += 0.05
    for t, text in ((160, "lamps off"), (360, "lamps on")):
        d.line([X(t), T, X(t), T + PH], fill="black"); d.text((X(t) + 4, T + PH - 20), text, fill="black", font=small)
    for t in (150, 200, 250, 300, 350, 400, 450):
        d.text((X(t) - 12, T + PH + 4), str(t), fill="black", font=small)
    for n, s in series.items():
        for r, w in (("on", 1), ("off", 1), ("switch", 3)):
            pts = [(X(t), Y(s[r][t][k] / ref[t][k])) for t in range(x0, x1 + 1)]
            d.line(pts, fill=colors[n], width=w)
    d.text((ox + 6, T + 6), name, fill="black", font=font)
    d.text((ox + PW / 2 - 60, T + PH + 24), "draw of the film", fill="black", font=small)
y = T + PH + 50
x = L
for n in names:
    d.line([x, y + 8, x + 40, y + 8], fill=colors[n], width=3); d.text((x + 48, y), n, fill="black", font=small); x += 260
d.text((x, y), "thick: switch run. thin: the build's controls with the lamps on and off", fill="black", font=small)
d.text((L, 40), "Light as a share of the fixed branch's lamps-on control (1.00 = the level that the fixed branch holds in a still)", fill="black", font=small)
im.save(out); print(out)
