#!/usr/bin/env python3
"""Draws the change of one still against its control as a map, for several variants, stacked in one picture.

Usage: capmap.py <out.png> <full scale, as a share> <control.png> <label=variant.png>...
A pixel is blue where the variant is darker than the control and red where it is brighter. The color is full at the
given relative change of the linear luminance. The control itself is the first panel, at half size.
"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

def linear(path):
    a = np.asarray(Image.open(path).convert('RGB'), dtype=np.float64) / 255.0
    a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
    return a @ np.array([0.2126, 0.7152, 0.0722])

out, full, control = sys.argv[1], float(sys.argv[2]), sys.argv[3]
ref = linear(control)
half = lambda im: im.resize((im.width // 2, im.height // 2), Image.LANCZOS)
panels = [('the control: both caps at their defaults', half(Image.open(control).convert('RGB')))]
for item in sys.argv[4:]:
    label, path = item.split('=', 1)
    var = linear(path)
    rel = (var - ref) / np.maximum(ref, 0.01)
    t = np.clip(rel / full, -1, 1)
    rgb = np.zeros(ref.shape + (3,))
    rgb[..., 0] = np.clip(t, 0, 1)
    rgb[..., 2] = np.clip(-t, 0, 1)
    rgb[..., 1] = 0.25 * np.abs(t)
    panels.append((label, half(Image.fromarray((rgb ** 0.6 * 255).astype(np.uint8)))))
w, h = panels[0][1].size
cols = 2
rows = (len(panels) + 1) // 2
sheet = Image.new('RGB', (w * cols, h * rows), (0, 0, 0))
draw = ImageDraw.Draw(sheet)
try:
    font = ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc', 22)
except Exception:
    font = ImageFont.load_default()
for i, (label, im) in enumerate(panels):
    x, y = (i % cols) * w, (i // cols) * h
    sheet.paste(im, (x, y))
    box = draw.textbbox((x + 10, y + 8), label, font=font)
    draw.rectangle((box[0] - 6, box[1] - 4, box[2] + 6, box[3] + 4), fill=(0, 0, 0))
    draw.text((x + 10, y + 8), label, fill=(255, 255, 255), font=font)
sheet.save(out)
print(out, sheet.size)
