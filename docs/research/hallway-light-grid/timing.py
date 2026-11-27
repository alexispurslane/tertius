#!/usr/bin/env python3
"""Research tool (issue #21): tables of one Runpod A/B job of the hallway walk.

    python3 -I docs/research/hallway-light-grid/timing.py <job --out folder> [--base merged]

tools/runpod/ab_run.sh wrote out/<tree>/run-<n>/gpu-timings.json for each tree and paired round. Each run used
--gpu-run deformation..outlines, so runs[0] holds the per-frame sum of every pass but the headless `capture` readback,
with its own median and 95th percentile over the run's frames. The tool prints Markdown tables:

1. Per tree: the median over runs of that per-frame median and p95, the range of the run medians, and the change of
   the median against the base in ms and percent, with whether every run of one side lies below every run of the
   other.
2. Per tree: the median over runs of each key pass's median.
3. The card's mean SM clock and temperature over the job, from out/clocks.csv.
tools/runpod/ab_report.py reads the same files, but it takes the span without the capture as the median frame span
minus the median of the capture pass. A difference of medians gives no 95th percentile, and #21 asks for one. So this
tool reads the per-frame sum of --gpu-run instead, whose median and p95 come from the same frames. Over the 51 runs of
jobs 1 to 3 the two medians differ by -0.010 to +0.015 ms. The report of #21 uses this tool's numbers. If later A/B
work needs the p95 too, ab_report.py is the place to add it, so that one tool defines the span.
The standard library only.
"""

import argparse
import csv
import json
import statistics
from pathlib import Path

KEY_PASSES = [
    "directional light volume",
    "diffuse rays",
    "accumulate indirect diffuse",
    "filter indirect diffuse",
    "ray plan",
    "reflection rays",
    "screen trace resolve",
    "accumulate indirect specular",
    "composite",
    "FSR 3.1",
    "capture",
]


def runs_of(tree: Path) -> list[dict]:
    runs = []
    for folder in sorted((p for p in tree.glob("run-*") if p.is_dir()), key=lambda p: int(p.name.split("-")[1])):
        data = json.loads((folder / "gpu-timings.json").read_text())
        span = data["runs"][0]["timing"]
        runs.append(
            {
                "median": span["medianMs"],
                "p95": span["p95Ms"],
                "frames": span["frames"],
                "passes": {p["pass"]: p["medianMs"] for p in data["passes"]},
                "device": data["device"],
                "size": f'{data["width"]}x{data["height"]}',
            }
        )
    return runs


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("job")
    parser.add_argument("--base", default="merged")
    args = parser.parse_args()
    out = Path(args.job) / "out"
    trees = {t.name: runs_of(t) for t in sorted(out.iterdir()) if t.is_dir() and t.name != "warm-up"}
    first = next(iter(trees.values()))[0]
    count = len(next(iter(trees.values())))
    print(f'{first["device"]}, {first["size"]}, {first["frames"]} frames per run, {count} runs per tree\n')
    base = trees[args.base]
    base_median = statistics.median(r["median"] for r in base)
    base_p95 = statistics.median(r["p95"] for r in base)
    print("| Tree | Median (ms) | p95 (ms) | Run medians (ms) | Change of median vs " + args.base + " | Change of p95 | Separation |")
    print("|---|---:|---:|---|---:|---:|---|")
    for name, runs in trees.items():
        median = statistics.median(r["median"] for r in runs)
        p95 = statistics.median(r["p95"] for r in runs)
        each = ", ".join(f'{r["median"]:.3f}' for r in runs)
        if name == args.base:
            change, change95, separation = "—", "—", "—"
        else:
            change = f"{median - base_median:+.3f} ms ({(median / base_median - 1) * 100:+.1f}%)"
            change95 = f"{p95 - base_p95:+.3f} ms ({(p95 / base_p95 - 1) * 100:+.1f}%)"
            mine, theirs = [r["median"] for r in runs], [r["median"] for r in base]
            separated = max(mine) < min(theirs) or min(mine) > max(theirs)
            separation = "every run differs" if separated else "runs overlap"
        print(f"| {name} | {median:.3f} | {p95:.3f} | {each} | {change} | {change95} | {separation} |")
    print("\n| Pass median (ms) | " + " | ".join(trees) + " |")
    print("|---|" + "---:|" * len(trees))
    for name in KEY_PASSES:
        cells = []
        for runs in trees.values():
            values = [r["passes"][name] for r in runs if name in r["passes"]]
            cells.append(f"{statistics.median(values):.3f}" if values else "—")
        print(f"| {name} | " + " | ".join(cells) + " |")
    clocks = Path(args.job) / "out" / "clocks.csv"
    if clocks.exists():
        sm, temperature = [], []
        with clocks.open() as f:
            for row in csv.reader(f):
                try:
                    sm.append(float(row[1].split()[0]))
                    temperature.append(float(row[3]))
                except (IndexError, ValueError):
                    continue
        if sm:
            print(f"\nSM clock: mean {statistics.mean(sm):.0f} MHz ({min(sm):.0f} to {max(sm):.0f}); "
                  f"temperature {min(temperature):.0f} to {max(temperature):.0f} °C over {len(sm)} samples.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
