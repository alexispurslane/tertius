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

Each run's log holds gpu-clock's samples and the test's markers of its timed span, so each run has the GPU clock of
that span (tools/gpu_clock.py). For each build, it prints the clocks of its runs and the median of its run medians at
the windowed clock, an estimate. A round in which the build's clock and the reference's clock differ by more than
gpu_clock.TOLERANCE does not compare: the paired readings leave it out and say how many rounds they left out. A round
with an unknown clock stays in, unchecked, and the row counts such rounds. The paired readings refuse a build whose
checked rounds all ran at different clocks. The pooled readings mix every round, so the line above them marks them
invalid when the builds' median clocks differ by more than the tolerance.
"""

import importlib.util
import random
import re
import statistics
import sys
from dataclasses import dataclass
from pathlib import Path

# python3 -I leaves the script's folder out of the import path, so the module loads by its path.
_spec = importlib.util.spec_from_file_location("gpu_clock", Path(__file__).parent.parent / "gpu_clock.py")
gpu_clock = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(gpu_clock)

RESAMPLES = 2000


def percentile(values, fraction):
    ordered = sorted(values)
    return ordered[int(len(ordered) * fraction)]


def interval(samples):
    ordered = sorted(samples)
    return ordered[RESAMPLES // 20], ordered[RESAMPLES // 2], ordered[RESAMPLES - RESAMPLES // 20]


def load(folder):
    rounds, starts = set(), []
    for line in Path(f"{folder}/rounds.log").read_text().splitlines():
        match = re.match(r"round=(\d+) .*load average: ([0-9.]+)", line)
        if match:
            rounds.add(int(match[1]))
            starts.append(float(match[2]))
    return sorted(rounds), starts


def run_clock(folder, r, b):
    """The clock of the timed span of one run, or an unknown clock for a run without a log."""
    log = Path(f"{folder}/{r}-{b}.log")
    return gpu_clock.span_clock(log.read_text()) if log.exists() else gpu_clock.Unknown(f"{log} is missing")


@dataclass(frozen=True)
class BuildClock:
    """The clock of a build: the median of its runs' clocks over their timed spans in MHz, the 10th and 90th percentile
    of those runs' clocks, and the count of runs with a known clock. gpu_clock.compare and windowed_ms take it as a
    known clock."""

    mhz: float
    p10_mhz: float
    p90_mhz: float
    runs: int

    def describe(self) -> str:
        return (
            f"{self.mhz:.1f} MHz, the median of {self.runs} runs (p10 {self.p10_mhz:.1f}, p90 {self.p90_mhz:.1f} MHz"
            " of the runs)"
        )


def build_clock(clocks):
    """The BuildClock of a build's runs' clocks, or an unknown clock when no run knows its clock."""
    known = sorted(clock.mhz for clock in clocks if isinstance(clock, gpu_clock.Known))
    if not known:
        return gpu_clock.Unknown(clocks[0].reason)
    return BuildClock(
        mhz=percentile(known, 0.5), p10_mhz=percentile(known, 0.1), p90_mhz=percentile(known, 0.9), runs=len(known)
    )


def main(folder, reference, others):
    rounds, starts = load(folder)
    builds = [reference, *others]
    times = {
        (r, b): [float(t) for t in Path(f"{folder}/{r}-{b}.times").read_text().split()] for r in rounds for b in builds
    }
    clocks = {(r, b): run_clock(folder, r, b) for r in rounds for b in builds}
    print(f"{len(rounds)} rounds, frames per run {sorted({len(t) for t in times.values()})}")
    print(f"1-minute load at the run starts: least {min(starts)}, median {statistics.median(starts)}, most {max(starts)}")
    print(
        f"The clock of a build is the median of its runs' clocks over their timed spans (p10 and p90 of the runs). The"
        f" estimate at the windowed clock, {gpu_clock.WINDOWED_MHZ:.0f} MHz, is the median of the run medians, each"
        f" times its run's clock over {gpu_clock.WINDOWED_MHZ:.0f} MHz, within about {gpu_clock.ESTIMATE_ERROR * 100:.0f} %, to hold"
        f" a time against a budget. One build's estimates spread by about {gpu_clock.ESTIMATE_SPREAD * 100:.0f} %, so they do"
        " not compare two builds."
    )
    print("build | pooled median | pooled p10 | pooled p90 | median of run medians | clock | median of run medians at the windowed clock, estimate")
    for b in builds:
        pooled = [t for r in rounds for t in times[(r, b)]]
        medians = [percentile(times[(r, b)], 0.5) for r in rounds]
        windowed = [
            value
            for r in rounds
            if (value := gpu_clock.windowed_ms(percentile(times[(r, b)], 0.5), clocks[(r, b)])) is not None
        ]
        print(
            f"{b} | {percentile(pooled, 0.5):.3f} | {percentile(pooled, 0.1):.3f} | {percentile(pooled, 0.9):.3f} | "
            f"{statistics.median(medians):.3f} | {build_clock([clocks[(r, b)] for r in rounds]).describe()} | "
            f"{f'{statistics.median(windowed):.3f}' if windowed else 'unknown'}"
        )
    print()
    # The rounds of each build's paired reading, and how many of them are unchecked.
    reading, unchecked = {}, {}
    for b in others:
        checks = {r: gpu_clock.compare(clocks[(r, reference)], clocks[(r, b)]) for r in rounds}
        by = {verdict: [r for r in rounds if checks[r].verdict == verdict] for verdict in
              (gpu_clock.SAME_CLOCK, gpu_clock.DIFFERENT_CLOCKS, gpu_clock.UNCHECKED)}
        same, different = by[gpu_clock.SAME_CLOCK], by[gpu_clock.DIFFERENT_CLOCKS]
        # A round whose clocks are unknown stays in, unchecked: the tool still works where gpu-clock cannot run. But
        # when every checked round ran at different clocks, the unchecked rounds alone do not make a reading.
        reading[b] = [] if different and not same else sorted(same + by[gpu_clock.UNCHECKED])
        unchecked[b] = len(by[gpu_clock.UNCHECKED]) if reading[b] else 0
        print(
            f"{b} against {reference}: {len(same)} rounds at the same clock, {len(different)} at different clocks,"
            f" {len(by[gpu_clock.UNCHECKED])} unchecked. Pooled frames: "
            + gpu_clock.compare(
                build_clock([clocks[(r, reference)] for r in rounds]), build_clock([clocks[(r, b)] for r in rounds])
            ).line()
        )
    for name, fraction in (("median", 0.5), ("p10", 0.1)):
        random.seed(7)
        picks = [[random.choice(rounds) for _ in rounds] for _ in range(RESAMPLES)]
        print(f"\n{name}: build minus {reference}, middle and 90 % interval, ms")
        print("build | pooled frames | paired runs at the same clock or unchecked | rounds faster | unchecked rounds in the paired reading")
        for b in others:
            pooled = [
                percentile([t for r in pick for t in times[(r, b)]], fraction)
                - percentile([t for r in pick for t in times[(r, reference)]], fraction)
                for pick in picks
            ]
            low, middle, high = interval(pooled)
            if not reading[b]:
                print(f"{b} | {middle:+.3f} ({low:+.3f} to {high:+.3f}) | refused: no round at the same clock | | ")
                continue
            # The paired reading holds only the rounds at the same clock or unchecked, and its resamples draw from those.
            differences = {
                r: percentile(times[(r, b)], fraction) - percentile(times[(r, reference)], fraction) for r in reading[b]
            }
            paired_picks = [[random.choice(reading[b]) for _ in reading[b]] for _ in range(RESAMPLES)]
            paired = [statistics.median(differences[r] for r in pick) for pick in paired_picks]
            faster = sum(d < 0 for d in differences.values())
            pairedLow, pairedMiddle, pairedHigh = interval(paired)
            print(
                f"{b} | {middle:+.3f} ({low:+.3f} to {high:+.3f}) | "
                f"{pairedMiddle:+.3f} ({pairedLow:+.3f} to {pairedHigh:+.3f}) | {faster}/{len(reading[b])} | "
                f"{unchecked[b]} unchecked"
            )


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3:])
