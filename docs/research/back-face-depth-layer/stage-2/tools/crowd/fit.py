#!/usr/bin/env python3
"""Throwaway (t1-kiaw): the cost model of the back-face pass, from the takes of several crowd sizes.

    python3 -I fit.py <mac|cloud> <takes folder> <counts folder>

The takes folder holds crowd-<n>/ for n = 1, 10, 25, 50 (where present) and crowd/ for n = 100, from job-crowd.sh or
mac-take.sh. For each size, the frame cost is the mean of the per-round differences pass minus off, over the rounds
that every side ran (on the Mac, also with every run's mean clock within 1 MHz, as crowd_report.py keeps them). On the
card, the pass's own time is the median over those runs of the `back faces` pass's median. The vertices are 20,202 n
plus the static vertices that counts.sh found in the crowd. The fit is a least-squares line of each cost against the
vertices, with the residual of each point.
"""

import json
import statistics as st
import sys
import re
from pathlib import Path

CLOCK_TOLERANCE = 1.0


def run_values(folder: Path, side: str, cloud: bool):
    out = {}
    for path in folder.glob(f"{side}/run-*/gpu-timings.json"):
        d = json.loads(path.read_text())
        passes = {p["pass"]: p["medianMs"] for p in d.get("passes", [])}
        span = d["frame"]["medianMs"] - (passes.get("capture", 0.0) if cloud else 0.0)
        out[int(path.parts[-2].removeprefix("run-"))] = (span, passes, (d.get("gpuClock") or {}).get("meanMhz"))
    return out


def fit(xs, ys):
    mx, my = st.mean(xs), st.mean(ys)
    slope = sum((x - mx) * (y - my) for x, y in zip(xs, ys)) / sum((x - mx) ** 2 for x in xs)
    return my - slope * mx, slope


kind, takes, count_dir = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
cloud = kind == "cloud"
line = count_dir.joinpath("crowd", "counts.txt").read_text().splitlines()[-1]
static = int(re.search(r"vertices (\d+)", line).group(1)) - 100 * 20202
print(f"static vertices of the crowd: {static}")
points = []
for n in [1, 10, 25, 50, 100]:
    folder = takes / ("crowd" if n == 100 else f"crowd-{n}")
    if not folder.exists():
        continue
    on, off = run_values(folder, "pass", cloud), run_values(folder, "off", cloud)
    rounds = sorted(set(on) & set(off))
    if not cloud:
        rounds = [r for r in rounds if None not in (on[r][2], off[r][2]) and abs(on[r][2] - off[r][2]) <= CLOCK_TOLERANCE]
    diffs = [on[r][0] - off[r][0] for r in rounds]
    own = st.median(on[r][1]["back faces"] for r in rounds) if cloud else None
    vertices = 20202 * n + static
    points.append((n, vertices, st.mean(diffs), own))
    own_text = f"  own {own:.3f}" if cloud else ""
    print(f"n {n:3}: vertices {vertices:9,}  pass minus off {st.mean(diffs):+.3f} ({min(diffs):+.3f} to {max(diffs):+.3f}), "
          f"rounds {len(rounds)}{own_text}")
for label, idx in [("pass minus off", 2)] + ([("own time", 3)] if cloud else []):
    a, b = fit([p[1] / 1e6 for p in points], [p[idx] for p in points])
    print(f"fit of {label}: {a:+.4f} ms + {b:.4f} ms per million vertices")
    for p in points:
        print(f"  n {p[0]:3}: measured {p[idx]:.3f}, line {a + b * p[1] / 1e6:.3f}, residual {p[idx] - a - b * p[1] / 1e6:+.4f}")
