#!/usr/bin/env python3
"""Research tool (issue #25): compares hallway films of main and the guarded outer grid frame by frame.

    nix develop --command python3 -I docs/research/outer-grid-guard/analyze.py <films folder> <out folder> <variant>...

<films folder> holds <variant>/walk.mkv for each variant, rendered by render.sh (1280 x 720, 1,190 frames of
scripts/hallway-walk.json). The tool reuses the camera path, the projection and the two tracked surfaces of
docs/research/hallway-light-grid/analyze.py: a nose-wall patch beside the window and a floor patch 1 to 3 m before the
nose wall. It writes:

- surfaces.csv: per frame, the camera's distance to the nose wall, the pixel box of each surface and its mean luma
  (0.2126 R + 0.7152 G + 0.0722 B of the 8-bit sRGB values) in each film.
- nose-wall.svg and floor.svg: each surface's luma in every film against the distance to the nose wall.
- summary.json: for each surface and film, over the walk (frames 32 to 1129), the frame-to-frame luma changes: the
  largest, the 95th and 99th percentile of their size, the count over 0.6 and over 2, the ten largest, and the changes
  at the anchor frames, each only where the surface's box is at least MIN_ROWS tall in both frames; the same, pooled,
  for world-fixed floor patches along the path while each lies 3 to 12 m ahead; and the mean absolute difference of all pixels of each film from the first, by distance band.
"""

import csv
import importlib.util
import json
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("hallway", HERE.parent / "hallway-light-grid" / "analyze.py")
hallway = importlib.util.module_from_spec(spec)
spec.loader.exec_module(hallway)

WALK = range(32, 1130)
ANCHORS = [38, 975, 1065]  # the steps of main that #21 measured: 58.33 m, 11.48 m and 6.98 m from the nose wall
BANDS = [(40, 60), (17, 40), (9, 17), (0, 9)]
MIN_ROWS = 8  # px: a change counts only where the surface's box is this tall in both frames, so that a box of a few rows,
# whose crop moves by whole pixels, does not count as a change of light. The floor patch is 3 rows tall at 24 m.


# World-fixed floor patches of 3 x 2 m along the path, one every 4 m of z. Each counts while it lies 3 to 12 m ahead of
# the camera, so every patch crosses the outer grid's fade (6.5 to 7.5 m) on screen.
TRACK = [(-1.5, 1.5, z - 1.0, z + 1.0) for z in range(24, -28, -4)]
TRACK_AHEAD = (3.0, 12.0)


def track_corners(x0: float, x1: float, z0: float, z1: float) -> list[tuple[float, float, float]]:
    return [(x0, -3.5, z0), (x1, -3.5, z0), (x0, -3.5, z1), (x1, -3.5, z1)]


def rows_of(box: str) -> int:
    return int(box.split("x")[1]) if box else 0


NAMES = {"base": "main b0f8bbf1b (16 cells)", "guard": "Guarded outer grid (18 cells)"}
COLORS = {"base": "#1f77b4", "guard": "#d62728"}


# The chart of docs/research/hallway-light-grid/analyze.py, with the outer fade of the guarded grid marked.
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
    for edge, label, row in ((6.5, "outer fade, full (6.5 m)", 12), (7.5, "outer fade ends (7.5 m)", 26)):
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


def step_stats(rows: list[dict], key: str, variant: str) -> dict:
    changes = []
    for before, after in zip(rows, rows[1:]):
        a, b = before[f"{key}_{variant}"], after[f"{key}_{variant}"]
        tall = min(rows_of(before[f"{key}_box"]), rows_of(after[f"{key}_box"])) >= MIN_ROWS
        if after["frame"] in WALK and tall:
            changes.append((after["frame"], after["distance"], a, b - a))
    sizes = np.abs(np.array([c[3] for c in changes]))
    largest = sorted(changes, key=lambda c: -abs(c[3]))[:10]
    by_frame = {c[0]: c for c in changes}
    return {
        "frames_measured": len(changes),
        "largest": round(float(sizes.max()), 2),
        "p95": round(float(np.percentile(sizes, 95)), 3),
        "p99": round(float(np.percentile(sizes, 99)), 3),
        "over_0.6": int((sizes > 0.6).sum()),
        "over_2": int((sizes > 2).sum()),
        "ten_largest": [
            {"frame": f, "distance": d, "before": round(b, 2), "change": round(c, 2)} for f, d, b, c in largest
        ],
        "anchors": {
            str(f): {"distance": by_frame[f][1], "change": round(by_frame[f][3], 2)} if f in by_frame else None
            for f in ANCHORS
        },
    }


def track_stats(rows: list[dict], variant: str) -> dict:
    """The frame-to-frame luma changes of every tracked floor patch, pooled, with the distance ahead of each change."""
    changes = []
    for i, (_, _, z0, z1) in enumerate(TRACK):
        for before, after in zip(rows, rows[1:]):
            a, b = before[f"track{i}_{variant}"], after[f"track{i}_{variant}"]
            if after["frame"] in WALK and a != "" and b != "":
                ahead = round(hallway.camera_z(after["frame"]) - 0.5 * (z0 + z1), 2)
                changes.append((after["frame"], i, ahead, b - a))
    sizes = np.abs(np.array([c[3] for c in changes]))
    fade = np.abs(np.array([c[3] for c in changes if 6.0 <= c[2] <= 8.0]))
    return {
        "changes": len(changes),
        "largest": round(float(sizes.max()), 2),
        "p95": round(float(np.percentile(sizes, 95)), 3),
        "over_0.6": int((sizes > 0.6).sum()),
        "over_2": int((sizes > 2).sum()),
        "largest_6_to_8_m_ahead": round(float(fade.max()), 2),
        "over_0.6_6_to_8_m_ahead": int((fade > 0.6).sum()),
        "ten_largest": [
            {"frame": f, "patch": i, "ahead": d, "change": round(c, 2)}
            for f, i, d, c in sorted(changes, key=lambda c: -abs(c[3]))[:10]
        ],
    }


def main() -> int:
    films, out = Path(sys.argv[1]), Path(sys.argv[2])
    variants = sys.argv[3:]
    out.mkdir(parents=True, exist_ok=True)
    readers = [hallway.frames(films / v / "walk.mkv") for v in variants]
    rows, diffs = [], {v: [] for v in variants[1:]}
    for frame, images in enumerate(zip(*readers)):
        cz = hallway.camera_z(frame)
        row = {"frame": frame, "distance": round(cz - hallway.NOSE_Z, 3)}
        stack = {v: img.astype(np.float32) for v, img in zip(variants, images)}
        for key, corners in (("wall", hallway.WALL), ("floor", hallway.FLOOR)):
            region = hallway.box(corners, cz)
            row[f"{key}_box"] = "" if region is None else f"{region[3] - region[2]}x{region[1] - region[0]}"
            for v in variants:
                if region is None:
                    row[f"{key}_{v}"] = ""
                    continue
                v0, v1, u0, u1 = region
                row[f"{key}_{v}"] = round(hallway.luma(stack[v][v0:v1, u0:u1].reshape(-1, 3).mean(axis=0)), 3)
        for i, (x0, x1, z0, z1) in enumerate(TRACK):
            ahead = cz - 0.5 * (z0 + z1)
            region = hallway.box(track_corners(x0, x1, z0, z1), cz)
            for v in variants:
                inside = region is not None and TRACK_AHEAD[0] <= ahead <= TRACK_AHEAD[1]
                if not inside or region[1] - region[0] < MIN_ROWS:
                    row[f"track{i}_{v}"] = ""
                    continue
                v0, v1, u0, u1 = region
                row[f"track{i}_{v}"] = round(hallway.luma(stack[v][v0:v1, u0:u1].reshape(-1, 3).mean(axis=0)), 3)
        for v in variants[1:]:
            diffs[v].append((row["distance"], float(np.abs(stack[v] - stack[variants[0]]).mean())))
        rows.append(row)

    with (out / "surfaces.csv").open("w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    summary = {
        "steps": {f"{key}_{v}": step_stats(rows, key, v) for key in ("wall", "floor") for v in variants},
        "floor_track": {v: track_stats(rows, v) for v in variants},
        "mean_abs_diff_8bit": {},
    }
    for v, values in diffs.items():
        entry = {"walk": round(float(np.mean([d for _, d in values])), 4)}
        for lo, hi in BANDS:
            band = [d for dist, d in values if lo <= dist < hi]
            entry[f"{lo}-{hi} m"] = round(float(np.mean(band)), 4) if band else None
        summary["mean_abs_diff_8bit"][f"{v}_vs_{variants[0]}"] = entry
    (out / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    named = [{**r, **{f"{k}_{v}_luma": r[f"{k}_{v}"] for k in ("wall", "floor") for v in variants}} for r in rows]
    for key, title, name in (
        ("wall", "Nose wall beside the window: luma (8-bit sRGB) as the camera approaches", "nose-wall.svg"),
        ("floor", "Floor 1 to 3 m before the nose wall: luma (8-bit sRGB) as the camera approaches", "floor.svg"),
    ):
        chart(named, key, title, out / name, variants)
    print(json.dumps(summary, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
