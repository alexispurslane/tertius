# budget.py <out.png>: the deck's frame span through the changes in flight and the exact cuts of this sweep, against the budgets.
import sys
from PIL import Image, ImageDraw, ImageFont
def font(size):
    for path in ('/System/Library/Fonts/Helvetica.ttc', '/System/Library/Fonts/SFNS.ttf'):
        try: return ImageFont.truetype(path, size)
        except OSError: pass
    return ImageFont.load_default()
F, B = font(26), font(30)
rows = [  # label, low, high, color
 ('Today (measured)', 24.4, 24.4, (90, 90, 90)),
 ('With the reserve of 1.5 ms for the mottle fixes', 25.9, 25.9, (190, 80, 80)),
 ('After the lamp design in flight (estimated 1.0 to 1.7)', 24.2, 24.9, (217, 131, 36)),
 ('After the thinner filter in flight (measured 2.15)', 22.0, 22.7, (52, 120, 190)),
 ('After the exact-image cuts of this sweep (0.9 to 1.1)', 20.9, 21.8, (99, 160, 86)),
 ('Then render scale 0.59 in place of 0.667 (visible)', 17.1, 17.9, (150, 110, 180)),
 ('Or block-rate bounce light, reflected place, map cadence (visible)', 17.9, 19.2, (150, 110, 180)),
]
W, row_h, left, top = 1800, 56, 760, 100
H = top + row_h * len(rows) + 90
scale = (W - left - 140) / 28.0
img = Image.new('RGB', (W, H), (255, 255, 255)); d = ImageDraw.Draw(img)
d.text((30, 26), "The deck's GPU frame span in ms: where the work in flight and this sweep's cuts leave it", fill=(20, 20, 20), font=B)
for target, name in ((16.7, '16.7 ms'), (12.0, '12 ms')):
    x = left + target * scale
    d.line([x, top - 10, x, top + row_h * len(rows)], fill=(200, 40, 40), width=3)
    d.text((x - 44, top + row_h * len(rows) + 8), name, fill=(200, 40, 40), font=F)
for i, (label, lo, hi, c) in enumerate(rows):
    y = top + i * row_h
    d.text((30, y + 10), label, fill=(20, 20, 20), font=font(22))
    pale = tuple(int(v + (255 - v) * 0.55) for v in c)
    d.rectangle([left, y + 8, left + lo * scale, y + row_h - 10], fill=c)
    d.rectangle([left + lo * scale, y + 8, left + hi * scale, y + row_h - 10], fill=pale)
    d.text((left + hi * scale + 10, y + 10), f'{lo:.1f}' if lo == hi else f'{lo:.1f} to {hi:.1f}', fill=(20, 20, 20), font=F)
img.save(sys.argv[1]); print(sys.argv[1], img.size)
