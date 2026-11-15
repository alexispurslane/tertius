#!/usr/bin/env python3
"""Throwaway (t1-yy2f, part 2): the counts of the sheet census files, as sheet_report.py of stage 1b counts them.

    python3 -I cards_report.py <census run folder> [...]

For each run and ray kind: the counted rays, their hits, the hits whose crossing lies behind the ray's start (class 3,
reason 2), the hits on a sheet, and the hits on a sheet of class 1 (the ray crosses the hit pixel's plane ahead of its
start, where the eye sees a surface within 2 %). Also the mean share of surface pixels that are sheets."""
import sys
from collections import defaultdict
from pathlib import Path


def read(run):
    c = defaultdict(int)
    frame = {}
    for line in (Path(run) / "census.txt").read_text().splitlines()[1:]:
        p = line.split()
        if p[0] == "frame":
            frame[p[1]] = float(p[3]) / int(p[2])
            continue
        kind = p[0][len("sheet-"):]
        n = int(p[-2])
        if p[1] != "hit":
            c[(kind, "rays")] += n if p[1] == "miss" else 0
            continue
        c[(kind, "rays")] += n
        c[(kind, "hits")] += n
        if p[6] == "3" and p[8] == "2":
            c[(kind, "behind-start")] += n
        if p[2] == "sheet":
            c[(kind, "sheet")] += n
            if p[6] == "1":
                c[(kind, "sheet1")] += n
            if p[6] == "3" and p[8] == "2":
                c[(kind, "sheet-behind")] += n
    return c, frame


print("| Run | Kind | Rays | Hits | Crossing behind the start | Hits on sheets | Of class 1 | On sheets, crossing behind the start | Sheets per surface pixel |")
print("|---|---|---:|---:|---:|---:|---:|---:|---:|")
for run in sys.argv[1:]:
    c, frame = read(run)
    share = frame.get("sheet-pixels", 0) / max(frame.get("surface-pixels", 1), 1)
    for kind in ("reflection", "diffuse"):
        print(
            f"| {Path(run).name} | {kind} | {c[(kind, 'rays')]} | {c[(kind, 'hits')]} | {c[(kind, 'behind-start')]} | "
            f"{c[(kind, 'sheet')]} | {c[(kind, 'sheet1')]} | {c[(kind, 'sheet-behind')]} | {100 * share:.1f} % |"
        )
