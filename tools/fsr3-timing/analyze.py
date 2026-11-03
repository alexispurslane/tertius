"""Tool code (tools/README.md, "FSR 3.1 timing"): compares the frame times that time-round.sh wrote.

Usage: python3 -I tools/fsr3-timing/analyze.py <out folder> <reference build> <build> ...

For each build other than the reference, it prints two readings of "the build's median minus the reference's":

- The pooled frame median: the median of every frame of every run of the build. Its interval comes from resamples
  that draw whole rounds with replacement and pool their frames.
- The paired run median: per round, the median of the build's run minus the median of the reference's run in the
  same round, and then the median of those differences over the rounds. Its interval comes from resamples of the
  per-round differences. It also counts the rounds in which the build was faster.

It prints the same two readings for the 10th percentile, the median of the run medians of each build, and the least,
median and most 1-minute load at the starts of the runs (rounds.log). Every interval holds 90 % of 2,000 resamples.
"""

import random
import re
import statistics
import sys

RESAMPLES = 2000


def percentile(values, fraction):
    ordered = sorted(values)
    return ordered[int(len(ordered) * fraction)]


def interval(samples):
    ordered = sorted(samples)
    return ordered[RESAMPLES // 20], ordered[RESAMPLES // 2], ordered[RESAMPLES - RESAMPLES // 20]


def load(folder):
    rounds, starts = set(), []
    for line in open(f"{folder}/rounds.log"):
        match = re.match(r"round=(\d+) .*load average: ([0-9.]+)", line)
        if match:
            rounds.add(int(match[1]))
            starts.append(float(match[2]))
    return sorted(rounds), starts


def main(folder, reference, others):
    rounds, starts = load(folder)
    builds = [reference, *others]
    times = {
        (r, b): [float(t) for t in open(f"{folder}/{r}-{b}.times").read().split()] for r in rounds for b in builds
    }
    print(f"{len(rounds)} rounds, frames per run {sorted({len(t) for t in times.values()})}")
    print(f"1-minute load at the run starts: least {min(starts)}, median {statistics.median(starts)}, most {max(starts)}")
    print("build | pooled median | pooled p10 | pooled p90 | median of run medians")
    for b in builds:
        pooled = [t for r in rounds for t in times[(r, b)]]
        medians = [percentile(times[(r, b)], 0.5) for r in rounds]
        print(
            f"{b} | {percentile(pooled, 0.5):.3f} | {percentile(pooled, 0.1):.3f} | {percentile(pooled, 0.9):.3f} | "
            f"{statistics.median(medians):.3f}"
        )
    for name, fraction in (("median", 0.5), ("p10", 0.1)):
        random.seed(7)
        picks = [[random.choice(rounds) for _ in rounds] for _ in range(RESAMPLES)]
        print(f"\n{name}: build minus {reference}, middle and 90 % interval, ms")
        print("build | pooled frames | paired runs | rounds faster")
        for b in others:
            pooled = [
                percentile([t for r in pick for t in times[(r, b)]], fraction)
                - percentile([t for r in pick for t in times[(r, reference)]], fraction)
                for pick in picks
            ]
            differences = {
                r: percentile(times[(r, b)], fraction) - percentile(times[(r, reference)], fraction) for r in rounds
            }
            paired = [statistics.median(differences[r] for r in pick) for pick in picks]
            faster = sum(d < 0 for d in differences.values())
            low, middle, high = interval(pooled)
            pairedLow, pairedMiddle, pairedHigh = interval(paired)
            print(
                f"{b} | {middle:+.3f} ({low:+.3f} to {high:+.3f}) | "
                f"{pairedMiddle:+.3f} ({pairedLow:+.3f} to {pairedHigh:+.3f}) | {faster}/{len(rounds)}"
            )


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3:])
