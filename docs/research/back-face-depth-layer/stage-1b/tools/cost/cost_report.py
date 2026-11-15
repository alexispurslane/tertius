#!/usr/bin/env python3
"""Throwaway (t1-stwp): the report of the cost of stage 1b on both machines.

    python3 -I cost_report.py mac <mac folder>
    python3 -I cost_report.py cloud <job folder>...

A run's value is the median GPU frame span of its 120 filmed frames, in ms. On the cloud it leaves out the `capture`
pass, the headless readback over PCIe (tools/runpod/ab_report.py does the same). The Mac runs have no pass time
stamps, so their value is the whole span.

A round is one run of every side. Mac folder: <scene>/<side>/run-<round>/gpu-timings.json, from mac-take2.sh. Cloud job
folder: out-<scene>/<side>/run-<round>/gpu-timings.json, from ab_run.sh, whose run N of every tree forms round N.
A difference pairs two sides inside one round. The report keeps a Mac round only when every side ran and every run of
it held the same GPU clock: the mean MHz of gpu_clock.py's record agrees within CLOCK_TOLERANCE over the round's
runs. It names each dropped round and the reason.
"""

import json
import statistics as st
import sys
from pathlib import Path

SIDES = ["main", "layer", "ten", "off"]
PAIRS = [("layer", "main"), ("ten", "main"), ("off", "main"), ("layer", "ten"), ("ten", "off")]
# MHz: the means of one round must agree this closely. The top state is 1296 MHz, and one 100 ms sample in the next
# lower state moves a mean of about 40 samples by more than 1 MHz.
CLOCK_TOLERANCE = 1.0
PASSES = ["G-buffer", "back faces", "surface test", "depth pyramid", "ray plan", "reflection rays", "diffuse rays"]


def read_run(path: Path, cloud: bool):
    d = json.loads(path.read_text())
    passes = {p["pass"]: p["medianMs"] for p in d.get("passes", [])}
    span = d["frame"]["medianMs"] - (passes.get("capture", 0.0) if cloud else 0.0)
    return {"span": span, "clock": (d.get("gpuClock") or {}).get("meanMhz"), "passes": passes}


def read_scene(folder: Path, cloud: bool):
    """side -> round -> run."""
    runs = {}
    for path in folder.glob("*/run-*/gpu-timings.json"):
        side, rnd = path.parts[-3], int(path.parts[-2].removeprefix("run-"))
        runs.setdefault(side, {})[rnd] = read_run(path, cloud)
    return runs


def stats(values):
    return f"mean {st.mean(values):.3f}, {min(values):.3f} to {max(values):.3f} (spread {max(values) - min(values):.3f}), n {len(values)}"


def holds_zero(values):
    """Whether the per-round differences hold 0 in their range: not every round has the same sign."""
    return min(values) <= 0 <= max(values)


def dstats(values):
    inside = "the range holds 0" if holds_zero(values) else "every round has the same sign"
    return f"mean {st.mean(values):+.3f}, {min(values):+.3f} to {max(values):+.3f}, n {len(values)}: {inside}"


def complete_rounds(runs, check_clock: bool):
    rounds = sorted(set().union(*(set(r) for r in runs.values())))
    kept, dropped = [], []
    for rnd in rounds:
        missing = [s for s in SIDES if rnd not in runs.get(s, {})]
        if missing:
            dropped.append((rnd, f"no run of {', '.join(missing)}"))
            continue
        clocks = {runs[s][rnd]["clock"] for s in SIDES}
        if check_clock and (None in clocks or max(clocks) - min(clocks) > CLOCK_TOLERANCE):
            dropped.append((rnd, "clocks " + ", ".join(f"{s} {runs[s][rnd]['clock']}" for s in SIDES)))
            continue
        kept.append(rnd)
    return kept, dropped


def report(name: str, runs, check_clock: bool, also_without_first: bool = False):
    print(f"== {name}")
    kept, dropped = complete_rounds(runs, check_clock)
    for rnd, why in dropped:
        print(f"  dropped round {rnd}: {why}")
    print(f"  rounds kept: {kept}")
    if not kept:
        return
    clocks = [runs[s][r]["clock"] for s in SIDES for r in kept]
    if None not in clocks:
        print(f"  clocks of the kept runs: {min(clocks):.2f} to {max(clocks):.2f} MHz")
    for side in SIDES:
        vals = [runs[side][r]["span"] for r in kept]
        print(f"  {side:5}: {' '.join(f'{v:.3f}' for v in vals)}  ({stats(vals)})")
    for a, b in PAIRS:
        diffs = [runs[a][r]["span"] - runs[b][r]["span"] for r in kept]
        print(f"  {a} minus {b}: {' '.join(f'{v:+.3f}' for v in diffs)}  ({dstats(diffs)}, median {st.median(diffs):+.3f})")
    if also_without_first and len(kept) > 2:
        # The first run of a tree on a fresh worker can run cold (stage 1: about 0.57 ms on the deck), so this line
        # repeats the numbers without the first round.
        later = kept[1:]
        print(f"  without round {kept[0]}:")
        for side in SIDES:
            print(f"    {side:5}: {stats([runs[side][r]['span'] for r in later])}")
        for a, b in PAIRS:
            print(f"    {a} minus {b}: {dstats([runs[a][r]['span'] - runs[b][r]['span'] for r in later])}")
    if any(runs["main"][r]["passes"] for r in kept):
        print("  pass medians, the median over the kept runs, ms:")
        print("    " + "".join(f"{p[:15]:>16}" for p in PASSES))
        for side in SIDES:
            row = []
            for p in PASSES:
                vals = [runs[side][r]["passes"].get(p) for r in kept]
                vals = [v for v in vals if v is not None]
                row.append(f"{st.median(vals):16.3f}" if vals else f"{'-':>16}")
            print(f"    {side:5}" + "".join(row))


def main():
    kind, folders = sys.argv[1], [Path(p) for p in sys.argv[2:]]
    if kind == "mac":
        for scene in sorted(p for p in folders[0].iterdir() if p.is_dir()):
            report(scene.name, read_scene(scene, cloud=False), check_clock=True)
    else:
        for job in folders:
            for scene in sorted(job.glob("out-*")):
                stops = scene / "exposure-stops.txt"
                label = f"{job.name}/{scene.name}, exposure {stops.read_text().strip() if stops.exists() else 'unknown'}"
                report(label, read_scene(scene, cloud=True), check_clock=False, also_without_first=True)


if __name__ == "__main__":
    main()
