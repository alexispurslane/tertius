#!/usr/bin/env python3
"""Throwaway (t1-gxzg): a comparison image of crops at full size, one image pixel per pixel, side by side with a gap of
4 px, in the order of the columns given.

  python3 -I crops.py <job folder> <x0> <y0> <x1> <y1> <out.png> <column>[:<frame>] ...

Each column reads <job>/out/pack/<column>-<frame>.webp, frame 000239 by default. The box is in output pixels, x1 and y1
excluded.
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

job = Path(sys.argv[1])
x0, y0, x1, y1 = map(int, sys.argv[2:6])
out = Path(sys.argv[6])
crops = []
for arg in sys.argv[7:]:
    name, _, frame = arg.partition(":")
    crops.append(np.asarray(Image.open(job / "out" / "pack" / f"{name}-{frame or '000239'}.webp").convert("RGB"))[y0:y1, x0:x1])
gap = 4
h, w = crops[0].shape[:2]
canvas = np.full((h, len(crops) * w + (len(crops) - 1) * gap, 3), 255, dtype=np.uint8)
for i, c in enumerate(crops):
    canvas[:, i * (w + gap): i * (w + gap) + w] = c
Image.fromarray(canvas).save(out, optimize=True)
print(out, canvas.shape[1], "x", canvas.shape[0], out.stat().st_size, "bytes")
