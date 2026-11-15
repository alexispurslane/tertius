#!/usr/bin/env python3
"""Throwaway (t1-gxzg): the table of part 1 from the sill census files of job-census.sh.

  python3 -I sill_report.py <box> <base run folder> <run folder> ...

For each run it prints the bright samples and their light by class, the false share (the light of class 0 over the
box's bright light) and the false light as a share of the first run's false light. Classes: 0 an opening, 1 the hit
surface, 2 a nearer surface, 3 undetermined, and the samples without a reflection ray or whose walk misses again."""
import sys
from collections import defaultdict
from pathlib import Path

box, runs = sys.argv[1], [Path(p) for p in sys.argv[2:]]


def classes(run):
    c = defaultdict(lambda: [0, 0.0])
    for line in (run / "census.txt").read_text().splitlines():
        parts = line.split()
        if parts[0] == f"sill-{box}":
            key = parts[2] if parts[1] == "crossing" else parts[1]
            c[key][0] += int(parts[-2])
            c[key][1] += float(parts[-1])
    return c


base = classes(runs[0])
for run in runs:
    c = classes(run)
    total = sum(v[1] for v in c.values())
    samples = sum(v[0] for v in c.values())
    false = c["0"][1]
    cells = " ".join(f"{k}: {v[0]} / {v[1]:,.0f}" for k, v in sorted(c.items()))
    print(f"{run.name}: {cells}; total {samples} / {total:,.0f}; false share {100 * false / total:.3f} %;"
          f" false light against {runs[0].name} {100 * false / base['0'][1]:.3f} %")
