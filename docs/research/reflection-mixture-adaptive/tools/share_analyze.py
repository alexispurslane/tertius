#!/usr/bin/env python3
"""Analyze and visualize Design 1 per-tile guided share dumps (.shares.meta + .shares.f32)."""
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw


def load_shares(prefix: Path):
    w, h, first = map(int, Path(f"{prefix}.shares.meta").read_text().strip().split())
    raw = np.fromfile(f"{prefix}.shares.f32", dtype=np.float32)
    n = raw.size // (w * h)
    return raw[: n * w * h].reshape(n, h, w), first


def colormap(vals: np.ndarray, ceiling: float) -> np.ndarray:
    """Map share in [0, ceiling] to RGB uint8: 0=deep navy, 0.5*ceil=teal/yellow, ceil=bright orange-white."""
    t = np.clip(vals / max(ceiling, 1e-6), 0.0, 1.0)
    # Perceptually clear inferno-like ramp: 0->black/navy, 0.25->purple, 0.5->red-orange, 0.8->yellow, 1.0->white
    r = np.clip(np.interp(t, [0.0, 0.25, 0.55, 0.85, 1.0], [10, 110, 225, 252, 255]), 0, 255)
    g = np.clip(np.interp(t, [0.0, 0.25, 0.55, 0.85, 1.0], [12, 28, 88, 215, 252]), 0, 255)
    b = np.clip(np.interp(t, [0.0, 0.25, 0.55, 0.85, 1.0], [38, 120, 48, 45, 220]), 0, 255)
    return np.stack([r, g, b], axis=-1).astype(np.uint8)


def region_stats(shares: np.ndarray, box_out, ceiling: float, settle_from: int = 100):
    """Compute tile stats for an output-pixel box (1920x1080 -> 1280x720 -> 80x45 tiles, 24 output px per tile)."""
    x0, y0, x1, y1 = box_out
    tx0, ty0 = int(round(x0 / 24.0)), int(round(y0 / 24.0))
    tx1, ty1 = int(round(x1 / 24.0)), int(round(y1 / 24.0))
    sub = shares[:, ty0:ty1, tx0:tx1].astype(np.float64)
    late = sub[settle_from:]
    late_tile_mean = late.mean(axis=0)
    late_tile_std = late.std(axis=0)
    series_mean = sub.mean(axis=(1, 2))
    target = float(series_mean[settle_from:].mean())
    # Draws to settle: first draw d after which |series_mean[k] - target| <= max(0.015, 0.05 * ceiling) for all k >= d
    tol = max(0.015, 0.05 * ceiling)
    settle_draw = len(series_mean)
    for d in range(len(series_mean)):
        if np.all(np.abs(series_mean[d:] - target) <= tol):
            settle_draw = d
            break
    # Also draw where mean first enters within tol of target
    first_enter = next((d for d in range(len(series_mean)) if abs(series_mean[d] - target) <= tol), len(series_mean))
    return {
        "tile_box": [tx0, ty0, tx1, ty1],
        "tiles": int(sub.shape[1] * sub.shape[2]),
        "draw0_mean": float(series_mean[0]),
        "draw5_mean": float(series_mean[min(5, len(series_mean) - 1)]),
        "draw15_mean": float(series_mean[min(15, len(series_mean) - 1)]),
        "draw30_mean": float(series_mean[min(30, len(series_mean) - 1)]),
        "draw60_mean": float(series_mean[min(60, len(series_mean) - 1)]),
        "settled_mean": target,
        "settled_tile_mean_min": float(late_tile_mean.min()),
        "settled_tile_mean_max": float(late_tile_mean.max()),
        "settled_tile_std_mean": float(late_tile_std.mean()),
        "settled_tile_std_median": float(np.median(late_tile_std)),
        "settled_tile_std_p95": float(np.percentile(late_tile_std, 95)),
        "settled_tile_std_max": float(late_tile_std.max()),
        "settled_frac_zero": float((late_tile_mean < 1e-4).mean()),
        "settled_frac_ceiling": float((late_tile_mean > ceiling - 1e-3).mean()),
        "draws_to_enter_tol": int(first_enter),
        "draws_to_settle_stay": int(settle_draw),
    }


def save_strip(shares: np.ndarray, ceiling: float, draws_list, out_png: Path, scale: int = 4):
    h, w = shares.shape[1], shares.shape[2]
    tw, th = w * scale, h * scale
    pad = 8
    header = 24
    strip = Image.new("RGB", (len(draws_list) * tw + (len(draws_list) - 1) * pad, th + header), "white")
    dr = ImageDraw.Draw(strip)
    for i, d in enumerate(draws_list):
        idx = min(d, shares.shape[0] - 1)
        rgb = colormap(shares[idx], ceiling)
        im = Image.fromarray(rgb).resize((tw, th), Image.NEAREST)
        x = i * (tw + pad)
        strip.paste(im, (x, header))
        dr.text((x + 4, 5), f"draw {idx} (mean={shares[idx].mean():.3f})", fill="black")
    strip.save(out_png)


def main():
    mode = sys.argv[1]
    prefix = Path(sys.argv[2])
    ceiling = float(sys.argv[3])
    shares, first = load_shares(prefix)
    if mode == "stats":
        boxes = json.loads(Path(sys.argv[4]).read_text())
        out = {
            "shape": list(shares.shape),
            "first_draw": first,
            "ceiling": ceiling,
            "whole_frame": region_stats(shares, [0, 0, 1920, 1080], ceiling),
            "regions": {k: region_stats(shares, v, ceiling) for k, v in boxes.items()},
        }
        print(json.dumps(out, indent=2))
    elif mode == "strip":
        draws_list = [int(x) for x in sys.argv[4].split(",")]
        save_strip(shares, ceiling, draws_list, Path(sys.argv[5]), scale=int(sys.argv[6]) if len(sys.argv) > 6 else 4)


if __name__ == "__main__":
    main()
