# bars.py <rows.json> <out.png> <title>: horizontal bars. rows.json: [{"label":..., "value": ms, "idle": ms, "group": name}, ...]
# The solid part of a bar is the pass's work and the pale part is the idle time before it.
import sys, json
from PIL import Image, ImageDraw, ImageFont
rows = json.load(open(sys.argv[1])); out = sys.argv[2]; title = sys.argv[3]
def font(size):
    for path in ('/System/Library/Fonts/Helvetica.ttc', '/System/Library/Fonts/SFNS.ttf'):
        try: return ImageFont.truetype(path, size)
        except OSError: pass
    return ImageFont.load_default()
F, B = font(26), font(30)
colors = {'lights': (217, 131, 36), 'indirect light': (52, 120, 190), 'output': (99, 160, 86), 'geometry and shadow maps': (140, 140, 140),
          'measurement': (190, 80, 80)}
row_h, left, right, top = 44, 470, 150, 90
most = max(r['value'] + r.get('idle', 0) for r in rows)
W, H = 1700, top + row_h * len(rows) + 90
img = Image.new('RGB', (W, H), (255, 255, 255)); d = ImageDraw.Draw(img)
d.text((30, 24), title, fill=(20, 20, 20), font=B)
scale = (W - left - right) / most
for i, r in enumerate(rows):
    y = top + i * row_h
    c = colors.get(r.get('group'), (90, 90, 90))
    pale = tuple(int(v + (255 - v) * 0.6) for v in c)
    d.text((30, y + 6), r['label'], fill=(20, 20, 20), font=F)
    w = r['value'] * scale; wi = r.get('idle', 0) * scale
    d.rectangle([left, y + 6, left + w, y + row_h - 8], fill=c)
    d.rectangle([left + w, y + 6, left + w + wi, y + row_h - 8], fill=pale)
    d.text((left + w + wi + 10, y + 6), f"{r['value'] + r.get('idle', 0):.2f} ms", fill=(20, 20, 20), font=F)
x = 30
for name, c in colors.items():
    if any(r.get('group') == name for r in rows):
        d.rectangle([x, H - 60, x + 28, H - 32], fill=c); d.text((x + 38, H - 62), name, fill=(20, 20, 20), font=F)
        x += 70 + int(d.textlength(name, font=F))
img.save(out); print(out, img.size)
