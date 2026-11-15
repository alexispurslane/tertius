#!/usr/bin/env python3
"""Throwaway (t1-gxzg): the counts of part 3 from the sheet census files of job-census.sh.

  python3 -I sheet_report.py <census out folder>

For each view: the share of surface pixels that are sheets (mean over the counted draws), and for each mode and ray
kind the counted rays, their hits, the hits whose crossing lies behind the ray's start (class 3, reason 2), the hits on
a sheet, and the hits on a sheet whose crossing is class 1 (the ray crosses the hit surface's plane ahead of its start,
where the eye sees that surface). The share of the 10 % rule's count follows."""
import sys
from collections import defaultdict
from pathlib import Path

out = Path(sys.argv[1])
modes = ["ten", "layer", "layer-plane"]
views = sorted({p.name[len("sheet-"):-len("-ten")] for p in out.glob("sheet-*-ten")})


def read(run):
    c = defaultdict(int)
    frame = {}
    for line in (run / "census.txt").read_text().splitlines()[1:]:
        p = line.split()
        if p[0] == "frame":
            frame[p[1]] = float(p[3]) / int(p[2])
            continue
        kind = p[0][len("sheet-"):]
        n = int(p[-2])
        if p[1] != "hit":
            c[(kind, "rays")] += n if p[1] in ("miss",) else 0
            continue
        c[(kind, "rays")] += n
        c[(kind, "hits")] += n
        if p[6] == "3" and p[8] == "2":
            c[(kind, "behind-start")] += n
        if p[2] == "sheet":
            c[(kind, "sheet")] += n
            if p[6] == "1":
                c[(kind, "sheet1")] += n
    return c, frame


for view in views:
    data = {m: read(out / f"sheet-{view}-{m}") for m in modes}
    frame = data["ten"][1]
    print(f"{view}: sheets {frame['sheet-pixels']:,.0f} of {frame['surface-pixels']:,.0f} surface pixels "
          f"({100 * frame['sheet-pixels'] / frame['surface-pixels']:.1f} %)")
    for kind in ("reflection", "diffuse"):
        base = data["ten"][0]
        for m in modes:
            c = data[m][0]
            print(f"  {kind:10} {m:12} rays {c[(kind, 'rays')]:>9,} hits {c[(kind, 'hits')]:>9,}"
                  f" behind the start {c[(kind, 'behind-start')]:>9,} on a sheet {c[(kind, 'sheet')]:>7,}"
                  f" ({100 * c[(kind, 'sheet')] / max(base[(kind, 'sheet')], 1):5.1f} % of ten), class 1 {c[(kind, 'sheet1')]:>7,}"
                  f" ({100 * c[(kind, 'sheet1')] / max(base[(kind, 'sheet1')], 1):5.1f} % of ten)")
    print("  " + "\n  ".join((out / f"{view}-slope.txt").read_text().splitlines()))
