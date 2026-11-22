#!/usr/bin/env python3
"""Throwaway (t1-kiaw): the report of one timing take of the crowd cost, after stage 1b's cost_report.py.

    python3 -I crowd_report.py <mac|cloud> <scene folder> <base side> [<a>:<b> ...]

A scene folder holds <side>/run-<round>/gpu-timings.json, from mac-take.sh or job-crowd.sh. A run's value is the
median GPU frame span of its filmed frames, in ms. On the cloud it leaves out the `capture` pass, the headless readback
over PCIe, as tools/runpod/ab_report.py does. A round is one run of every side. The report keeps a Mac round only when
every run of it held the same GPU clock: the mean MHz of gpu_clock.py's record agrees within CLOCK_TOLERANCE.

Each side's difference to the base side pairs the two runs of one round. The marks are those of stage 1b's cost.md:
'‡' means that the per-round range of a difference holds 0, and '†' means that the size of the mean difference is at
most the base side's run range, its slowest kept run minus its fastest. Each further <a>:<b> prints a minus b.
"""

import json
import statistics as st
import sys
from pathlib import Path

CLOCK_TOLERANCE = 1.0  # MHz, as in stage 1b
PASSES = ["deformation", "shadow maps", "G-buffer", "back faces", "surface test", "depth pyramid", "reflection rays"]


def read_run(path: Path, cloud: bool):
    d = json.loads(path.read_text())
    passes = {p["pass"]: p["medianMs"] for p in d.get("passes", [])}
    span = d["frame"]["medianMs"] - (passes.get("capture", 0.0) if cloud else 0.0)
    return {"span": span, "clock": (d.get("gpuClock") or {}).get("meanMhz"), "passes": passes}


def read_scene(folder: Path, cloud: bool):
    runs = {}
    for path in folder.glob("*/run-*/gpu-timings.json"):
        side, rnd = path.parts[-3], int(path.parts[-2].removeprefix("run-"))
        runs.setdefault(side, {})[rnd] = read_run(path, cloud)
    return runs


def exposures(folder: Path):
    """The distinct exposure stops of every frame of every run, from exposure.json."""
    seen = set()
    for path in folder.glob("*/run-*/exposure.json"):
        for e in json.loads(path.read_text()):
            seen.add(e.get("stops", e.get("Stops")))
    return seen


def marks(diffs, base_range):
    m = st.mean(diffs)
    out = ""
    if min(diffs) <= 0 <= max(diffs):
        out += " ‡"
    if abs(m) <= base_range:
        out += " †"
    return out


def line(values):
    return f"mean {st.mean(values):.3f} ({min(values):.3f} to {max(values):.3f}, spread {max(values) - min(values):.3f}), n {len(values)}"


def dline(diffs, base_range):
    return f"{st.mean(diffs):+.3f} ({min(diffs):+.3f} to {max(diffs):+.3f}){marks(diffs, base_range)}"


def main():
    kind, folder, base = sys.argv[1], Path(sys.argv[2]), sys.argv[3]
    pairs = [tuple(p.split(":")) for p in sys.argv[4:]]
    cloud = kind == "cloud"
    runs = read_scene(folder, cloud)
    sides = sorted(runs)
    rounds = sorted(set().union(*(set(r) for r in runs.values())))
    kept, dropped = [], []
    for rnd in rounds:
        missing = [s for s in sides if rnd not in runs[s]]
        if missing:
            dropped.append((rnd, "no run of " + ", ".join(missing)))
            continue
        clocks = [runs[s][rnd]["clock"] for s in sides]
        if not cloud and (None in clocks or max(clocks) - min(clocks) > CLOCK_TOLERANCE):
            dropped.append((rnd, "clocks " + ", ".join(f"{s} {runs[s][rnd]['clock']}" for s in sides)))
            continue
        kept.append(rnd)
    print(f"== {folder}")
    print(f"  exposure stops of every frame: {sorted(exposures(folder))}")
    for rnd, why in dropped:
        print(f"  dropped round {rnd}: {why}")
    print(f"  rounds kept: {kept}")
    if not kept:
        return
    clocks = [runs[s][r]["clock"] for s in sides for r in kept]
    if None not in clocks:
        print(f"  clocks of the kept runs: {min(clocks):.2f} to {max(clocks):.2f} MHz")
    base_vals = [runs[base][r]["span"] for r in kept]
    base_range = max(base_vals) - min(base_vals)
    for side in sides:
        vals = [runs[side][r]["span"] for r in kept]
        print(f"  {side:8}: {' '.join(f'{v:.3f}' for v in vals)}  ({line(vals)})")
    for side in sides:
        if side == base:
            continue
        diffs = [runs[side][r]["span"] - runs[base][r]["span"] for r in kept]
        print(f"  {side} minus {base}: {' '.join(f'{v:+.3f}' for v in diffs)}  = {dline(diffs, base_range)}")
    for a, b in pairs:
        if a not in runs or b not in runs:
            continue
        diffs = [runs[a][r]["span"] - runs[b][r]["span"] for r in kept]
        print(f"  {a} minus {b}: {' '.join(f'{v:+.3f}' for v in diffs)}  = {dline(diffs, base_range)}")
    if cloud:
        print("  pass medians, the median over the kept runs (lowest to highest), ms:")
        for p in PASSES:
            cells = []
            for side in sides:
                vals = [runs[side][r]["passes"].get(p) for r in kept]
                vals = [v for v in vals if v is not None]
                cells.append(f"{side} {st.median(vals):.3f} ({min(vals):.3f}-{max(vals):.3f})" if vals else f"{side} -")
            print(f"    {p}: " + "; ".join(cells))


if __name__ == "__main__":
    main()
