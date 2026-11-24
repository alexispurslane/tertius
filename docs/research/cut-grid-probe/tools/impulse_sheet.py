#!/usr/bin/env python3
# Tool code of t1-3er4: reads the GPU impulse dumps of CutGridProbeTests (256 x 128 RGBA float32, 50 impulses of light 81
# on 0, one per cell of 24 x 24) and measures, per mode and stage, each impulse's footprint in its cell: the energy (sum of
# the cell's light over 81), the receivers (pixels above 0), the RMS radius, and the 4th-order anisotropy of the received
# light. Draws a sheet of four impulses per mode and stage at 8 x, and the evolution of one impulse over draws 0 to 5.
# Usage: python3 -I impulse_sheet.py <dump dir> <out dir>
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

W, H, CELL, BRIGHT = 256, 128, 24, 81.0
D, OUT = Path(sys.argv[1]), Path(sys.argv[2]); OUT.mkdir(parents=True, exist_ok=True)
def load(name): return np.fromfile(D / name, dtype=np.float32).reshape(H, W, 4)
def impulses():
    for j in range(H // CELL):
        for i in range(W // CELL):
            yield i, j, i * CELL + 8 + (i * 3 + j * 5) % 8, j * CELL + 8 + (i * 5 + j * 3) % 8
def stats(img, total):
    rows = []
    for i, j, x, y in impulses():
        cell = img[j * CELL:(j + 1) * CELL, i * CELL:(i + 1) * CELL, 0].astype(np.float64)
        ys, xs = np.mgrid[0:CELL, 0:CELL]; dx = xs - (x - i * CELL); dy = ys - (y - j * CELL)
        e = cell.sum(); r2 = dx * dx + dy * dy; ang = np.arctan2(dy, dx); off = r2 > 0
        rows.append({"energy": e / total, "receivers": int((cell > 1e-6).sum()),
                     "rms_radius": float(np.sqrt((cell * r2).sum() / e)) if e > 0 else 0.0,
                     "c4": float(abs((cell * r2 * np.exp(4j * ang))[off].sum()) / (cell * r2)[off].sum()) if (cell * r2)[off].sum() > 0 else 0.0})
    a = {k: np.array([r[k] for r in rows]) for k in rows[0]}
    return {"energy_mean": round(float(a["energy"].mean()), 4), "energy_std": round(float(a["energy"].std()), 4),
            "energy_min": round(float(a["energy"].min()), 4), "energy_max": round(float(a["energy"].max()), 4),
            "receivers_mean": round(float(a["receivers"].mean()), 2), "receivers_min": int(a["receivers"].min()),
            "receivers_max": int(a["receivers"].max()), "rms_radius_mean": round(float(a["rms_radius"].mean()), 3),
            "c4_of_mean_footprint": None, "c4_mean_single": round(float(a["c4"].mean()), 3)}
out = {}
modes = ["baseline", "a", "b", "off"]
for m in modes:
    for n in range(3):
        img = load(f"{m}-stage{n}-draw{n}.f32")
        # In draw n the impulse pixel's own blend holds 81 / (n + 1) of the light (n draws of 0 before it).
        out[f"{m}-stage{n}"] = stats(img, BRIGHT / (n + 1))
        out[f"{m}-stage{n}"]["image_sum_over_total"] = round(float(img[..., 0].sum() / (50 * BRIGHT / (n + 1))), 4)
    out[f"{m}-evolution"] = {f"draw{d}": round(float(load(f"{m}-evolution-draw{d}.f32")[..., 0].sum() / (50 * BRIGHT)), 4) for d in range(6)}
(OUT / "impulse-gpu.json").write_text(json.dumps(out, indent=1))
for k, v in out.items(): print(k, v)

# Sheet: 4 impulses (cells (0,0), (3,1), (6,2), (9,4)) per mode at stage 0, 1, 2, at 8 x nearest; gray = light / max.
Z, picks = 8, [(0, 0), (3, 1), (6, 2), (9, 4)]
tile = CELL * Z; pad = 10; head = 16
sheet = Image.new("RGB", (90 + 3 * (len(picks) * (tile + 4) + pad), len(modes) * (tile + head + pad) + 20), "white")
dr = ImageDraw.Draw(sheet)
for r, m in enumerate(modes):
    top = 20 + r * (tile + head + pad); dr.text((4, top + tile // 2), m, fill="black")
    for n in range(3):
        img = load(f"{m}-stage{n}-draw{n}.f32")[..., 0]
        for c, (i, j) in enumerate(picks):
            cell = img[j * CELL:(j + 1) * CELL, i * CELL:(i + 1) * CELL]
            g = np.clip(cell / (BRIGHT / (n + 1) / 9) * 255, 0, 255).astype(np.uint8)  # 1/9 of the pixel's light = white
            x0 = 90 + n * (len(picks) * (tile + 4) + pad) + c * (tile + 4)
            sheet.paste(Image.fromarray(g).resize((tile, tile), Image.NEAREST), (x0, top + head))
            # block borders of 8 x 8 in red ticks
            for b in range(0, CELL + 1, 8): dr.line([(x0 + b * Z, top + head - 4), (x0 + b * Z, top + head - 1)], fill="red")
        dr.text((90 + n * (len(picks) * (tile + 4) + pad), 4), f"stage {n}: spacing {4 >> n} (draw {n} after a reset)", fill="black")
sheet.save(OUT / "impulse-sheet-8x.png")

# Evolution: the impulse of cell (3, 1) in draw 0, read after draws 0..5, per mode, 8 x, scaled to its own maximum.
ev = Image.new("RGB", (90 + 6 * (tile + 4), len(modes) * (tile + head + pad) + 20), "white"); dr = ImageDraw.Draw(ev)
for r, m in enumerate(modes):
    top = 20 + r * (tile + head + pad); dr.text((4, top + tile // 2), m, fill="black")
    for d in range(6):
        cell = load(f"{m}-evolution-draw{d}.f32")[CELL:2 * CELL, 3 * CELL:4 * CELL, 0]
        g = np.clip(cell / max(cell.max(), 1e-9) * 255, 0, 255).astype(np.uint8)
        ev.paste(Image.fromarray(g).resize((tile, tile), Image.NEAREST), (90 + d * (tile + 4), top + head))
        dr.text((90 + d * (tile + 4), top + 2), f"draw {d} max {cell.max():.2f}", fill="black")
ev.save(OUT / "impulse-evolution-8x.png")
