#!/usr/bin/env python3
"""Tool code. The tests of the pure functions of tools/gpu-trace.py, on synthetic GPU intervals in nanoseconds.

    nix develop --command python3 -I tools/test_gpu_trace.py

The standard library only. The file loads gpu-trace.py by its path, because the hyphen in its name stops an import.
"""

import importlib.util
import unittest
from pathlib import Path

_spec = importlib.util.spec_from_file_location("gpu_trace", Path(__file__).with_name("gpu-trace.py"))
gpu_trace = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(gpu_trace)


class BusyAndAloneTimes(unittest.TestCase):
    """busy_and_alone: a pass's busy time is the union of its own intervals, and its time alone is the part of that
    union in which no other pass of any frame has an interval open."""

    def test_two_overlapping_passes_each_count_the_shared_time_as_busy_but_not_as_alone(self):
        times = gpu_trace.busy_and_alone({("frame", "a"): [(0, 10)], ("frame", "b"): [(6, 16)]})
        self.assertEqual(times, {("frame", "a"): (10, 6), ("frame", "b"): (10, 6)})

    def test_a_pass_that_never_overlaps_has_its_whole_busy_time_alone(self):
        times = gpu_trace.busy_and_alone({("frame", "a"): [(0, 10), (20, 25)], ("frame", "b"): [(30, 40)]})
        self.assertEqual(times[("frame", "a")], (15, 15))

    def test_a_pass_that_starts_where_another_ends_shares_no_time_with_it(self):
        times = gpu_trace.busy_and_alone({("frame", "a"): [(0, 10)], ("frame", "b"): [(10, 20)]})
        self.assertEqual(times, {("frame", "a"): (10, 10), ("frame", "b"): (10, 10)})

    def test_a_pass_nested_inside_another_is_never_alone(self):
        times = gpu_trace.busy_and_alone(
            {("frame", "outer"): [(0, 30)], ("frame", "middle"): [(5, 25)], ("frame", "inner"): [(10, 15)]}
        )
        self.assertEqual(times, {("frame", "outer"): (30, 10), ("frame", "middle"): (20, 0), ("frame", "inner"): (5, 0)})

    def test_a_pass_that_holds_one_pass_and_overlaps_another_is_alone_only_outside_both(self):
        times = gpu_trace.busy_and_alone(
            {("frame", "long"): [(0, 30)], ("frame", "held"): [(5, 10)], ("frame", "late"): [(25, 40)]}
        )
        self.assertEqual(times, {("frame", "long"): (30, 20), ("frame", "held"): (5, 0), ("frame", "late"): (15, 10)})

    def test_the_same_pass_in_two_frames_that_overlap_is_not_alone_while_both_run(self):
        times = gpu_trace.busy_and_alone({("first", "a"): [(0, 10)], ("second", "a"): [(8, 20)]})
        self.assertEqual(times, {("first", "a"): (10, 8), ("second", "a"): (12, 10)})

    def test_overlapping_intervals_of_one_pass_count_once_and_leave_it_alone(self):
        times = gpu_trace.busy_and_alone({("frame", "a"): [(0, 10), (5, 12)]})
        self.assertEqual(times, {("frame", "a"): (12, 12)})


class Window(unittest.TestCase):
    """window: the wall-clock GPU time of one frame from the end of one pass's last interval to the start of the first
    interval of another pass that starts at or after that end."""

    def test_the_window_runs_from_the_last_end_of_the_first_pass_to_the_next_start_of_the_second(self):
        frame = {"reflections": [(0, 10), (12, 14)], "bloom": [(5, 8), (20, 30), (40, 50)], "haze": [(14, 19)]}
        self.assertEqual(gpu_trace.window(frame, "reflections", "bloom"), 6)

    def test_a_second_pass_that_starts_exactly_at_the_end_of_the_first_gives_a_window_of_zero(self):
        self.assertEqual(gpu_trace.window({"a": [(0, 10)], "b": [(10, 20)]}, "a", "b"), 0)

    def test_a_frame_without_the_first_pass_has_no_window(self):
        self.assertIsNone(gpu_trace.window({"bloom": [(20, 30)]}, "reflections", "bloom"))

    def test_a_frame_without_the_second_pass_has_no_window(self):
        self.assertIsNone(gpu_trace.window({"reflections": [(0, 10)]}, "reflections", "bloom"))

    def test_a_frame_whose_second_pass_starts_only_before_the_first_pass_ends_has_no_window(self):
        self.assertIsNone(gpu_trace.window({"a": [(0, 10)], "b": [(5, 9)]}, "a", "b"))


class FrameSpan(unittest.TestCase):
    """frame_span: the wall-clock GPU time of one frame from the first start of any of its passes to the last end."""

    def test_the_span_runs_from_the_earliest_start_to_the_latest_end_of_any_pass(self):
        self.assertEqual(gpu_trace.frame_span({"a": [(5, 10), (30, 32)], "b": [(2, 40)], "c": [(41, 45)]}), 43)


MS = 1_000_000
"""One millisecond in nanoseconds, the unit of the synthetic intervals below."""


def _two_frames():
    """Two frames of the passes a, b and c. In frame f1, a and b overlap by 1 ms, and an upload runs before them. In
    frame f2, a runs alone and b starts 1 ms after a ends. c has no GPU work in either frame."""
    return {
        ("f1", gpu_trace.BEFORE): [(0, MS // 2)],
        ("f1", "a"): [(1 * MS, 3 * MS)],
        ("f1", "b"): [(2 * MS, 4 * MS)],
        ("f2", "a"): [(10 * MS, 12 * MS)],
        ("f2", "b"): [(13 * MS, 14 * MS)],
    }


class Summarize(unittest.TestCase):
    """summarize: the statistics of each pass over the frames, in milliseconds, in the order of the frame graph."""

    def setUp(self):
        self.report = gpu_trace.summarize(_two_frames(), ["a", "b", "c"], {}, ("a", "b"))
        self.rows = {entry["pass"]: entry for entry in self.report["passes"]}

    def test_each_pass_gives_its_time_alone_beside_its_busy_time(self):
        self.assertEqual((self.rows["a"]["meanMs"], self.rows["a"]["alone"]["meanMs"]), (2.0, 1.5))

    def test_the_shared_time_of_a_frame_is_its_busy_time_minus_its_time_alone(self):
        self.assertEqual(self.rows["b"]["shared"]["medianMs"], 0.5)
        self.assertEqual(self.rows["b"]["shared"]["p95Ms"], 1.0)

    def test_the_rows_follow_the_frame_graph_with_the_encoders_before_the_first_pass_first(self):
        self.assertEqual([entry["pass"] for entry in self.report["passes"]], [gpu_trace.BEFORE, "a", "b"])

    def test_a_pass_with_no_gpu_work_in_any_frame_is_idle(self):
        self.assertEqual(self.report["idle"], ["c"])

    def test_the_frame_span_leaves_out_the_encoders_before_the_first_pass(self):
        self.assertEqual(self.report["frame"]["meanMs"], 3.5)

    def test_the_window_gives_each_frame_from_the_end_of_one_pass_to_the_next_start_of_the_other(self):
        self.assertEqual(self.report["window"]["medianMs"], 1.0)
        self.assertEqual(self.report["window"]["frames"], 1)

    def test_the_window_counts_the_frames_in_which_it_does_not_exist(self):
        self.assertEqual(self.report["window"]["missing"], 1)

    def test_without_two_passes_the_summary_has_no_window(self):
        self.assertNotIn("window", gpu_trace.summarize(_two_frames(), ["a", "b", "c"], {}))


class Table(unittest.TestCase):
    """table: the text of passes.md for one report."""

    def setUp(self):
        report = {"device": "Test GPU", "width": 64, "height": 32, "commandBuffers": {"frames": 2, "skipped": 0}}
        report |= gpu_trace.summarize(_two_frames(), ["a", "b", "c"], {}, ("a", "b"))
        self.lines = gpu_trace.table(report, has_counters=False).splitlines()

    def row(self, name):
        (found,) = [line for line in self.lines if line.startswith(f"| {name} |")]
        return [cell.strip() for cell in found.strip("|").split("|")]

    def test_the_header_names_the_quantity_and_the_unit_of_every_time_column(self):
        header = self.row("Pass")
        self.assertEqual(
            header[1:8],
            ["Frames", "Busy mean (ms)", "Busy median (ms)", "Busy p95 (ms)", "Alone mean (ms)", "Alone median (ms)",
             "Alone p95 (ms)"],
        )

    def test_a_pass_row_gives_the_busy_time_and_then_the_time_alone(self):
        self.assertEqual(self.row("a")[1:8], ["2", "2.000", "2.000", "2.000", "1.500", "1.500", "2.000"])

    def test_the_window_row_names_its_two_passes_and_gives_the_window_in_the_busy_columns(self):
        self.assertEqual(self.row("a end to b start")[1:8], ["1", "1.000", "1.000", "1.000", "", "", ""])

    def test_a_line_below_the_table_counts_the_frames_without_a_window(self):
        self.assertTrue(any(line.startswith("1 frames have no window from a to b") for line in self.lines))

    def test_a_table_without_counters_says_so(self):
        self.assertIn("The trace holds no GPU counters", self.lines[-1])


if __name__ == "__main__":
    unittest.main()
