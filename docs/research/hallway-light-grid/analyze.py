#!/usr/bin/env python3
"""Research tool (issue #21): measures the four hallway films frame by frame.

    nix develop --command python3 -I docs/research/hallway-light-grid/analyze.py <films folder> <out folder> [variants]

<films folder> holds one folder per variant, by default legacy/, merged/, outer/ and four/, each with the walk.mkv of render.sh (1280 x 720, 1,190
frames). [variants] is a comma-separated list such as legacy,merged,tiles. The tool reads the films in step through ffmpeg and writes:

- surfaces.csv: per frame, the camera's distance to the nose wall and the mean 8-bit sRGB color of two fixed surfaces
  of the nose end in each film: a wall patch beside the nose window and a floor patch in front of the wall.
- summary.json: the mean absolute difference of each film from legacy and from merged, over the walk and in bands of
  the distance to the nose wall; the temporal noise of the two still holds; and, during the walk (frames 32 to 1129),
  the count of frame-to-frame changes of each surface's luma over 0.6 in two distance bands, with the largest steps.
- nose-wall.svg and floor.svg: the luma of each surface against the distance to the nose wall.

The camera stands in the ship's frame at x = 0 and y = -1.85 m (eye height 1.65 m over the floor at -3.5 m) and looks
along -z with a vertical field of view of 60 degrees. Its z is 29.18 m in the first hold (frames 0 to 30), falls by
0.05 m per frame from frame 31 on, and stops at -25.72 m (the aft-wall and nose-wall dumps).
"""

import csv
import json
import math
import subprocess
import sys
from pathlib import Path

import numpy as np

WIDTH, HEIGHT = 1280, 720
FOCAL = (HEIGHT / 2) / math.tan(math.radians(30))  # px: vertical field of view of 60 degrees
EYE_Y = -1.85
NOSE_Z = -29.5  # the inner face of the nose wall
VARIANTS = ["legacy", "merged", "outer", "four"]
NAMES = {
    "legacy": "Legacy ray tracing",
    "merged": "Merged grid (16 m)",
    "outer": "Prototype 0.25/0.5/2 m (32 m)",
    "four": "Reference 0.25/0.5/1/2 m (32 m)",
    "tiles": "Diagnostic: 4 levels, 4 m tiles at 2 m (32 m)",
}
COLORS = {"legacy": "#444444", "merged": "#1f77b4", "outer": "#d62728", "four": "#2ca02c", "tiles": "#9467bd"}
START_HOLD = range(10, 31)  # still frames before the walk, after 10 frames of settling
END_HOLD = range(1150, 1190)  # still frames after the walk, after 20 frames of stopping


def camera_z(frame: int) -> float:
    if frame < 31:
        return 29.18
    return max(-25.72, 29.18 - 0.05 * (frame - 31))


def project(x: float, y: float, z: float, cz: float) -> tuple[float, float]:
    d = cz - z
    return WIDTH / 2 + FOCAL * x / d, HEIGHT / 2 - FOCAL * (y - EYE_Y) / d


def box(corners: list[tuple[float, float, float]], cz: float):
    """The pixel box that holds the projected corners, shrunk by 15% on each side, or None below 3 px."""
    us, vs = zip(*(project(x, y, z, cz) for x, y, z in corners))
    u0, u1, v0, v1 = min(us), max(us), min(vs), max(vs)
    du, dv = 0.15 * (u1 - u0), 0.15 * (v1 - v0)
    u0, u1, v0, v1 = int(u0 + du), int(u1 - du), int(v0 + dv), int(v1 - dv)
    u0, v0, u1, v1 = max(u0, 0), max(v0, 0), min(u1, WIDTH), min(v1, HEIGHT)
    if u1 - u0 < 3 or v1 - v0 < 3:
        return None
    return v0, v1, u0, u1


# A wall patch right of the nose window (the window spans x from -3 to 3 m and y from -2.85 to -0.85 m).
WALL = [(4.0, -2.6, NOSE_Z), (7.0, -2.6, NOSE_Z), (4.0, -0.4, NOSE_Z), (7.0, -0.4, NOSE_Z)]
# A floor patch 1 to 3 m in front of the nose wall.
FLOOR = [(-1.5, -3.5, NOSE_Z + 1.0), (1.5, -3.5, NOSE_Z + 1.0), (-1.5, -3.5, NOSE_Z + 3.0), (1.5, -3.5, NOSE_Z + 3.0)]


def frames(path: Path):
    process = subprocess.Popen(
        ["ffmpeg", "-loglevel", "error", "-i", str(path), "-f", "rawvideo", "-pix_fmt", "rgb24", "-"],
        stdout=subprocess.PIPE,
    )
    size = WIDTH * HEIGHT * 3
    while True:
        data = process.stdout.read(size)
        if len(data) < size:
            break
        yield np.frombuffer(data, np.uint8).reshape(HEIGHT, WIDTH, 3)
    process.wait()


def luma(rgb) -> float:
    return float(0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2])


WALK_FRAMES = range(32, 1130)
STEP = 0.6  # luma: a frame-to-frame change larger than this counts as a step


def steps(rows: list[dict], key: str, variant: str) -> dict:
    """The walk's frame-to-frame luma changes over STEP for one surface: counts by band and the five largest."""
    changes = []
    for before, after in zip(rows, rows[1:]):
        if after["frame"] not in WALK_FRAMES or "" in (before[f"{key}_{variant}_luma"], after[f"{key}_{variant}_luma"]):
            continue
        change = after[f"{key}_{variant}_luma"] - before[f"{key}_{variant}_luma"]
        if abs(change) > STEP:
            changes.append((after["distance"], after["frame"], before[f"{key}_{variant}_luma"], change))
    largest = sorted(changes, key=lambda c: -abs(c[3]))[:5]
    return {
        "16-60 m": sum(1 for c in changes if c[0] >= 16),
        "7.1-16 m": sum(1 for c in changes if 7.1 <= c[0] < 16),
        "below 7.1 m": sum(1 for c in changes if c[0] < 7.1),
        "largest": [
            {"distance": d, "frame": f, "before": round(b, 2), "after": round(b + c, 2), "change_percent": round(c / b * 100, 1)}
            for d, f, b, c in largest
        ],
    }


def chart(rows: list[dict], key: str, title: str, path: Path, variants: list[str]) -> None:
    """A line chart of one surface's luma against the distance to the nose wall, as a self-contained SVG."""
    left, top, w, h = 60, 40, 640, 300
    points = {v: [(r["distance"], r[f"{key}_{v}_luma"]) for r in rows if r[f"{key}_{v}_luma"] != ""] for v in variants}
    lumas = [y for p in points.values() for _, y in p]
    if not lumas:
        return
    y0, y1 = 0.0, max(lumas) * 1.1
    x0, x1 = 60.0, 0.0  # the camera approaches the wall: distance falls left to right

    def sx(x):
        return left + (x0 - x) / (x0 - x1) * w

    def sy(y):
        return top + h - (y - y0) / (y1 - y0) * h

    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{left + w + 330}" height="{top + h + 50}" '
        'font-family="Helvetica, Arial, sans-serif" font-size="12">',
        f'<rect width="100%" height="100%" fill="white"/><text x="{left}" y="22" font-size="14">{title}</text>',
    ]
    for edge, label, row in ((8.0, "16 m cube edge (8 m)", 12), (16.0, "32 m cube edge (16 m)", 26)):
        parts.append(
            f'<line x1="{sx(edge):.1f}" y1="{top}" x2="{sx(edge):.1f}" y2="{top + h}" stroke="#999" stroke-dasharray="4 3"/>'
            f'<text x="{sx(edge) - 3:.1f}" y="{top + row}" fill="#666" text-anchor="end">{label}</text>'
        )
    for tick in range(0, 61, 10):
        parts.append(f'<text x="{sx(tick):.1f}" y="{top + h + 16}" text-anchor="middle">{tick}</text>')
    for i in range(5):
        value = y0 + (y1 - y0) * i / 4
        parts.append(f'<text x="{left - 6}" y="{sy(value) + 4:.1f}" text-anchor="end">{value:.0f}</text>')
    parts.append(
        f'<text x="{left + w / 2}" y="{top + h + 36}" text-anchor="middle">distance from camera to nose wall (m)</text>'
        f'<rect x="{left}" y="{top}" width="{w}" height="{h}" fill="none" stroke="#333"/>'
    )
    for n, variant in enumerate(variants):
        path_points = " ".join(f"{sx(x):.1f},{sy(y):.1f}" for x, y in points[variant])
        parts.append(f'<polyline points="{path_points}" fill="none" stroke="{COLORS[variant]}" stroke-width="1.6"/>')
        parts.append(
            f'<line x1="{left + w + 14}" y1="{top + 10 + 18 * n}" x2="{left + w + 34}" y2="{top + 10 + 18 * n}" '
            f'stroke="{COLORS[variant]}" stroke-width="3"/>'
            f'<text x="{left + w + 40}" y="{top + 14 + 18 * n}">{NAMES[variant]}</text>'
        )
    parts.append("</svg>")
    path.write_text("\n".join(parts) + "\n")


def main() -> int:
    films, out = Path(sys.argv[1]), Path(sys.argv[2])
    variants = sys.argv[3].split(",") if len(sys.argv) > 3 else VARIANTS
    out.mkdir(parents=True, exist_ok=True)
    readers = [frames(films / v / "walk.mkv") for v in variants]
    rows = []
    bases = [b for b in ("legacy", "merged") if b in variants]
    diffs = {f"{v}_vs_{b}": [] for b in bases for v in variants if v != b}
    holds = {v: {"start": [], "end": []} for v in variants}
    for frame, images in enumerate(zip(*readers)):
        cz = camera_z(frame)
        row = {"frame": frame, "camera_z": round(cz, 3), "distance": round(cz - NOSE_Z, 3)}
        originals = dict(zip(variants, images))
        stack = {v: img.astype(np.float32) for v, img in originals.items()}
        for key, corners in (("wall", WALL), ("floor", FLOOR)):
            region = box(corners, cz)
            for v in variants:
                if region is None:
                    row[f"{key}_{v}_rgb"], row[f"{key}_{v}_luma"] = "", ""
                    continue
                v0, v1, u0, u1 = region
                rgb = stack[v][v0:v1, u0:u1].reshape(-1, 3).mean(axis=0)
                row[f"{key}_{v}_rgb"] = " ".join(f"{c:.1f}" for c in rgb)
                row[f"{key}_{v}_luma"] = round(luma(rgb), 2)
        for name in diffs:
            v, b = name.split("_vs_")
            diffs[name].append((row["distance"], float(np.abs(stack[v] - stack[b]).mean())))
        for v in variants:
            if frame in START_HOLD:
                holds[v]["start"].append(originals[v])
            if frame in END_HOLD:
                holds[v]["end"].append(originals[v])
        rows.append(row)

    with (out / "surfaces.csv").open("w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

    bands = [(40, 60), (17, 40), (9, 17), (0, 9)]
    summary = {"frames": len(rows), "mean_abs_diff_8bit": {}, "temporal_noise_8bit": {}}
    for name, values in diffs.items():
        entry = {"walk": round(float(np.mean([d for _, d in values])), 3)}
        for lo, hi in bands:
            band = [d for dist, d in values if lo <= dist < hi]
            entry[f"{lo}-{hi} m"] = round(float(np.mean(band)), 3) if band else None
        summary["mean_abs_diff_8bit"][name] = entry
    for v in variants:
        summary["temporal_noise_8bit"][v] = {
            hold: round(float(np.std(np.stack(images).astype(np.float32), axis=0).mean()), 3) for hold, images in holds[v].items()
        }
    summary["walk_steps"] = {f"{key}_{v}": steps(rows, key, v) for key in ("wall", "floor") for v in variants}
    (out / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    chart(rows, "wall", "Nose wall beside the window: luma (8-bit sRGB) as the camera approaches", out / "nose-wall.svg", variants)
    chart(rows, "floor", "Floor 1 to 3 m before the nose wall: luma (8-bit sRGB) as the camera approaches", out / "floor.svg", variants)
    print(json.dumps(summary, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
