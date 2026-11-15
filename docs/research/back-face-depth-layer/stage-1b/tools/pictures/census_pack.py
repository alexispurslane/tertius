#!/usr/bin/env python3
"""Throwaway (t1-gxzg): packs the census runs of job-census.sh for the trip back.

  census_pack.py <out> sill   For each run, out/<run>/sillpixels.txt: one line per hit pixel and class, with the hit
                              pixel's front distance, back share, slab in m and depth edge (below). And out/geometry.npz:
                              the front distance and back share of the run sill-rect-lamps-ten around the hit pixels.
  census_pack.py <out> sheet  For each run, out/pack/<run>-sheet-hits.png: the hits on a sheet per hit pixel, both kinds,
                              as 16-bit counts. For each view, out/pack/<view>-sheets.png (the sheet pixels white) and
                              out/<view>-slope.txt: the sheet pixels and each mode's sheet hits by the hit pixel's slope.

The slope of a pixel is the largest change of the front distance to a neighbour, over the front distance: about the
pixel's angle over the tangent of the view's angle to the surface. A depth edge holds a large slope too.
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

out, part = Path(sys.argv[1]), sys.argv[2]


def raw(folder, name):
    [path] = folder.glob(f"{name}-*.f32")
    w, h = map(int, path.stem.rsplit("-", 1)[1].split("x"))
    return np.fromfile(path, dtype=np.float32).reshape(h, w)


def slope(front):
    f = np.where(front > 0, front, np.nan)
    pad = np.pad(f, 1, constant_values=np.nan)
    h, w = f.shape
    best = np.zeros_like(f)
    for dy, dx in ((0, 1), (2, 1), (1, 0), (1, 2)):
        d = np.abs(pad[dy:dy + h, dx:dx + w] - f)
        best = np.fmax(best, np.nan_to_num(d, nan=0.0))
    return np.where(front > 0, best / np.where(front > 0, front, 1), 0)


def edge(front):
    """The largest front distance among a pixel and its eight neighbours, over its own: above 1.02 the pixel lies within
    one pixel of a surface more than 2 % farther, or of the sky (counted as infinite)."""
    f = np.where(front > 0, front, np.inf)
    pad = np.pad(f, 1, constant_values=np.inf)
    h, w = f.shape
    most = f.copy()
    for dy in range(3):
        for dx in range(3):
            most = np.maximum(most, pad[dy:dy + h, dx:dx + w])
    return most / np.where(front > 0, front, 1)


(out / "pack").mkdir(exist_ok=True)
if part == "sill":
    boxes = []
    for run in sorted(p for p in out.iterdir() if p.is_dir() and p.name.startswith("sill-")):
        front, back = raw(run, "front"), raw(run, "back")
        e = edge(front)
        lines = ["box x y crossing sheet samples light front back slab edge"]
        for line in (run / "census.txt").read_text().splitlines():
            if line.startswith("sillpixel-"):
                key, x, y, crossing, sheet, n, light = line.split()
                x, y = int(x), int(y)
                share = back[y, x]
                slab = front[y, x] * (1 / share - 1) if share > 0 else 0.0
                lines.append(f"{key[10:]} {x} {y} {crossing} {sheet} {n} {light} {front[y, x]:.4f} {share:.6f} {slab:.4f} {e[y, x]:.4f}")
                boxes.append((x, y))
        (run / "sillpixels.txt").write_text("\n".join(lines) + "\n")
    xs, ys = zip(*boxes)
    x0, x1, y0, y1 = max(min(xs) - 16, 0), max(xs) + 17, max(min(ys) - 16, 0), max(ys) + 17
    ten = out / "sill-rect-lamps-ten"
    np.savez_compressed(out / "geometry.npz", origin=np.array([x0, y0]), front=raw(ten, "front")[y0:y1, x0:x1],
                        back=raw(ten, "back")[y0:y1, x0:x1])
else:
    runs = sorted(p for p in out.iterdir() if p.is_dir() and p.name.startswith("sheet-"))
    views = sorted({r.name.split("-", 1)[1].rsplit("-ten", 1)[0] for r in runs if r.name.endswith("-ten")})
    bins = np.array([0, 1e-3, 3e-3, 1e-2, 3e-2, 1e-1, 3e-1, 1, np.inf])
    for view in views:
        ten = out / f"sheet-{view}-ten"
        front, back = raw(ten, "front"), raw(ten, "back")
        sheets = (front > 0) & ~(back > 0)
        Image.fromarray((sheets * 255).astype(np.uint8)).save(out / "pack" / f"{view}-sheets.png", optimize=True)
        s = slope(front)
        rows = [f"slope bins (upper bounds): {' '.join(f'{b:g}' for b in bins[1:])}",
                f"sheet pixels: {' '.join(str(int(((s >= lo) & (s < hi) & sheets).sum())) for lo, hi in zip(bins, bins[1:]))}"]
        for run in (r for r in runs if r.name.startswith(f"sheet-{view}-")):
            mode = run.name[len(f"sheet-{view}-"):]
            hits = sum(raw(run, f"sheet-hits-at-{k}") for k in ("reflection", "diffuse"))
            Image.fromarray(np.minimum(hits, 65535).astype(np.uint16)).save(out / "pack" / f"{run.name}-sheet-hits.png")
            rows.append(f"{mode} sheet hits: {' '.join(str(int(hits[(s >= lo) & (s < hi)].sum())) for lo, hi in zip(bins, bins[1:]))}")
        (out / f"{view}-slope.txt").write_text("\n".join(rows) + "\n")
print("pack", sum(p.stat().st_size for p in (out / "pack").iterdir()))
