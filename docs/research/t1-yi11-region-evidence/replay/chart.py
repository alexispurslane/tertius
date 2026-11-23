# Tool code of the line orchestrator (t1-yi11), never for src. chart.py <out.png>: the trade of the effect tests from
# model.txt, the frozen bay's added noise against rect-bay's distance from the smoothed logged light, at a model cap of 128.
import sys

from PIL import Image, ImageDraw, ImageFont

POINTS = [  # name, frozen bay's added noise, rect-bay's "off" in percent (model.txt)
    ('today (column b)', 0.8165, 15.78), ('repaired cap (column c)', 0.4798, 18.50),
    ('6 in a row', 0.1066, 18.31), ('12 in a row', 0.0121, 22.15), ('5 standard deviations', 0.0557, 28.23),
    ('signed rank, 7.6e-5 (design E)', 0.0238, 19.78), ('18 of one sign', 0.0007, 28.62),
    ('3 in a row', 0.3616, 16.85), ('4 standard deviations', 0.2116, 18.72)]
W, H, L, R, T, B = 1200, 700, 90, 40, 60, 80
X0, X1, Y0, Y1 = 0.0, 0.9, 12.0, 32.0


def font(size):
    for name in ('/System/Library/Fonts/Helvetica.ttc', '/System/Library/Fonts/Supplemental/Arial.ttf'):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


def at(x, y):
    return L + (x - X0) / (X1 - X0) * (W - L - R), H - B - (y - Y0) / (Y1 - Y0) * (H - T - B)


image = Image.new('RGB', (W, H), 'white')
draw = ImageDraw.Draw(image)
small, big = font(18), font(22)
draw.text((L, 14), 'Effect tests of the region drift, replayed at a model cap of 128: no test is at the lower left', fill='black', font=big)
for k in range(10):
    x = k / 10
    px, py = at(x, Y0)
    draw.line([(px, T), (px, H - B)], fill=(225, 225, 225))
    draw.text((px - 12, H - B + 8), f'{x:.1f}', fill='black', font=small)
for y in range(12, 33, 4):
    px, py = at(X0, y)
    draw.line([(L, py), (W - R, py)], fill=(225, 225, 225))
    draw.text((L - 52, py - 10), f'{y} %', fill='black', font=small)
draw.rectangle([L, T, W - R, H - B], outline='black')
draw.text((L, H - 44), "Frozen bay: added noise of the false cuts (0 = column d, no region evidence, whose rect-bay value is 366 %)", fill='black', font=small)
draw.text((L + 6, T + 6), "rect-bay's film: the model light's distance from the logged light", fill='black', font=small)
for name, x, y in POINTS:
    px, py = at(x, y)
    design = 'design E' in name
    draw.ellipse([px - 7, py - 7, px + 7, py + 7], fill=(200, 60, 40) if design else (40, 90, 170))
    left = x > 0.6
    box = draw.textbbox((0, 0), name, font=small)
    draw.text((px - 12 - box[2] if left else px + 12, py - 11), name, fill='black', font=small)
image.save(sys.argv[1])
