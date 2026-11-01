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


def _frames(marks, drift_ns=0):
    """Synthetic frames of the passes a, x and b, 20 ms apart, keyed (frame name, pass) in a shuffled order.

    In a frame whose mark is true, x runs from 0.5 to 3 ms, so it overlaps a (0 to 1 ms) by 0.5 ms, and b runs from 3 to
    4 ms. In a frame whose mark is false, x has no GPU work and b runs from 1 to 2 ms. So x costs a busy time of 2.5 ms,
    a time alone of 2 ms, a window from a to b of 2 ms and a frame span of 2 ms, and takes 0.5 ms of a's time alone.
    Frame i lasts drift_ns * i longer in each of its passes' ends, a slow drift such as a GPU clock change.
    """
    intervals = {}
    for i, marked in enumerate(marks):
        base = i * 20 * MS
        late = drift_ns * i
        intervals[(f"f{i}", "a")] = [(base, base + MS + late)]
        if marked:
            intervals[(f"f{i}", "x")] = [(base + MS // 2, base + 3 * MS + late)]
            intervals[(f"f{i}", "b")] = [(base + 3 * MS + late, base + 4 * MS + late)]
        else:
            intervals[(f"f{i}", "b")] = [(base + MS + late, base + 2 * MS + late)]
    # The frames' keys in an order that is not the order of their time, as the dicts of a trace can have them.
    return dict(sorted(intervals.items(), key=lambda item: (-int(item[0][0][1:]), item[0][1])))


def _paired_row(result, name):
    (found,) = [entry for entry in result["passes"] if entry["pass"] == name]
    return found


class PairedFrames(unittest.TestCase):
    """paired: each frame without the named pass against the mean of its two neighbors with it, per pass, as the median
    of the differences in ms, with minus without."""

    def setUp(self):
        self.result = gpu_trace.paired(_frames([True, False] * 5 + [True]), ["a", "x", "b"], "x", between=("a", "b"))

    def test_the_dropped_pass_costs_its_whole_busy_time(self):
        self.assertEqual(_paired_row(self.result, "x")["busy"]["medianMs"], 2.5)

    def test_the_dropped_pass_costs_its_time_alone_in_the_time_alone(self):
        self.assertEqual(_paired_row(self.result, "x")["alone"]["medianMs"], 2.0)

    def test_a_pass_that_the_dropped_pass_overlaps_keeps_its_busy_time_and_loses_time_alone(self):
        row = _paired_row(self.result, "a")
        self.assertEqual((row["busy"]["medianMs"], row["alone"]["medianMs"]), (0.0, -0.5))

    def test_the_frame_span_grows_by_the_dropped_pass(self):
        self.assertEqual(self.result["frame"]["medianMs"], 2.0)

    def test_the_window_between_two_passes_grows_by_the_dropped_pass_between_them(self):
        self.assertEqual(self.result["window"]["medianMs"], 2.0)

    def test_each_frame_without_the_pass_between_two_with_it_makes_one_pair(self):
        self.assertEqual(self.result["pairs"], 5)

    def test_every_frame_without_the_pass_has_a_neighbor_pair_so_none_is_left_out(self):
        self.assertEqual(self.result["leftOut"], [])

    def test_the_result_names_the_pass_the_mode_and_the_bootstrap(self):
        self.assertEqual(
            {key: self.result[key] for key in ("pass", "control", "resamples", "seed")},
            {"pass": "x", "control": False, "resamples": gpu_trace.RESAMPLES, "seed": gpu_trace.SEED},
        )

    def test_a_slow_drift_over_the_frames_cancels_in_the_pairs(self):
        drifting = gpu_trace.paired(_frames([True, False] * 5 + [True], drift_ns=7_000), ["a", "x", "b"], "x")
        self.assertAlmostEqual(_paired_row(drifting, "a")["busy"]["medianMs"], 0.0, places=9)

    def test_the_paired_median_is_the_median_of_the_differences_not_the_difference_of_the_medians(self):
        # a's busy time in ms per frame: with, without, with, without, with, without, with.
        busy = [1.0, 1.0, 3.0, 1.0, 3.0, 2.0, 3.0]
        intervals = {}
        for i, length in enumerate(busy):
            intervals[(f"f{i}", "a")] = [(i * 20 * MS, i * 20 * MS + int(length * MS))]
            if i % 2 == 0:
                intervals[(f"f{i}", "x")] = [(i * 20 * MS + 10 * MS, i * 20 * MS + 11 * MS)]
        result = gpu_trace.paired(intervals, ["a", "x"], "x")
        # Differences: (1 + 3) / 2 - 1 = 1, (3 + 3) / 2 - 1 = 2, (3 + 3) / 2 - 2 = 1, so the median is 1. The medians
        # of the two kinds of frames are 3 and 1, whose difference is 2.
        self.assertEqual(_paired_row(result, "a")["busy"]["medianMs"], 1.0)


class LeftOutFrames(unittest.TestCase):
    """paired: a frame without the pass whose two neighbors do not both have it is left out of every pair, and the
    result names it by its index in the order of the frames' first GPU start, counted from 0."""

    def test_frames_without_the_pass_at_the_two_ends_of_the_trace_are_left_out(self):
        result = gpu_trace.paired(_frames([False, True, False, True, False]), ["a", "x", "b"], "x")
        self.assertEqual((result["leftOut"], result["pairs"]), ([0, 4], 1))

    def test_two_neighboring_frames_without_the_pass_are_both_left_out(self):
        result = gpu_trace.paired(_frames([True, False, True, False, False, True, False, True]), ["a", "x", "b"], "x")
        self.assertEqual((result["leftOut"], result["pairs"]), ([3, 4], 2))

    def test_a_frame_whose_window_does_not_exist_leaves_its_pair_out_of_the_window_only(self):
        intervals = _frames([True, False, True, False, True])
        del intervals[("f2", "b")]
        result = gpu_trace.paired(intervals, ["a", "x", "b"], "x", between=("a", "b"))
        self.assertEqual((result["window"]["leftOut"], result["window"]["pairs"], result["pairs"]), ([1, 3], 0, 2))

    def test_a_trace_in_which_no_frame_drops_the_pass_has_no_pairs_and_no_medians(self):
        result = gpu_trace.paired(_frames([True] * 4), ["a", "x", "b"], "x")
        self.assertEqual((result["pairs"], result["passes"]), (0, []))


class ControlPairs(unittest.TestCase):
    """paired with control: each odd frame against the mean of its two even neighbors, on a trace whose frames all run
    the pass, so the medians give the noise floor of the method."""

    def test_the_control_on_frames_that_all_run_the_pass_gives_a_median_of_zero_in_every_pass(self):
        result = gpu_trace.paired(_frames([True] * 9, drift_ns=7_000), ["a", "x", "b"], "x", control=True)
        medians = [entry[kind]["medianMs"] for entry in result["passes"] for kind in ("busy", "alone")]
        self.assertEqual(len(medians), 6)
        self.assertLess(max(abs(median) for median in [*medians, result["frame"]["medianMs"]]), 1e-9)

    def test_the_control_pairs_each_odd_frame_that_has_two_neighbors(self):
        result = gpu_trace.paired(_frames([True] * 8), ["a", "x", "b"], "x", control=True)
        self.assertEqual((result["pairs"], result["leftOut"]), (3, [7]))

    def test_the_control_leaves_out_a_frame_without_the_pass_and_the_odd_frames_beside_it(self):
        result = gpu_trace.paired(_frames([True, True, False, True, True, True, True]), ["a", "x", "b"], "x", control=True)
        self.assertEqual((result["pairs"], result["leftOut"]), (1, [1, 2, 3]))

    def test_the_control_says_that_it_is_the_control(self):
        self.assertTrue(gpu_trace.paired(_frames([True] * 3), ["a", "x", "b"], "x", control=True)["control"])


class BootstrapError(unittest.TestCase):
    """bootstrap_error: the bootstrap standard error of a median, in the unit of the values."""

    values = [0.11, 0.13, 0.09, 0.2, 0.15, 0.12, 0.1, 0.17, 0.14]

    def test_the_same_seed_gives_the_same_error(self):
        self.assertEqual(gpu_trace.bootstrap_error(self.values, 500, 3), gpu_trace.bootstrap_error(self.values, 500, 3))

    def test_another_seed_gives_another_error(self):
        self.assertNotEqual(gpu_trace.bootstrap_error(self.values, 500, 3), gpu_trace.bootstrap_error(self.values, 500, 4))

    def test_values_that_are_all_equal_have_no_error(self):
        self.assertEqual(gpu_trace.bootstrap_error([0.25] * 9, 500, 3), 0.0)

    def test_the_error_scales_with_the_values(self):
        doubled = [2 * value for value in self.values]
        self.assertAlmostEqual(
            gpu_trace.bootstrap_error(doubled, 500, 3), 2 * gpu_trace.bootstrap_error(self.values, 500, 3), places=12
        )

    def test_the_error_is_the_spread_of_the_medians_of_the_resamples(self):
        # Resamples of 1, 2 and 3 have the medians 1, 2 or 3 with the chances 7/27, 13/27 and 7/27, whose standard
        # deviation is sqrt(14/27), about 0.72.
        self.assertAlmostEqual(gpu_trace.bootstrap_error([1.0, 2.0, 3.0], 20_000, 5), (14 / 27) ** 0.5, delta=0.01)

    def test_the_paired_errors_use_the_fixed_seed_and_resample_count(self):
        intervals = _frames([True, False] * 5 + [True], drift_ns=7_000)
        busy = [sum(end - start for start, end in intervals.get((f"f{i}", "x"), [])) / MS for i in range(11)]
        differences = [(busy[i - 1] + busy[i + 1]) / 2 - busy[i] for i in range(1, 10, 2)]
        result = gpu_trace.paired(intervals, ["a", "x", "b"], "x")
        self.assertEqual(
            _paired_row(result, "x")["busy"]["standardErrorMs"],
            gpu_trace.bootstrap_error(differences, gpu_trace.RESAMPLES, gpu_trace.SEED),
        )


class PairedTable(unittest.TestCase):
    """table: the paired table of passes.md, below the table of the passes."""

    def lines(self, marks, control=False):
        intervals = _frames(marks)
        report = {"device": "Test GPU", "width": 64, "height": 32, "commandBuffers": {"frames": len(marks), "skipped": 0}}
        report |= gpu_trace.summarize(intervals, ["a", "x", "b"], {}, ("a", "b"))
        report["paired"] = gpu_trace.paired(intervals, ["a", "x", "b"], "x", control=control, between=("a", "b"))
        return gpu_trace.table(report, has_counters=False).splitlines()

    def row(self, lines, name):
        found = [line for line in lines if line.startswith(f"| {name} |")]
        return [cell.strip() for cell in found[-1].strip("|").split("|")]

    def test_the_header_names_the_quantity_and_the_unit_of_every_paired_column(self):
        header = self.row(self.lines([True, False, True]), "Pass")
        self.assertEqual(
            header,
            ["Pass", "Pairs", "Busy paired median (ms)", "Busy standard error (ms)", "Alone paired median (ms)",
             "Alone standard error (ms)"],
        )

    def test_a_paired_row_gives_the_pairs_and_the_signed_medians_and_errors(self):
        self.assertEqual(self.row(self.lines([True, False, True]), "x"), ["x", "1", "+2.5000", "0.0000", "+2.0000", "0.0000"])

    def test_the_paired_window_row_names_its_two_passes(self):
        self.assertEqual(
            self.row(self.lines([True, False, True]), "a end to b start"), ["a end to b start", "1", "+2.0000", "0.0000", "", ""]
        )

    def test_the_text_states_the_resamples_and_the_seed(self):
        text = "\n".join(self.lines([True, False, True]))
        self.assertIn(f"{gpu_trace.RESAMPLES} resamples with the seed {gpu_trace.SEED}", text)

    def test_a_line_names_the_frames_that_were_left_out(self):
        lines = self.lines([False, True, False, True, False])
        self.assertIn("Left out, with no neighbor pair: frames 0, 4.", lines)

    def test_the_control_table_says_that_it_is_the_control(self):
        self.assertTrue(any(line.startswith("Control:") for line in self.lines([True] * 5, control=True)))


if __name__ == "__main__":
    unittest.main()
