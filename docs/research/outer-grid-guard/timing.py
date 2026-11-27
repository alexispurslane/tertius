#!/usr/bin/env python3
"""Research tool (issue #25): paired tables of one Runpod A/B job of the hallway walk.

    python3 -I docs/research/outer-grid-guard/timing.py <job --out folder> <base tree> <changed tree>

tools/runpod/ab_run.sh wrote out/<tree>/run-<n>/gpu-timings.json for each tree, one run of each tree per round, with
the order of the trees reversed in each round. Each run used --gpu-run deformation..outlines, so its first timing is
the per-frame sum of every pass but the headless `capture` readback, with its median and 95th percentile over the
run's frames (docs/research/hallway-light-grid/timing.py, runs_of). A pair is the two runs of one round. The tool
prints Markdown tables of the per-run values and of the paired changes, changed minus base: the median over the pairs,
their range, and how many pairs rise. Then the same for the median of each key pass.
The standard library only.
"""

import importlib.util
import statistics
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("timing21", HERE.parent / "hallway-light-grid" / "timing.py")
timing21 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(timing21)

PASSES = ["directional light volume", "ray plan", "reflection rays", "screen trace resolve", "composite"]


def paired(base: list[float], changed: list[float]) -> str:
    changes = [c - b for b, c in zip(base, changed)]
    median = statistics.median(changes)
    rises = sum(1 for c in changes if c > 0)
    return (
        f"{median:+.3f} ms ({median / statistics.median(base) * 100:+.1f}%) | "
        f"{min(changes):+.3f} to {max(changes):+.3f} | {rises} of {len(changes)}"
    )


def main() -> int:
    job, base_name, changed_name = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
    base = timing21.runs_of(job / "out" / base_name)
    changed = timing21.runs_of(job / "out" / changed_name)
    first = base[0]
    print(f'{first["device"]}, {first["size"]}, {first["frames"]} frames per run, {len(base)} pairs\n')
    print(f"| Value | {base_name} runs (ms) | {changed_name} runs (ms) | Paired change, median | Range | Pairs that rise |")
    print("|---|---|---|---:|---|---:|")
    for key, label in (("median", "GPU pass sum per frame, median"), ("p95", "GPU pass sum per frame, p95")):
        b, c = [r[key] for r in base], [r[key] for r in changed]
        print(f"| {label} | {', '.join(f'{v:.3f}' for v in b)} | {', '.join(f'{v:.3f}' for v in c)} | {paired(b, c)} |")
    for name in PASSES:
        b, c = [r["passes"][name] for r in base], [r["passes"][name] for r in changed]
        print(f"| {name}, median | {', '.join(f'{v:.3f}' for v in b)} | {', '.join(f'{v:.3f}' for v in c)} | {paired(b, c)} |")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
