"""Scratch measure of ticket t2-mlvy: how much the ratio of a film to the reference film changes from one frame to the
next. Usage: flicker.py <reference folder> <film folder> <first frame> <last frame>"""
import sys
import numpy as n
from PIL import Image, ImageFilter

def lum(path):
    im = Image.open(path).convert("L").filter(ImageFilter.BoxBlur(4))
    return n.asarray(im, dtype=n.float32) / 255.0

ref, film, a, b = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4])
prev, steps = None, []
for f in range(a, b + 1):
    r, c = lum(f"{ref}/film-{f:06d}.png"), lum(f"{film}/film-{f:06d}.png")
    lit = r > 0.05
    ratio = n.where(lit, c / n.maximum(r, 1e-4), 1.0)
    if prev is not None:
        both = lit & prev[1]
        steps.append(n.abs(ratio - prev[0])[both])
    prev = (ratio, lit)
s = n.concatenate(steps) * 100
print(f"change of the ratio between frames: mean {s.mean():.3f} %, p90 {n.percentile(s, 90):.3f} %, p99 {n.percentile(s, 99):.3f} %, p99.9 {n.percentile(s, 99.9):.3f} %")
