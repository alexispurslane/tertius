#!/usr/bin/env python3
"""Throwaway (t1-yy2f, part 1): what the back-face pass stores on each pixel, from TERTIUS_BACK_FACE_DUMP folders.

    python3 -I share_map.py <out.png> <dump folder> [<dump folder> ...]

One panel per folder, side by side with a gap of 4 px, at the render size. Gray: the front distance, darker farther,
on pixels with a back face within 1 m. Blue: a share of 1, the pixel's own fragment (thickness 0). Yellow: a share of 0,
no back face. Red: a hair pixel (the G-buffer's shading model) with a back face behind it, borrowed from what lies
behind the card. Prints the counts of each class per panel."""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

panels = []
for folder in map(Path, sys.argv[2:]):
    front_file = next(folder.glob("front-0-*.f32"))
    size = front_file.stem.split("-")[-1]
    w, h = map(int, size.split("x"))
    front = np.fromfile(front_file, np.float32).reshape(h, w)
    share = np.fromfile(folder / f"back-0-{size}.f32", np.float32).reshape(h, w)
    surface = front > 0
    slab = np.where((share > 0) & (share < 1), front / np.maximum(share, 1e-30) - front, 0)
    own = surface & (share >= 1)
    none = surface & ~(share > 0)
    shading = np.fromfile(folder / f"shading-0-{size}.u8", np.uint8).reshape(h, w, 4)
    hair = (shading[..., 3] & 3) == 2
    far = surface & hair & (share > 0) & (share < 1)
    img = np.zeros((h, w, 3), np.uint8)
    gray = np.clip(140 - front * 12, 25, 140).astype(np.uint8)
    img[surface] = np.stack([gray, gray, gray], -1)[surface]
    img[own] = (70, 90, 220)
    img[none] = (250, 220, 0)
    img[far] = (230, 30, 30)
    panels.append(img)
    print(f"{folder.name}: surface {surface.sum()}, own fragment {own.sum()}, no back face {none.sum()}, hair pixels {(surface & hair).sum()}, hair with a borrowed back face {far.sum()}, its median slab {np.median(slab[far]) if far.any() else 0:.2f} m")
gap = 4
h, w = panels[0].shape[:2]
canvas = np.full((h, len(panels) * w + (len(panels) - 1) * gap, 3), 255, np.uint8)
for i, p in enumerate(panels):
    canvas[:, i * (w + gap): i * (w + gap) + w] = p
Image.fromarray(canvas).save(sys.argv[1], optimize=True)
print(sys.argv[1], Path(sys.argv[1]).stat().st_size, "bytes")
