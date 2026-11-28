#!/usr/bin/env python3
"""Prepare or measure the issue #26 hallway walk. Use the Nix dev shell."""

import argparse
import csv
import importlib.util
import json
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("hallway", HERE.parent / "hallway-light-grid" / "analyze.py")
hallway = importlib.util.module_from_spec(spec)
spec.loader.exec_module(hallway)

FORWARD_FIRST = 32
FORWARD_END = 1130
REVERSE_START = 1190
REVERSE_END = 2290
RETURN_CUT = 2322
PATCH_MARGIN = 6
MIN_ROWS = 8
OUTSIDE_GRID_M = 7.5


def key(kind, code):
    return {
        "do": "post",
        "to": {"kind": "topic", "name": "input"},
        "event": "InputEvent",
        "payload": {"kind": kind, "code": code},
    }


def film(name, count):
    return {"do": "film", "name": name, "frames": count, "every": 1}


def prepare(script, short):
    """Start with #21's script and keep its complete forward walk."""
    source = json.loads((HERE.parents[2] / "scripts/hallway-walk.json").read_text())
    steps = source["steps"]
    if short:
        walk = next(index for index, step in enumerate(steps) if step.get("name") == "walk")
        source["steps"] = steps[:walk] + [film("walk", 60)]
    else:
        steps.extend([
            key("keyDown", 83),
            film("reverse", 1100),
            key("keyUp", 83),
            {"do": "dump", "name": "back-at-aft-wall"},
            film("reverse-stop", 30),
            {"do": "camera", "entity": "far"},
            film("cut-to-far", 2),
            {"do": "camera", "entity": "person"},
            {"do": "dump", "name": "cut-to-person"},
            film("cut-to-person", 30),
        ])
    script.write_text(json.dumps(source, indent=2) + "\n")


def camera_z(frame):
    if frame < FORWARD_END:
        return hallway.camera_z(frame)
    if frame < REVERSE_START:
        return max(-25.82, -25.77 - 0.05 * (frame - FORWARD_END))
    if frame < REVERSE_END:
        return -25.82 + 0.05 * max(0, frame - REVERSE_START - 1)
    if frame == REVERSE_END:
        return 29.13
    if frame in (RETURN_CUT - 2, RETURN_CUT - 1):
        return None
    return 29.18


def read_samples(video):
    for frame, image in enumerate(hallway.frames(video)):
        z = camera_z(frame)
        box = None if z is None else hallway.box(hallway.WALL, z)
        if box is None:
            yield {"frame": frame}
            continue
        y0, y1, x0, x1 = box
        yield {
            "frame": frame,
            "distance_m": round(z - hallway.NOSE_Z, 3),
            "rows": y1 - y0,
            "patch": hallway.luma(image[y0:y1, x0:x1].mean(axis=(0, 1))),
            "margin": hallway.luma(image[
                max(0, y0 - PATCH_MARGIN):y1 + PATCH_MARGIN,
                max(0, x0 - PATCH_MARGIN):x1 + PATCH_MARGIN,
            ].mean(axis=(0, 1))),
        }


def changes(rows, key, first, end, distance=0):
    steps = [
        {
            "frame": after["frame"],
            "distance_m": after["distance_m"],
            "change": after[key] - before[key],
        }
        for before, after in zip(rows, rows[1:])
        if first <= after["frame"] < end
        and min(before.get("rows", 0), after.get("rows", 0)) >= MIN_ROWS
        and min(before["distance_m"], after["distance_m"]) > distance
    ]
    if not steps:
        return {}
    sizes = np.abs([step["change"] for step in steps])
    return {
        "samples": len(steps),
        "largest": round(float(sizes.max()), 3),
        "p95": round(float(np.percentile(sizes, 95)), 3),
        "over_0.6": int((sizes > 0.6).sum()),
        "over_2": int((sizes > 2).sum()),
        "largest_steps": sorted(steps, key=lambda step: -abs(step["change"]))[:8],
    }


def measure(video, out):
    rows = list(read_samples(video))
    if len(rows) not in (90, 1190, 2352):
        raise ValueError(f"Unexpected frame count {len(rows)} for {video}")
    out.mkdir(parents=True, exist_ok=True)
    result = {}
    for key in ("patch", "margin"):
        result[key] = {
            "forward": changes(rows, key, FORWARD_FIRST, FORWARD_END),
            "forward_outside_grids": changes(rows, key, FORWARD_FIRST, FORWARD_END, OUTSIDE_GRID_M),
        }
        if len(rows) > REVERSE_START:
            result[key]["reverse"] = changes(rows, key, REVERSE_START + 2, REVERSE_END)
            result[key]["reverse_outside_grids"] = changes(
                rows, key, REVERSE_START + 2, REVERSE_END, OUTSIDE_GRID_M
            )
            result[key]["return_cut"] = [rows[i][key] for i in range(RETURN_CUT, RETURN_CUT + 6)]
            result[key]["before_cuts"] = rows[RETURN_CUT - 3][key]
    with (out / "samples.csv").open("w", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=["frame", "distance_m", "rows", "patch", "margin"])
        writer.writeheader()
        writer.writerows(rows)
    (out / "summary.json").write_text(json.dumps(result, indent=2) + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    preparation = actions.add_parser("prepare")
    preparation.add_argument("script", type=Path)
    preparation.add_argument("--short", action="store_true")
    measurement = actions.add_parser("measure")
    measurement.add_argument("video", type=Path)
    measurement.add_argument("out", type=Path)
    args = parser.parse_args()
    if args.action == "prepare":
        prepare(args.script, args.short)
    else:
        measure(args.video, args.out)
