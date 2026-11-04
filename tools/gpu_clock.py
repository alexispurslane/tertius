"""Tool code (tools/README.md, "The GPU clock of a timed run"): the one owner of the record of the GPU clock over a
timed span, of the check that two spans ran at the same clock, and of the estimate of a time at the windowed clock.

Usage:
    python3 -I tools/gpu_clock.py span <log>
    python3 -I tools/gpu_clock.py record <log> <gpu-timings.json>
    python3 -I tools/gpu_clock.py compare <reference gpu-timings.json> <other gpu-timings.json>

A timed run prints the lines "timed-start" and "timed-end" around its timed work, and runs beside gpu-clock
(tools/gpu-clock.sh), so that its log holds gpu-clock's samples between the two markers. "span" prints the record of the
log's span. "record" also writes it into a headless demo run's gpu-timings.json as the entry "gpuClock". "compare" prints
both runs' clocks, whether they compare, and each time of the frame span and of each pass with its estimate at the
windowed clock.

The clock of a span is the mean clock over the GPU's active time of the samples between the markers: each sample
weighs by the share of its interval in which the GPU was not off (docs/research/temporal-renderer/gpu-calibration.md,
"The clock under our runs"). A sample of gpu-clock covers the 100 ms before its line, so a sample right after
timed-start holds part of the work before it.
"""

import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path

# The windowed clock: the clock of a game in a window whose GPU limits its frame rate (gpu-calibration.md, "The clock of
# a window run"). The budgets of docs/plans/temporal-renderer.md, section 11, hold at it.
WINDOWED_MHZ = 1296.0

# Two spans compare when the higher clock exceeds the lower one by at most this share. A pass whose time follows the
# clock then differs by at most 3 % because of the clock. Five runs of one build of FSR's batched measurement
# (captures/3ug8/upscalers-2, 2026-11-03) ran at 1241.0 to 1272.4 MHz over their timed spans, 2.5 % apart, and their
# medians did not follow those clocks: the run at 1272.4 MHz was the slowest. So 3 % holds the spread of one build at a
# held clock. The seven headless deck runs of the same build ran at 748 to 990 MHz, and their frame span fell from
# 11.80 to 9.61 ms as the clock rose: a difference of that size is the clock, not the build.
TOLERANCE = 0.03

# The error of the estimate at the windowed clock (gpu-calibration.md, "The rule that converts a headless time").
ESTIMATE_ERROR = 0.15

SAME_CLOCK = "same clock"
DIFFERENT_CLOCKS = "different clocks"
UNCHECKED = "unchecked"

START = "timed-start"
END = "timed-end"
# The line that tools/gpu-clock.sh or gpu-clock prints when it cannot read the clock, with the reason after the colon.
UNKNOWN = re.compile(r"^clock unknown: (.*)$", re.M)
SAMPLE = re.compile(r"^sample t=([0-9.]+) active=([0-9.]+) mhz=([0-9.]+)")


@dataclass(frozen=True)
class Clock:
    """The clock of a timed span: the mean clock over the active time in MHz, the 10th and 90th percentile of the
    samples' clocks by nearest rank, and the count of samples with active time. An unknown clock has mhz None and says
    why in unknown."""

    mhz: float | None
    p10_mhz: float | None
    p90_mhz: float | None
    samples: int
    unknown: str | None

    @staticmethod
    def of_unknown(reason: str) -> "Clock":
        return Clock(mhz=None, p10_mhz=None, p90_mhz=None, samples=0, unknown=reason)


def _at(values, fraction):
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(len(ordered) * fraction))]


def span_clock(log: str) -> Clock:
    """The clock of the timed spans of a log. Each timed-start opens a span, and the next timed-end closes it. A marker
    can follow other text on its line, as in xunit's live output. The clock is unknown when gpu-clock said that it
    cannot read the clock, when the log has no span, or when no sample with active time falls in a span."""
    if found := UNKNOWN.search(log):
        return Clock.of_unknown(found[1].strip())
    inside = False
    spans = 0
    chosen = []
    any_sample = False
    for line in log.splitlines():
        text = line.rstrip()
        if text.endswith(START):
            inside = True
        elif text.endswith(END) and inside:
            inside = False
            spans += 1
        elif match := SAMPLE.match(line):
            any_sample = True
            if inside and float(match[2]) > 0:
                chosen.append((float(match[2]), float(match[3])))
    if spans == 0:
        return Clock.of_unknown(f"the log has no {START} and {END} lines around a timed span")
    if not any_sample:
        return Clock.of_unknown("the log holds no sample of gpu-clock, so gpu-clock did not run beside the work")
    if not chosen:
        return Clock.of_unknown("no sample of gpu-clock with active time falls in the timed span")
    active = sum(share for share, _ in chosen)
    clocks = [mhz for _, mhz in chosen]
    return Clock(
        mhz=sum(share * mhz for share, mhz in chosen) / active,
        p10_mhz=_at(clocks, 0.1),
        p90_mhz=_at(clocks, 0.9),
        samples=len(chosen),
        unknown=None,
    )


def to_json(clock: Clock) -> dict:
    """The record as the entry "gpuClock" of gpu-timings.json."""
    if clock.mhz is None:
        return {"unknown": clock.unknown}
    return {"meanMhz": clock.mhz, "p10Mhz": clock.p10_mhz, "p90Mhz": clock.p90_mhz, "samples": clock.samples}


def from_json(entry: dict | None) -> Clock:
    """The record of an entry "gpuClock", or an unknown clock for a file without one."""
    if entry is None:
        return Clock.of_unknown("the run recorded no clock (gpu-timings.json has no entry gpuClock)")
    if "unknown" in entry:
        return Clock.of_unknown(entry["unknown"])
    return Clock(
        mhz=entry["meanMhz"], p10_mhz=entry["p10Mhz"], p90_mhz=entry["p90Mhz"], samples=entry["samples"], unknown=None
    )


def describe(clock: Clock) -> str:
    """The record as one line of text."""
    if clock.mhz is None:
        return f"unknown ({clock.unknown})"
    return f"{clock.mhz:.1f} MHz (p10 {clock.p10_mhz:.1f}, p90 {clock.p90_mhz:.1f} MHz, {clock.samples} samples)"


def windowed_ms(ms: float, clock: Clock) -> float | None:
    """The estimate of a time at the windowed clock: the measured time times the span's clock over 1296 MHz. It holds
    for a pass whose time follows the clock, within about 15 %. None at an unknown clock."""
    return None if clock.mhz is None else ms * clock.mhz / WINDOWED_MHZ


@dataclass(frozen=True)
class Check:
    """Whether two spans ran at the same clock: the verdict, and the share by which the higher clock exceeds the lower
    one, or None when a clock is unknown."""

    verdict: str
    difference: float | None
    first: Clock
    second: Clock

    def line(self) -> str:
        clocks = f"clocks {describe(self.first)} and {describe(self.second)}"
        if self.verdict == UNCHECKED:
            return f"UNCHECKED: {clocks}. A clock is unknown, so the comparison may hold or not."
        share = f"{self.difference * 100:.1f} %"
        if self.verdict == DIFFERENT_CLOCKS:
            return (
                f"INVALID: {clocks} differ by {share}, more than the tolerance of {TOLERANCE * 100:.0f} %. Compare"
                " the estimates at the windowed clock instead."
            )
        return f"Same clock: {clocks} differ by {share}, within the tolerance of {TOLERANCE * 100:.0f} %."


def compare(first: Clock, second: Clock) -> Check:
    """The check of two spans' clocks against TOLERANCE."""
    if first.mhz is None or second.mhz is None:
        return Check(verdict=UNCHECKED, difference=None, first=first, second=second)
    difference = max(first.mhz, second.mhz) / min(first.mhz, second.mhz) - 1
    # A small margin, so that a difference of exactly the tolerance compares despite rounding.
    verdict = SAME_CLOCK if difference <= TOLERANCE + 1e-9 else DIFFERENT_CLOCKS
    return Check(verdict=verdict, difference=difference, first=first, second=second)


def compare_timings(reference: dict, other: dict) -> list[str]:
    """The lines that compare two headless runs' gpu-timings.json: the check of their clocks, then a table of the mean
    frame span and the mean of each pass of either run, each with its estimate at the windowed clock."""
    first, second = from_json(reference.get("gpuClock")), from_json(other.get("gpuClock"))

    def estimate(ms, clock):
        value = windowed_ms(ms, clock)
        return "" if value is None else f"{value:.3f}"

    lines = [
        compare(first, second).line(),
        "",
        f"The estimates convert each mean to the windowed clock, {WINDOWED_MHZ:.0f} MHz: the mean times the run's clock"
        f" over {WINDOWED_MHZ:.0f} MHz. They are estimates, within about {ESTIMATE_ERROR * 100:.0f} %.",
        "",
        "| Row | Reference mean, ms | Other mean, ms | Other minus reference, ms | Reference at the windowed clock, estimate, ms | Other at the windowed clock, estimate, ms |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    rows = [("frame span", reference["frame"]["meanMs"], other["frame"]["meanMs"])]
    # A pass of one run only, such as a new pass, has an empty cell in the other run's column.
    reference_means = {each["pass"]: each["meanMs"] for each in reference["passes"]}
    other_means = {each["pass"]: each["meanMs"] for each in other["passes"]}
    names = list(reference_means) + [name for name in other_means if name not in reference_means]
    rows += [(name, reference_means.get(name), other_means.get(name)) for name in names]
    for name, a, b in rows:
        difference = "" if a is None or b is None else f"{b - a:+.3f}"
        lines.append(
            f"| {name} | {'' if a is None else f'{a:.3f}'} | {'' if b is None else f'{b:.3f}'} | {difference} | "
            f"{'' if a is None else estimate(a, first)} | {'' if b is None else estimate(b, second)} |"
        )
    return lines


def record(log: Path, timings: Path) -> Clock:
    """Writes the record of the log's span into gpu-timings.json as "gpuClock", and returns it."""
    clock = span_clock(log.read_text())
    report = json.loads(timings.read_text())
    report["gpuClock"] = to_json(clock)
    timings.write_text(json.dumps(report, indent=2) + "\n")
    return clock


def main() -> int:
    command = sys.argv[1] if len(sys.argv) > 1 else ""
    if command == "span" and len(sys.argv) == 3:
        print(f"GPU clock over the timed span: {describe(span_clock(Path(sys.argv[2]).read_text()))}")
        return 0
    if command == "record" and len(sys.argv) == 4:
        print(f"GPU clock over the timed span: {describe(record(Path(sys.argv[2]), Path(sys.argv[3])))}")
        return 0
    if command == "compare" and len(sys.argv) == 4:
        reference, other = (json.loads(Path(path).read_text()) for path in sys.argv[2:4])
        print("\n".join(compare_timings(reference, other)))
        return 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
