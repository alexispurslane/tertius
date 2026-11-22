#!/usr/bin/env python3
"""Tool code (ter-ukw6): one crop of two stills, before on the left and after on the right, with an 8-pixel black gap.

  python3 -I pair.py <before.png> <after.png> <x0,y0,x1,y1 in output pixels> <scale> <out.png>"""
import sys

from PIL import Image

before, after, box, scale, out = sys.argv[1], sys.argv[2], tuple(int(v) for v in sys.argv[3].split(",")), int(sys.argv[4]), sys.argv[5]
w, h = (box[2] - box[0]) * scale, (box[3] - box[1]) * scale
sheet = Image.new("RGB", (2 * w + 8, h), "black")
for i, path in enumerate((before, after)):
    sheet.paste(Image.open(path).convert("RGB").crop(box).resize((w, h), Image.NEAREST), (i * (w + 8), 0))
sheet.save(out)
