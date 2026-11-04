#!/usr/bin/env python3
"""Tool code. The tests of tools/fsr3-timing/analyze.py, the comparison of FSR's timing rounds, on small synthetic out
folders of time-round.sh: the times of each run from its log, which rounds enter the paired reading, when the tool
refuses a build, and the clock of a build.

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


def run_log(mhz: float | None, ms: float) -> str:
    """The log of one run of FSR's batched measurement: its lines as xunit's live output prints them, with the markers
    around one sample of gpu-clock, or without the markers and the sample, as where gpu-clock cannot run. Every one of
    its 11 batches has a mean frame time of ms."""
    prefix = "    Tertius.Rendering.Tests.Fsr3UpscalerTests.Batched [OUTPUT] "
    span = [] if mhz is None else [
        f"{prefix}timed-start", f"sample t=0.100 active=1.000 mhz={mhz:.1f} states=0,0,0,0,0,1", f"{prefix}timed-end"
    ]
    summary = (
        f"FSR 3.1 in batches of 8 from 1280 x 720 to 1920 x 1080 on Apple M1 Max: median {ms:.3f} ms, 10th percentile"
        f" {ms:.3f} ms, 90th percentile {ms:.3f} ms over 11 batches"
    )
    return "\n".join([*span, prefix + summary, f"{prefix}frame times, ms: " + " ".join([f"{ms:.4f}"] * 11)]) + "\n"


def write_rounds(folder: str, *rounds: tuple) -> None:
    """Writes an out folder of time-round.sh with rounds of base and other, each round given as the two runs' clocks.
    The other build is 0.1 ms slower in every batch."""
    log = []
    for number, clocks in enumerate(rounds):
        for build, mhz, ms in (("base", clocks[0], 1.0), ("other", clocks[1], 1.1)):
            Path(folder, f"{number}-{build}.log").write_text(run_log(mhz, ms))
            log.append(f"round={number} order=0 build={build} exit=0 uptime=[ 09:24:13  up 1 day,  load average: 6.12, 8.56, 10.17]")
    Path(folder, "rounds.log").write_text("\n".join(log) + "\n")


def analyze_folder(folder: str) -> list[str]:
    """The output lines of analyze.py on an out folder, with base as the reference and other as the build."""
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        analyze.main(folder, "base", ["other"])
    return output.getvalue().splitlines()


def analyze_rounds(*rounds: tuple) -> list[str]:
    """The output lines of analyze.py on rounds of base and other, as write_rounds gives them."""
    with tempfile.TemporaryDirectory() as folder:
        write_rounds(folder, *rounds)
        return analyze_folder(folder)


def paired_cells(lines: list[str]) -> list[str]:
    """The cells of the other build's row of the median's paired table."""
    start = next(index for index, line in enumerate(lines) if line.startswith("median: "))
    row = next(line for line in lines[start:] if line.startswith("other |"))
    return [cell.strip() for cell in row.split("|")]


class BatchedTimes(unittest.TestCase):
    """The times of a run are the mean frame times of the batches of FSR's batched measurement, which the run's log
    holds."""

    def test_the_times_of_each_run_come_from_the_frame_times_line_of_its_log(self):
        cells = paired_cells(analyze_rounds(SAME, SAME))
        self.assertEqual(cells[2], "+0.100 (+0.100 to +0.100)")

    def test_the_first_line_counts_the_batches_of_each_run_and_the_frames_of_each_batch(self):
        lines = analyze_rounds(SAME, SAME)
        self.assertEqual(lines[0], "2 rounds, batches per run [11], frames per batch [8]")

    def test_a_log_of_the_per_frame_measurement_is_refused_with_the_path_of_its_log(self):
        # The per-frame test prints a line of frame times too, but its summary names no batch size.
        prefix = "    Tertius.Rendering.Tests.Fsr3UpscalerTests.PerFrame [OUTPUT] "
        per_frame = (
            f"{prefix}FSR 3.1 from 1280 x 720 to 1920 x 1080 on Apple M1 Max: median 1.000 ms, 10th percentile 1.000 ms,"
            f" 90th percentile 1.000 ms over 300 frames\n{prefix}frame times, ms: " + " ".join(["1.0000"] * 300) + "\n"
        )
        with tempfile.TemporaryDirectory() as folder:
            write_rounds(folder, SAME, SAME)
            Path(folder, "1-other.log").write_text(per_frame)
            with self.assertRaisesRegex(ValueError, "1-other.log"):
                analyze_folder(folder)

    def test_a_log_without_any_frame_times_is_refused_with_the_path_of_its_log(self):
        with tempfile.TemporaryDirectory() as folder:
            write_rounds(folder, SAME, SAME)
            Path(folder, "1-other.log").write_text("timed-start\ntimed-end\n")
            with self.assertRaisesRegex(ValueError, "1-other.log"):
                analyze_folder(folder)

    def test_a_missing_log_stops_the_tool_with_the_path_of_the_log(self):
        with tempfile.TemporaryDirectory() as folder:
            write_rounds(folder, SAME, SAME)
            Path(folder, "1-other.log").unlink()
            with self.assertRaisesRegex(FileNotFoundError, "1-other.log"):
                analyze_folder(folder)


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
