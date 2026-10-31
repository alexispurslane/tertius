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


if __name__ == "__main__":
    unittest.main()
