#!/usr/bin/env python3
"""Throwaway (t1-yy2f, part 3): where the real hits on sheets that no thickness loses land, by reason.

    python3 -I lost_map.py <out.png> <x0> <y0> <x1> <y1> <census folder> [...]

Gray is the front distance of the census's last draw, darker farther. Each lost real hit (class 1, reflection and
diffuse rays) colors its hit pixel by its reason (sheet_rules.py): blue 1, green 2, orange 3, magenta 4. A pixel takes the
reason of most of its lost hits. The box crops each panel, in render pixels, and the panels stand side by side, scaled 2x.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from sheet_rules import kept, load, reasons  # noqa: E402

COLORS = {1: (60, 110, 255), 2: (40, 210, 70), 3: (255, 150, 20), 4: (230, 40, 220)}
out = sys.argv[1]
x0, y0, x1, y1 = map(int, sys.argv[2:6])
panels = []
for folder in map(Path, sys.argv[6:]):
    front_file = next(folder.glob("front-*.f32"))
    w, h = map(int, front_file.stem.split("-")[-1].split("x"))
    front = np.fromfile(front_file, np.float32).reshape(h, w)
    img = np.zeros((h, w, 3), np.uint8)
    gray = np.clip(120 - front * 10, 20, 120).astype(np.uint8)
    img[front > 0] = np.stack([gray] * 3, -1)[front > 0]
    r = load(folder)
    reason, _ = reasons(r)
    lost = (r["crossing"] == 1) & ~kept(r, 1)
    votes = np.zeros((h, w, 5), np.int32)
    np.add.at(votes, (r["hy"][lost].astype(int), r["hx"][lost].astype(int), reason[lost].astype(int) - 1), 1)
    any_lost = votes.sum(-1) > 0
    top = votes.argmax(-1) + 1
    for k, c in COLORS.items():
        img[any_lost & (top == k)] = c
    print(f"{folder.name}: lost real hits {lost.sum()} on {any_lost.sum()} pixels; " + ", ".join(f"reason {k} {(any_lost & (top == k)).sum()} pixels" for k in COLORS))
    panels.append(np.asarray(Image.fromarray(img[y0:y1, x0:x1]).resize(((x1 - x0) * 2, (y1 - y0) * 2), Image.NEAREST)))
gap = 4
ph, pw = panels[0].shape[:2]
canvas = np.full((ph, len(panels) * pw + (len(panels) - 1) * gap, 3), 255, np.uint8)
for i, p in enumerate(panels):
    canvas[:, i * (pw + gap): i * (pw + gap) + pw] = p
Image.fromarray(canvas).save(out, optimize=True)
print(out, canvas.shape[1], "x", canvas.shape[0], Path(out).stat().st_size, "bytes")
