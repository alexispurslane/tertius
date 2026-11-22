#!/usr/bin/env python3
"""Throwaway (t1-kiaw): the summary of the back-face pass's draw counts per view, from counts.sh.

    python3 -I counts_report.py <counts folder>

For each view: the drawn frames, and the lowest, median and highest count over them of the vertices, the deformed
vertices and the indices that the pass of stage 1b draws, and of the vertices that the pass of Design 1 (cards) draws.
"""

import re
import statistics as st
import sys
from pathlib import Path

FIELDS = ["commands", "instances", "indices", "vertices", "deformed"]


def parse(line):
    pass1b, cards = line.split("|")
    get = lambda part: {k: int(re.search(rf"{k} (-?\d+)", part).group(1)) for k in FIELDS}
    return get(pass1b), get(cards)


def spread(values):
    return f"{min(values):,} / {int(st.median(values)):,} / {max(values):,}"


for folder in sorted(Path(sys.argv[1]).iterdir()):
    path = folder / "counts.txt"
    if not path.exists():
        continue
    rows = [parse(l) for l in path.read_text().splitlines() if l.strip()]
    if not rows:
        continue
    print(f"== {folder.name}: {len(rows)} frames (lowest / median / highest)")
    for key in FIELDS:
        print(f"  pass 1b {key:9}: {spread([r[0][key] for r in rows])}")
    print(f"  cards   vertices : {spread([r[1]['vertices'] for r in rows])}")
    print(f"  cards   indices  : {spread([r[1]['indices'] for r in rows])}")
