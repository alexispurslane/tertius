"""Scratch tool of ticket t2-mlvy: a sheet of stills side by side with labels, and a heat map of two stills' difference.
Usage: sheet.py sheet <out.png> <width> <label=path>...   |   sheet.py crop <out.png> <x0> <y0> <x1> <y1> <zoom> <label=path>...
       sheet.py heat <out.png> <a.png> <b.png> <full-scale-steps>"""
import sys
import numpy as n
from PIL import Image, ImageDraw, ImageFont

def font(size):
    for path in ("/System/Library/Fonts/Helvetica.ttc", "/System/Library/Fonts/SFNS.ttf"):
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            pass
    return ImageFont.load_default()

def labeled(image, label):
    draw = ImageDraw.Draw(image)
    f = font(max(14, image.width // 40))
    box = draw.textbbox((0, 0), label, font=f)
    draw.rectangle((0, 0, box[2] + 16, box[3] + 12), fill=(0, 0, 0))
    draw.text((8, 4), label, fill=(255, 255, 255), font=f)
    return image

def row(images):
    sheet = Image.new("RGB", (sum(i.width for i in images) + 4 * (len(images) - 1), max(i.height for i in images)), (40, 40, 40))
    x = 0
    for i in images:
        sheet.paste(i, (x, 0))
        x += i.width + 4
    return sheet

mode, out = sys.argv[1], sys.argv[2]
if mode == "sheet":
    width = int(sys.argv[3])
    images = []
    for item in sys.argv[4:]:
        label, path = item.split("=", 1)
        image = Image.open(path).convert("RGB")
        image = image.resize((width, round(image.height * width / image.width)), Image.LANCZOS)
        images.append(labeled(image, label))
    row(images).save(out)
elif mode == "crop":
    x0, y0, x1, y1, zoom = map(int, sys.argv[3:8])
    images = []
    for item in sys.argv[8:]:
        label, path = item.split("=", 1)
        image = Image.open(path).convert("RGB").crop((x0, y0, x1, y1))
        image = image.resize((image.width * zoom, image.height * zoom), Image.NEAREST)
        images.append(labeled(image, label))
    row(images).save(out)
elif mode == "heat":
    a = n.asarray(Image.open(sys.argv[3]).convert("RGB"), dtype=n.float32)
    b = n.asarray(Image.open(sys.argv[4]).convert("RGB"), dtype=n.float32)
    full = float(sys.argv[5])
    d = (b - a).mean(axis=2) / full
    rgb = n.zeros(a.shape, dtype=n.float32)
    rgb[..., 0] = n.clip(d, 0, 1)          # brighter in b: red
    rgb[..., 2] = n.clip(-d, 0, 1)         # darker in b: blue
    rgb[..., 1] = 0.15 * a.mean(axis=2) / 255
    Image.fromarray((n.clip(rgb, 0, 1) * 255).astype(n.uint8)).save(out)
