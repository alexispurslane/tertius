#!/usr/bin/env python3
"""Tool code. The tests of the pure functions of tools/gpu-trace.py, on synthetic GPU intervals in nanoseconds.

    nix develop --command python3 -I tools/test_gpu_trace.py

The standard library only. The file loads gpu-trace.py by its path, because the hyphen in its name stops an import.
"""

import importlib.util
import random
import statistics
import subprocess
import sys
import tempfile
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


def _first_starts(intervals):
    """The first GPU start of each command buffer of intervals: the frames of the trace as gpu_intervals gives them
    when the trace lost no encoder."""
    first = {}
    for (buffer, _), spans in intervals.items():
        first[buffer] = min([first.get(buffer, spans[0][0]), *(start for start, _ in spans)])
    return first


class Summarize(unittest.TestCase):
    """summarize: the statistics of each pass over the frames, in milliseconds, in the order of the frame graph."""

    def setUp(self):
        self.report = gpu_trace.summarize(_two_frames(), _first_starts(_two_frames()), ["a", "b", "c"], {}, ("a", "b"))
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
        intervals = _two_frames()
        self.assertNotIn("window", gpu_trace.summarize(intervals, _first_starts(intervals), ["a", "b", "c"], {}))


class Table(unittest.TestCase):
    """table: the text of passes.md for one report."""

    def setUp(self):
        report = {"device": "Test GPU", "width": 64, "height": 32, "commandBuffers": {"frames": 2, "skipped": 0}}
        report |= gpu_trace.summarize(_two_frames(), _first_starts(_two_frames()), ["a", "b", "c"], {}, ("a", "b"))
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
        intervals = _frames([True, False] * 5 + [True])
        self.result = gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x", "b"], "x", between=("a", "b"))

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
            {key: self.result[key] for key in ("pass", "control", "resamples", "blockPairs", "seed", "minErrorPairs")},
            {"pass": "x", "control": False, "resamples": gpu_trace.RESAMPLES, "blockPairs": gpu_trace.BLOCK_PAIRS,
             "seed": gpu_trace.SEED, "minErrorPairs": gpu_trace.MIN_ERROR_PAIRS},
        )

    def test_a_slow_drift_over_the_frames_cancels_in_the_pairs(self):
        intervals = _frames([True, False] * 5 + [True], drift_ns=7_000)
        drifting = gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x", "b"], "x")
        self.assertAlmostEqual(_paired_row(drifting, "a")["busy"]["medianMs"], 0.0, places=9)

    def test_the_paired_median_is_the_median_of_the_differences_not_the_difference_of_the_medians(self):
        # a's busy time in ms per frame: with, without, with, without, with, without, with.
        busy = [1.0, 1.0, 3.0, 1.0, 3.0, 2.0, 3.0]
        intervals = {}
        for i, length in enumerate(busy):
            intervals[(f"f{i}", "a")] = [(i * 20 * MS, i * 20 * MS + int(length * MS))]
            if i % 2 == 0:
                intervals[(f"f{i}", "x")] = [(i * 20 * MS + 10 * MS, i * 20 * MS + 11 * MS)]
        result = gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x"], "x")
        # Differences: (1 + 3) / 2 - 1 = 1, (3 + 3) / 2 - 1 = 2, (3 + 3) / 2 - 2 = 1, so the median is 1. The medians
        # of the two kinds of frames are 3 and 1, whose difference is 2.
        self.assertEqual(_paired_row(result, "a")["busy"]["medianMs"], 1.0)


class LeftOutFrames(unittest.TestCase):
    """paired: a frame without the pass whose two neighbors do not both have it is left out of every pair, and the
    result names it by its index in the order of the frames' first GPU start, counted from 0."""

    def test_frames_without_the_pass_at_the_two_ends_of_the_trace_are_left_out(self):
        intervals = _frames([False, True, False, True, False])
        result = gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x", "b"], "x")
        self.assertEqual((result["leftOut"], result["pairs"]), ([0, 4], 1))

    def test_two_neighboring_frames_without_the_pass_are_both_left_out(self):
        intervals = _frames([True, False, True, False, False, True, False, True])
        result = gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x", "b"], "x")
        self.assertEqual((result["leftOut"], result["pairs"]), ([3, 4], 2))

    def test_a_frame_whose_window_does_not_exist_leaves_its_pair_out_of_the_window_only(self):
        intervals = _frames([True, False, True, False, True])
        del intervals[("f2", "b")]
        result = gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x", "b"], "x", between=("a", "b"))
        self.assertEqual((result["window"]["leftOut"], result["window"]["pairs"], result["pairs"]), ([1, 3], 0, 2))

    def test_a_trace_in_which_no_frame_drops_the_pass_has_no_pairs_and_no_medians(self):
        result = gpu_trace.paired(_frames([True] * 4), _first_starts(_frames([True] * 4)), ["a", "x", "b"], "x")
        self.assertEqual((result["pairs"], result["passes"]), (0, []))


class ControlPairs(unittest.TestCase):
    """paired with control: each odd frame against the mean of its two even neighbors, on a trace whose frames all run
    the pass, so the medians give the noise floor of the method."""

    def test_the_control_on_frames_that_all_run_the_pass_gives_a_median_of_zero_in_every_pass(self):
        intervals = _frames([True] * 9, drift_ns=7_000)
        result = gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x", "b"], "x", control=True)
        medians = [entry[kind]["medianMs"] for entry in result["passes"] for kind in ("busy", "alone")]
        self.assertEqual(len(medians), 6)
        self.assertLess(max(abs(median) for median in [*medians, result["frame"]["medianMs"]]), 1e-9)

    def test_the_control_pairs_each_odd_frame_that_has_two_neighbors(self):
        intervals = _frames([True] * 8)
        result = gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x", "b"], "x", control=True)
        self.assertEqual((result["pairs"], result["leftOut"]), (3, [7]))

    def test_the_control_leaves_out_a_frame_without_the_pass_and_the_odd_frames_beside_it(self):
        intervals = _frames([True, True, False, True, True, True, True])
        result = gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x", "b"], "x", control=True)
        self.assertEqual((result["pairs"], result["leftOut"]), (1, [1, 2, 3]))

    def test_the_control_says_that_it_is_the_control(self):
        intervals = _frames([True] * 3)
        self.assertTrue(gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x", "b"], "x", control=True)["control"])


def _starts(count):
    """The first GPU start of each of count frames of _frames, keyed by frame name, as gpu_intervals gives them."""
    return {f"f{i}": i * 20 * MS for i in range(count)}


def _lose(intervals, *frames):
    """intervals without every key of the named frames: frames whose encoders the trace lost."""
    return {key: spans for key, spans in intervals.items() if key[0] not in frames}


class FramesThatTheReportCannotRead(unittest.TestCase):
    """per_frame, summarize and paired with the starts of gpu_intervals: a frame of the trace whose passes the report
    cannot name keeps its place in the order of the frames and gives no values."""

    def odd_frames_longer(self, count):
        """count frames of _frames, all with x, in which a lasts 1 ms longer in each odd frame."""
        intervals = _frames([True] * count)
        for i in range(1, count, 2):
            ((start, end),) = intervals[(f"f{i}", "a")]
            intervals[(f"f{i}", "a")] = [(start, end + MS)]
        return intervals

    def test_the_control_pairs_the_odd_frames_of_the_trace_after_a_frame_that_the_report_cannot_read(self):
        intervals = _lose(self.odd_frames_longer(9), "f3")
        result = gpu_trace.paired(intervals, _starts(9), ["a", "x", "b"], "x", control=True)
        # The pairs around frames 1, 5 and 7 each give the even neighbors minus the longer odd frame: -1 ms.
        self.assertEqual((result["pairs"], _paired_row(result, "a")["busy"]["medianMs"]), (3, -1.0))

    def test_the_frames_after_a_frame_that_the_report_cannot_read_keep_their_index(self):
        intervals = _lose(_frames([True, False, True, False, True, False, True, False, True]), "f4")
        result = gpu_trace.paired(intervals, _starts(9), ["a", "x", "b"], "x")
        self.assertEqual((result["frames"], result["pairs"], result["parity"], result["leftOut"]), (9, 2, "odd", [3, 5]))

    def test_a_frame_that_the_report_cannot_read_is_not_a_frame_without_the_pass(self):
        result = gpu_trace.paired(_lose(_frames([True] * 3), "f1"), _starts(3), ["a", "x", "b"], "x")
        self.assertEqual((result["pairs"], result["leftOut"]), (0, []))

    def test_the_summary_names_the_frames_that_the_report_cannot_read_by_their_index(self):
        found = gpu_trace.summarize(_lose(_frames([True] * 5), "f2"), _starts(5), ["a", "x", "b"], {}, ("a", "b"))
        self.assertEqual(found["unread"], [2])

    def test_a_frame_that_the_report_cannot_read_counts_in_no_statistic(self):
        found = gpu_trace.summarize(_lose(_frames([True] * 5), "f2"), _starts(5), ["a", "x", "b"], {}, ("a", "b"))
        self.assertEqual((found["frame"]["frames"], found["window"]["frames"], found["window"]["missing"]), (4, 4, 0))

    def test_a_trace_in_which_every_frame_has_an_interval_of_a_pass_has_no_frame_that_the_report_cannot_read(self):
        intervals = _frames([True] * 3)
        self.assertEqual(gpu_trace.summarize(intervals, _first_starts(intervals), ["a", "x", "b"], {})["unread"], [])

    def test_a_line_below_the_table_names_the_frames_that_the_report_cannot_read(self):
        intervals = _lose(_frames([True] * 5), "f2")
        report = {"device": "Test GPU", "width": 64, "height": 32, "commandBuffers": {"frames": 4, "skipped": 0}}
        report |= gpu_trace.summarize(intervals, _starts(5), ["a", "x", "b"], {})
        lines = gpu_trace.table(report, has_counters=False).splitlines()
        self.assertIn(
            "The report cannot read the passes of frames 2: the trace lost some of their encoders, or they hold another"
            " number of timestamp encoders. Each keeps its place in the order of the frames and gives no values"
            " (tools/README.md, \"GPU trace\").",
            lines,
        )


class GpuIntervals(unittest.TestCase):
    """gpu_intervals: reads the exported table of GPU intervals into the intervals of each pass and the first GPU start
    of each frame."""

    def intervals(self, rows):
        """gpu_intervals on a table of rows (start ns, duration ns, process, command buffer, encoder, label)."""
        columns = ["start", "duration", "process", "cmdbuffer-id", "encoder-id", "event-label"]
        body = "".join(
            "<row>" + "".join(f'<c fmt="{fmt}">{text}</c>' for fmt, text in cells) + "</row>"
            for cells in (
                [("", start), ("", duration), (process, ""), (buffer, ""), (encoder, ""), (label, "")]
                for start, duration, process, buffer, encoder, label in rows
            )
        )
        schema = "".join(f"<col><mnemonic>{name}</mnemonic></col>" for name in columns)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "metal-gpu-intervals.xml"
            path.write_text(f"<trace-query-result><node><schema>{schema}</schema>{body}</node></trace-query-result>")
            return gpu_trace.gpu_intervals(path, "Demo", {"e1": ("b1", "a"), "e2": ("b1", "a")})

    def test_a_command_buffer_with_a_timestamp_encoder_is_a_frame_from_its_first_gpu_start(self):
        stamp = f"Queue 0-0:{gpu_trace.STAMP}   (Demo (1)) 0x1"
        _, starts = self.intervals([
            (50, 5, "Demo (1)", "b1", "e1", "Queue 0-0:vkCmdDispatch ComputeEncoder"),
            (40, 5, "Demo (1)", "b1", "s1", stamp),
            (90, 5, "Demo (1)", "b2", "s2", stamp),
            (95, 5, "Demo (1)", "b2", "e3", "Queue 0-0:vkCmdDispatch ComputeEncoder"),
            (70, 5, "Demo (1)", "b3", "e4", "Queue 0-0:vkCmdCopyBuffer BlitEncoder"),
            (80, 5, "Other (2)", "b4", "s4", stamp),
        ])
        self.assertEqual(starts, {"b1": 40, "b2": 90})

    def test_a_command_buffer_with_an_encoder_of_a_pass_is_a_frame_even_when_the_trace_lost_its_timestamp_intervals(self):
        _, starts = self.intervals([(50, 5, "Demo (1)", "b1", "e1", "Queue 0-0:vkCmdDispatch ComputeEncoder")])
        self.assertEqual(starts, {"b1": 50})

    def test_the_intervals_hold_only_the_encoders_that_name_a_pass(self):
        found, _ = self.intervals([
            (50, 5, "Demo (1)", "b1", "e1", "Queue 0-0:vkCmdDispatch ComputeEncoder"),
            (60, 5, "Demo (1)", "b1", "e2", "Queue 0-0:vkCmdDispatch ComputeEncoder"),
            (40, 5, "Demo (1)", "b1", "s1", f"Queue 0-0:{gpu_trace.STAMP}   (Demo (1)) 0x1"),
        ])
        self.assertEqual(found, {("b1", "a"): [(50, 55), (60, 65)]})


class CommandBuffers(unittest.TestCase):
    """passes_by_encoder and command_buffers: the first line of passes.md counts the frames of the frame order and the
    other command buffers of the encoder list."""

    def listed(self, rows):
        """passes_by_encoder on an encoder list of rows (process, command buffer, encoder, label, index) for the pass a."""
        columns = ["process", "cmdbuffer-id", "encoder-id", "encoder-label", "encoder-label-indexed"]
        body = "".join(
            "<row>" + "".join(f'<c fmt="{text}"></c>' for text in cells) + "</row>"
            for cells in ([process, buffer, encoder, label, f"[{index}] {label}"] for process, buffer, encoder, label, index in rows)
        )
        schema = "".join(f"<col><mnemonic>{name}</mnemonic></col>" for name in columns)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "metal-application-encoders-list.xml"
            path.write_text(f"<trace-query-result><node><schema>{schema}</schema>{body}</node></trace-query-result>")
            return gpu_trace.passes_by_encoder(path, "Demo", ["a"])

    def test_the_encoder_list_names_the_passes_of_a_command_buffer_with_one_more_timestamp_than_passes(self):
        named, _ = self.listed([
            ("Demo (1)", "b1", "s1", gpu_trace.STAMP, 0),
            ("Demo (1)", "b1", "e1", "vkCmdDispatch ComputeEncoder", 1),
            ("Demo (1)", "b1", "s2", gpu_trace.STAMP, 2),
            ("Demo (1)", "b2", "e2", "vkCmdCopyBuffer BlitEncoder", 0),
        ])
        self.assertEqual(named, {"e1": ("b1", "a")})

    def test_the_encoder_list_gives_every_command_buffer_that_it_lists(self):
        _, listed = self.listed([
            ("Demo (1)", "b1", "s1", gpu_trace.STAMP, 0),
            ("Demo (1)", "b1", "e1", "vkCmdDispatch ComputeEncoder", 1),
            ("Demo (1)", "b1", "s2", gpu_trace.STAMP, 2),
            ("Demo (1)", "b2", "e2", "vkCmdCopyBuffer BlitEncoder", 0),
            ("Other (2)", "b3", "e3", "vkCmdCopyBuffer BlitEncoder", 0),
        ])
        self.assertEqual(listed, {"b1", "b2"})

    def test_every_frame_of_the_order_counts_as_a_frame_also_one_that_the_encoder_list_lost(self):
        # Two uploads make the encoder list longer than the frame order, so the count must come from the order.
        found = gpu_trace.command_buffers({"b1", "b2", "upload", "upload-2"}, {"b1": 10, "b2": 20, "lost": 30})
        self.assertEqual(found["frames"], 3)

    def test_the_other_command_buffers_are_those_of_the_encoder_list_that_are_not_frames(self):
        # b2 is a frame whose encoder list holds another number of timestamps: it is a frame, not another command buffer.
        found = gpu_trace.command_buffers({"b1", "b2", "upload"}, {"b1": 10, "b2": 20, "lost": 30})
        self.assertEqual(found["skipped"], 1)


class BootstrapError(unittest.TestCase):
    """bootstrap_error: the moving-block bootstrap standard error of the median of a series, in the unit of the
    values."""

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

    def test_each_resample_joins_blocks_of_consecutive_values(self):
        # With B = BLOCK_PAIRS, at least 4, a resample of the B + 1 values 1 to B + 1 is the block from 1 or from 2,
        # each with the chance 1/2, then the first value of a second such block, 1 or 2. Its median is B / 2 after the
        # block from 1 and B / 2 + 1 after the block from 2, whichever that value is, so the standard deviation of the
        # medians is 0.5. Single values drawn alone give about 1.0.
        values = [float(value) for value in range(1, gpu_trace.BLOCK_PAIRS + 2)]
        self.assertAlmostEqual(gpu_trace.bootstrap_error(values, 20_000, 5), 0.5, delta=0.01)

    def test_the_paired_errors_use_the_differences_in_trace_order(self):
        # x's busy time in frame i grows by ((7 i) mod 11) * 10 µs, so the differences are in no order of their values,
        # and their sorted order gives another error.
        count = gpu_trace.MIN_ERROR_PAIRS + 6
        intervals = _frames([True, False] * count + [True])
        for i in range(0, 2 * count + 1, 2):
            ((start, end),) = intervals[(f"f{i}", "x")]
            intervals[(f"f{i}", "x")] = [(start, end + ((7 * i) % 11) * 10_000)]
        busy = [sum(end - start for start, end in intervals.get((f"f{i}", "x"), [])) / MS for i in range(2 * count + 1)]
        differences = [(busy[i - 1] + busy[i + 1]) / 2 - busy[i] for i in range(1, 2 * count, 2)]
        found = _paired_row(gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x", "b"], "x"), "x")["busy"]["standardErrorMs"]
        self.assertEqual(
            (found, found == gpu_trace.bootstrap_error(sorted(differences), gpu_trace.RESAMPLES, gpu_trace.SEED)),
            (gpu_trace.bootstrap_error(differences, gpu_trace.RESAMPLES, gpu_trace.SEED), False),
        )

    def test_a_pair_left_out_of_a_quantity_joins_the_pairs_on_its_two_sides_into_one_series(self):
        # Values in no order, so that the order of the differences matters, with one more pair than MIN_ERROR_PAIRS.
        count = gpu_trace.MIN_ERROR_PAIRS + 1
        values = [((7 * i) % 11) / 4 for i in range(2 * count + 1)]
        values[7] = None
        triples = [(i - 1, i, i + 1) for i in range(1, 2 * count, 2)]
        # Frame 7 has no value, so its pair is left out, and the series of differences runs from frame 5 to frame 9.
        kept = [(values[b] + values[a]) / 2 - values[f] for b, f, a in triples if f != 7]
        self.assertEqual(
            gpu_trace.paired_median(values, triples)["standardErrorMs"],
            gpu_trace.bootstrap_error(kept, gpu_trace.RESAMPLES, gpu_trace.SEED),
        )


def _independent_bootstrap_error(values, resamples, seed):
    """The bootstrap of ter-e91p, which drew single values as if they were independent, for the comparison below."""
    draws = random.Random(seed)
    return statistics.stdev(statistics.median(draws.choices(values, k=len(values))) for _ in range(resamples))


class _AlternatingFrames:
    """Synthetic alternating traces of 201 frames, 100 pairs, for the classes below. The pass costs 0.16 ms in each frame
    with it, the even frames, and each frame has independent Gaussian noise with a standard deviation of 0.01 ms. When
    noise_without is false, the frames without the pass have no noise: the time of the pass itself, which is 0 ms there.

    The real standard error of the paired median is the standard deviation of the paired medians of real_traces
    independent traces from the seed 7, which the class computes. The two bootstraps run on 400 other traces from the
    seed 11, with 250 resamples each, and the tests compare the mean of their errors with the real error. The seeds make
    the result the same in every run.
    """

    noise_without = True
    real_traces = 20_000
    traces = 400

    @classmethod
    def differences(cls, draws):
        frames = [
            (0.16 + draws.gauss(0, 0.01)) if i % 2 == 0 else (draws.gauss(0, 0.01) if cls.noise_without else 0.0)
            for i in range(201)
        ]
        triples, _ = gpu_trace.pairs([i % 2 == 0 for i in range(201)])
        return [(frames[before] + frames[after]) / 2 - frames[frame] for before, frame, after in triples]

    @classmethod
    def setUpClass(cls):
        draws = random.Random(7)
        cls.real = statistics.stdev(statistics.median(cls.differences(draws)) for _ in range(cls.real_traces))
        draws = random.Random(11)
        cls.series = [cls.differences(draws) for _ in range(cls.traces)]

    def ratio(self, error):
        return statistics.mean(error(values, 250, seed) for seed, values in enumerate(self.series)) / self.real


class BlockBootstrapOnAlternatingFrames(_AlternatingFrames, unittest.TestCase):
    """bootstrap_error on the paired differences of _AlternatingFrames with noise in every frame, as in the rows of the
    other passes.

    Neighboring pairs share a frame, so their differences correlate by 1/6. A bootstrap error of the median varies by
    about 22 % from trace to trace, so the mean over 400 traces varies by about 1.1 %, and the real error by about
    0.5 %. Here the mean ratio is 0.979 for blocks of 6 pairs and 0.927 for single differences. The bound 0.955 lies
    about 2 standard deviations from both.
    """

    def test_the_block_bootstrap_error_lies_near_the_real_standard_error_of_the_paired_median(self):
        self.assertAlmostEqual(self.ratio(gpu_trace.bootstrap_error), 1.0, delta=0.045)

    def test_the_bootstrap_of_single_differences_is_too_small_in_the_same_setup(self):
        self.assertLess(self.ratio(_independent_bootstrap_error), 0.955)


class BlockBootstrapOnTheRowOfThePass(_AlternatingFrames, unittest.TestCase):
    """bootstrap_error on the paired differences of _AlternatingFrames without noise in the frames without the pass, as
    in the row of the pass itself.

    A difference is then the mean of its two neighbors, so neighboring differences correlate by 1/2. The mean ratio over
    400 traces varies by about 1.3 %, and the real error from 10,000 traces by about 0.7 %. Here the mean ratio is 0.961
    for blocks of 6 pairs and 0.793 for single differences. The bound 1 - 0.08 lies about 3 standard deviations below
    the first, and the bound 0.88 more than 5 from both.
    """

    noise_without = False
    real_traces = 10_000
    traces = 400

    def test_the_block_bootstrap_error_lies_near_the_real_standard_error_of_the_paired_median(self):
        self.assertAlmostEqual(self.ratio(gpu_trace.bootstrap_error), 1.0, delta=0.08)

    def test_the_bootstrap_of_single_differences_is_too_small_in_the_same_setup(self):
        self.assertLess(self.ratio(_independent_bootstrap_error), 0.88)


class PairedTable(unittest.TestCase):
    """table: the paired table of passes.md, below the table of the passes."""

    def lines(self, marks, control=False, missing=()):
        intervals = _frames(marks)
        for key in missing:
            del intervals[key]
        report = {"device": "Test GPU", "width": 64, "height": 32, "commandBuffers": {"frames": len(marks), "skipped": 0}}
        report |= gpu_trace.summarize(intervals, _first_starts(intervals), ["a", "x", "b"], {}, ("a", "b"))
        report["paired"] = gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x", "b"], "x", control=control, between=("a", "b"))
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
        lines = self.lines([True, False] * gpu_trace.MIN_ERROR_PAIRS + [True])
        self.assertEqual(
            self.row(lines, "x"), ["x", str(gpu_trace.MIN_ERROR_PAIRS), "+2.5000", "0.0000", "+2.0000", "0.0000"]
        )

    def test_a_row_of_fewer_pairs_than_the_minimum_gives_its_medians_without_errors(self):
        self.assertEqual(self.row(self.lines([True, False, True]), "x"), ["x", "1", "+2.5000", "", "+2.0000", ""])

    def test_the_paired_window_row_names_its_two_passes(self):
        self.assertEqual(
            self.row(self.lines([True, False, True]), "a end to b start"), ["a end to b start", "1", "+2.0000", "", "", ""]
        )

    def test_the_text_states_the_resamples_the_block_length_and_the_seed(self):
        text = "\n".join(self.lines([True, False, True]))
        self.assertIn(
            f"moving-block bootstrap of {gpu_trace.RESAMPLES} resamples, with blocks of {gpu_trace.BLOCK_PAIRS}"
            f" consecutive pairs and the seed {gpu_trace.SEED}",
            text,
        )

    def test_the_text_states_the_fewest_pairs_that_give_a_standard_error(self):
        text = "\n".join(self.lines([True, False, True]))
        self.assertIn(f"A row of fewer than {gpu_trace.MIN_ERROR_PAIRS} pairs gives no standard error", text)

    def test_a_line_names_the_frames_that_were_left_out(self):
        lines = self.lines([False, True, False, True, False])
        self.assertIn("Left out, with no neighbor pair: frames 0, 4.", lines)

    def test_a_line_names_the_pairs_left_out_of_the_window_row(self):
        lines = self.lines([True, False, True, False, True], missing=[("f2", "b")])
        self.assertIn(
            "Left out of the window row, because a frame of the pair has no window: the pairs around frames 1, 3.", lines
        )

    def test_the_control_table_says_that_it_is_the_control(self):
        self.assertTrue(any(line.startswith("Control:") for line in self.lines([True] * 5, control=True)))



class PairedMedian(unittest.TestCase):
    """pairs and paired_median: the pairs are (before, frame, after) index triples, and a pair whose three frames do not
    all have a value is left out of that quantity alone."""

    def test_pairs_gives_each_paired_frame_with_its_two_neighbors(self):
        self.assertEqual(gpu_trace.pairs([True, False, True, False, True]), ([(0, 1, 2), (2, 3, 4)], []))

    def test_the_first_frame_makes_no_pair_with_the_last_frame_as_its_neighbor_before(self):
        self.assertEqual(gpu_trace.pairs([False, True, False, True]), ([(1, 2, 3)], [0]))

    def test_a_frame_marked_none_makes_no_pair_even_between_two_frames_on_the_with_side(self):
        self.assertEqual(gpu_trace.pairs([True, None, True]), ([], [1]))

    def test_a_pair_with_a_frame_without_a_value_is_left_out_and_named_by_its_middle_frame(self):
        found = gpu_trace.paired_median([2.0, 1.0, None, 1.0, 2.0, 0.5, 2.0], [(0, 1, 2), (2, 3, 4), (4, 5, 6)])
        self.assertEqual((found["pairs"], found["leftOut"], found["medianMs"]), (1, [1, 3], 1.5))

    def test_a_quantity_with_a_value_in_every_frame_leaves_out_no_pair(self):
        self.assertEqual(gpu_trace.paired_median([2.0, 1.0, 2.0], [(0, 1, 2)])["leftOut"], [])

    def test_a_quantity_with_fewer_pairs_than_the_minimum_has_a_median_but_no_standard_error(self):
        count = gpu_trace.MIN_ERROR_PAIRS - 1
        found = gpu_trace.paired_median([((7 * i) % 11) / 4 for i in range(2 * count + 1)], gpu_trace.pairs(
            [i % 2 == 0 for i in range(2 * count + 1)])[0])
        self.assertEqual((found["pairs"], "medianMs" in found, "standardErrorMs" in found), (count, True, False))

    def test_a_quantity_with_the_minimum_number_of_pairs_has_a_standard_error(self):
        count = gpu_trace.MIN_ERROR_PAIRS
        found = gpu_trace.paired_median([((7 * i) % 11) / 4 for i in range(2 * count + 1)], gpu_trace.pairs(
            [i % 2 == 0 for i in range(2 * count + 1)])[0])
        self.assertGreater(found["standardErrorMs"], 0.0)


class OneRowRule(unittest.TestCase):
    """summarize and paired give a row to the same passes: each pass with an interval in some frame."""

    def test_a_pass_whose_intervals_all_have_zero_length_has_a_row_in_both_tables(self):
        intervals = _frames([True, False, True])
        for i in range(3):
            intervals[(f"f{i}", "z")] = [(i * 20 * MS + 10 * MS, i * 20 * MS + 10 * MS)]
        summarized = [entry["pass"] for entry in gpu_trace.summarize(intervals, _first_starts(intervals), ["a", "x", "b", "z"], {})["passes"]]
        found = [entry["pass"] for entry in gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x", "b", "z"], "x")["passes"]]
        self.assertEqual((summarized, found), (["a", "x", "b", "z"], ["a", "x", "b", "z"]))


class PairedText(unittest.TestCase):
    """table: the lines around the paired table name the frames left out, the parity of the frames without the pass and
    how far the standard errors can be trusted."""

    def lines(self, marks, control=False):
        intervals = _frames(marks)
        report = {"device": "Test GPU", "width": 64, "height": 32, "commandBuffers": {"frames": len(marks), "skipped": 0}}
        report |= gpu_trace.summarize(intervals, _first_starts(intervals), ["a", "x", "b"], {})
        report["paired"] = gpu_trace.paired(intervals, _first_starts(intervals), ["a", "x", "b"], "x", control=control)
        return gpu_trace.table(report, has_counters=False).splitlines()

    def test_a_report_with_no_pair_still_names_the_frames_left_out(self):
        lines = self.lines([True, True], control=True)
        self.assertIn(
            "Left out, an odd frame without two even neighbors that run x, or a frame without GPU work of x: frames 1.", lines
        )

    def test_a_long_list_of_left_out_frames_gives_its_count_and_its_first_frames(self):
        lines = self.lines([False] * 25)
        self.assertIn(
            "Left out, with no neighbor pair: 25 frames, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 and 15 more (passes.json lists all).",
            lines,
        )

    def test_the_text_says_whether_the_frames_without_the_pass_are_the_odd_or_the_even_frames(self):
        lines = self.lines([False, True, False, True, False])
        self.assertTrue(any("The frames without x are the even frames." in line for line in lines))

    def test_the_text_says_that_the_blocks_hold_the_shared_frames_but_not_a_slower_drift_or_a_cycle(self):
        text = " ".join(self.lines([True, False, True, False, True]))
        self.assertIn(
            "Neighboring pairs share a frame, and the blocks keep that correlation. A drift that correlates pairs farther"
            " apart than a block still makes the errors too small, and a cycle of a pass's work over the frames can make"
            " them too small or too large",
            text,
        )


class ReportOptions(unittest.TestCase):
    """report: the command line of the paired mode, run as a process, before any trace is read."""

    def run_report(self, folder, *options):
        timings = Path(folder) / "gpu-timings.json"
        timings.write_text('{"passes": [{"pass": "a"}, {"pass": "x"}]}')
        script = Path(__file__).with_name("gpu-trace.py")
        command = [sys.executable, "-I", str(script), "report", str(Path(folder) / "none.trace"), str(timings), folder]
        return subprocess.run([*command, *options], capture_output=True, text=True)

    def test_paired_and_control_exclude_each_other(self):
        with tempfile.TemporaryDirectory() as folder:
            self.assertEqual(self.run_report(folder, "--paired", "x", "--control", "x").returncode, 2)

    def test_an_unknown_pass_is_named_with_the_option_that_gave_it(self):
        with tempfile.TemporaryDirectory() as folder:
            found = self.run_report(folder, "--window", "a", "y", "--control", "w")
        self.assertEqual(
            (found.returncode, found.stderr.splitlines()[:2]),
            (1, ["--window y: y is not a pass of the run.", "--control w: w is not a pass of the run."]),
        )


if __name__ == "__main__":
    unittest.main()
