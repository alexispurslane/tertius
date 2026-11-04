#!/usr/bin/env python3
"""Tool code. The tests of tools/fsr3-timing/analyze.py, the comparison of FSR's timing rounds, on small synthetic out
folders of time-round.sh: which rounds enter the paired reading, when the tool refuses a build, and the clock of a build.

    nix develop --command python3 -I tools/test_fsr3_timing.py

The standard library only. The file loads analyze.py by its path, because a hyphen in a folder name stops an import.
"""

import contextlib
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("fsr3_analyze", Path(__file__).parent / "fsr3-timing" / "analyze.py")
analyze = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analyze)

SAME = (1000.0, 1010.0)
DIFFERENT = (800.0, 1000.0)
UNKNOWN = (None, None)


def run_log(mhz: float | None) -> str:
    """The log of one run: the markers around one sample of gpu-clock, or a run of a build before the markers."""
    if mhz is None:
        return "FSR 3.1 from 1280 x 720 to 1920 x 1080: median 1.000 ms\n"
    return f"timed-start\nsample t=0.100 active=1.000 mhz={mhz:.1f} states=0,0,0,0,0,1\ntimed-end\n"


def analyze_rounds(*rounds: tuple) -> list[str]:
    """The output lines of analyze.py on rounds of base and other, each round given as the two runs' clocks. The other
    build is 0.1 ms slower in every frame."""
    with tempfile.TemporaryDirectory() as folder:
        log = []
        for number, clocks in enumerate(rounds):
            for build, mhz, ms in (("base", clocks[0], 1.0), ("other", clocks[1], 1.1)):
                Path(folder, f"{number}-{build}.times").write_text(" ".join([f"{ms:.3f}"] * 11))
                Path(folder, f"{number}-{build}.log").write_text(run_log(mhz))
                log.append(f"round={number} order=0 build={build} exit=0 uptime=[ 09:24:13  up 1 day,  load average: 6.12, 8.56, 10.17]")
        Path(folder, "rounds.log").write_text("\n".join(log) + "\n")
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            analyze.main(folder, "base", ["other"])
    return output.getvalue().splitlines()


def paired_cells(lines: list[str]) -> list[str]:
    """The cells of the other build's row of the median's paired table."""
    start = next(index for index, line in enumerate(lines) if line.startswith("median: "))
    row = next(line for line in lines[start:] if line.startswith("other |"))
    return [cell.strip() for cell in row.split("|")]


class PairedReading(unittest.TestCase):
    """The paired reading holds the rounds at the same clock and the unchecked rounds, and says how many of each."""

    def test_a_round_at_different_clocks_leaves_the_paired_reading(self):
        cells = paired_cells(analyze_rounds(SAME, DIFFERENT, SAME))
        self.assertEqual(cells[2], "+0.100 (+0.100 to +0.100)")
        self.assertEqual(cells[3], "0/2")

    def test_the_row_counts_the_unchecked_rounds_of_its_paired_reading(self):
        cells = paired_cells(analyze_rounds(SAME, UNKNOWN, DIFFERENT))
        self.assertEqual(cells[3], "0/2")
        self.assertEqual(cells[4], "1 unchecked")

    def test_a_build_whose_checked_rounds_all_ran_at_different_clocks_is_refused_despite_an_unchecked_round(self):
        cells = paired_cells(analyze_rounds(DIFFERENT, UNKNOWN, DIFFERENT))
        self.assertTrue(cells[2].startswith("refused"), cells)

    def test_a_build_whose_rounds_are_all_unchecked_keeps_its_paired_reading_as_where_gpu_clock_cannot_run(self):
        cells = paired_cells(analyze_rounds(UNKNOWN, UNKNOWN))
        self.assertEqual(cells[2], "+0.100 (+0.100 to +0.100)")
        self.assertEqual(cells[4], "2 unchecked")


class BuildClock(unittest.TestCase):
    """The clock of a build: the median of its runs' clocks, described as a spread of the runs, not of samples."""

    def test_the_line_of_a_build_gives_the_median_and_spread_of_its_runs_clocks(self):
        lines = analyze_rounds((900.0, 1000.0), (1000.0, 1000.0), (1100.0, 1000.0))
        row = next(line for line in lines if line.startswith("base |"))
        self.assertIn("1000.0 MHz, the median of 3 runs (p10 900.0, p90 1100.0 MHz of the runs)", row)


if __name__ == "__main__":
    unittest.main()
