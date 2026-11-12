#!/usr/bin/env python3
"""Tool code (tools/runpod/WORKBENCH.md): the report of an A/B job on the GPU workbench.

    python3 -I tools/runpod/ab_report.py <the job's --out folder> [--base main]

tools/runpod/ab_run.sh wrote out/<tree>/run-<n>/gpu-timings.json on the worker, one folder per run of each tree. For
each tree the report gives the GPU frame span of each run without the `capture` pass, the headless readback that the
renderer does not do in a window (ter-fw73), and their median. For each tree but the base, it gives the change of the
median against the base's, relative and in ms, whether every run of one side lies below every run of the other, the
estimate of the change on the Mac, and the passes that moved most. The standard library only.
"""

import argparse
import json
import statistics
import sys
from dataclasses import dataclass
from pathlib import Path

# A cut of GPU work on the RTX 2000 Ada shows on the M1 Max at about this many times its size in ms: the deck's frame
# span without the capture, 24.34 ms on the Mac against 10.33 ms on the card (ter-libf, 2026-11-12). The user's rule
# of 2026-11-11 uses 2.4. It does not hold for fixed costs such as dispatches and barriers, which Linux does not pay.
MAC_FACTOR = 2.4
CAPTURE = "capture"


@dataclass(frozen=True)
class Run:
    span: float  # ms: the median GPU frame span of the run, without the capture pass
    passes: dict  # pass name -> its median ms


@dataclass(frozen=True)
class Change:
    base_ms: float
    variant_ms: float
    delta_ms: float
    relative: float
    separated: bool  # every run of one side lies below every run of the other
    mac_ms: float
    passes: list  # (pass, base ms, variant ms), the largest change first


def read(folder: Path) -> dict:
    """tree -> its runs, in run order."""
    trees = {}
    for path in sorted((folder / "out").glob("*/run-*/gpu-timings.json"), key=lambda p: (p.parts[-3], run_number(p))):
        timings = json.loads(path.read_text())
        passes = {entry["pass"]: entry["medianMs"] for entry in timings["passes"]}
        trees.setdefault(path.parts[-3], []).append(
            Run(span=timings["frame"]["medianMs"] - passes.get(CAPTURE, 0.0), passes=passes))
    return trees


def run_number(path: Path) -> int:
    return int(path.parts[-2].removeprefix("run-"))


def median_passes(runs: list) -> dict:
    names = {name for run in runs for name in run.passes if name != CAPTURE}
    return {name: statistics.median(run.passes.get(name, 0.0) for run in runs) for name in names}


def compare(trees: dict, base: str) -> dict:
    """tree -> its Change against the base tree, for every tree but the base."""
    base_runs = [run.span for run in trees[base]]
    base_ms = statistics.median(base_runs)
    base_passes = median_passes(trees[base])
    changes = {}
    for tree, runs in trees.items():
        if tree == base:
            continue
        spans = [run.span for run in runs]
        variant_ms = statistics.median(spans)
        passes = median_passes(runs)
        moved = sorted(((name, base_passes.get(name, 0.0), passes.get(name, 0.0))
                        for name in base_passes.keys() | passes.keys()), key=lambda row: -abs(row[2] - row[1]))
        changes[tree] = Change(base_ms=base_ms, variant_ms=variant_ms, delta_ms=variant_ms - base_ms,
                               relative=variant_ms / base_ms - 1,
                               separated=max(spans) < min(base_runs) or min(spans) > max(base_runs),
                               mac_ms=(variant_ms - base_ms) * MAC_FACTOR, passes=moved)
    return changes


def main(argv: list) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("folder", type=Path, help="the --out folder of the job")
    parser.add_argument("--base", default="main", help="the tree that the others are compared with")
    parser.add_argument("--passes", type=int, default=8, help="how many of the passes that moved most to print")
    args = parser.parse_args(argv)
    trees = read(args.folder)
    if args.base not in trees:
        print(f"{args.folder} has no runs of the tree {args.base}; it has {sorted(trees)}", file=sys.stderr)
        return 2
    print("GPU frame span without the capture pass, ms, per run:")
    for tree, runs in trees.items():
        spans = [run.span for run in runs]
        print(f"  {tree}: median {statistics.median(spans):.3f}, runs {', '.join(f'{s:.3f}' for s in spans)}, "
              f"spread {max(spans) - min(spans):.3f}")
    for tree, change in compare(trees, args.base).items():
        verdict = "every run differs" if change.separated else "the runs overlap: within the noise"
        print(f"\n{tree} against {args.base}: {change.delta_ms:+.3f} ms ({change.relative:+.1%}), {verdict}. "
              f"Estimate on the Mac: {change.mac_ms:+.2f} ms.")
        for name, before, after in change.passes[:args.passes]:
            print(f"  {name}: {before:.3f} -> {after:.3f} ms ({after - before:+.3f})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
