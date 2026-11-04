#!/usr/bin/env python3
"""Tool code. The tests of tools/gpu_clock.py, the record of the GPU clock over a timed span, on synthetic logs of
gpu-clock and of the measurements' marker lines.

    nix develop --command python3 -I tools/test_gpu_clock.py

The standard library only. The file loads gpu_clock.py by its path, because python3 -I leaves the script's folder out
of the import path.
"""

import importlib.util
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("gpu_clock", Path(__file__).with_name("gpu_clock.py"))
gpu_clock = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gpu_clock)


def sample(t: float, active: float, mhz: float) -> str:
    return f"sample t={t:.3f} active={active:.3f} mhz={mhz:.1f} states=0,0,0,0,0,1"


def known(mhz: float) -> "gpu_clock.Known":
    return gpu_clock.Known(mhz=mhz, p10_mhz=mhz, p90_mhz=mhz, samples=10)


class SpanClock(unittest.TestCase):
    """span_clock: the clock over a measurement's timed span, from gpu-clock's samples between the marker lines that the
    measurement prints."""

    def test_only_the_samples_between_the_markers_count(self):
        log = "\n".join([
            sample(0.1, 1.0, 648.0),
            "    Some.Test [OUTPUT] timed-start",
            sample(0.2, 1.0, 1296.0),
            sample(0.3, 0.5, 1200.0),
            "    Some.Test [OUTPUT] timed-end",
            sample(0.4, 1.0, 648.0),
        ])
        self.assertAlmostEqual(gpu_clock.span_clock(log).mhz, (1296.0 + 0.5 * 1200.0) / 1.5)

    def test_the_record_counts_the_samples_of_the_span_and_gives_their_tenth_and_ninetieth_percentiles(self):
        clocks = [648.0, 778.0, 972.0, 1000.0, 1100.0, 1150.0, 1200.0, 1250.0, 1280.0, 1296.0]
        log = "\n".join(["timed-start", *(sample(0.1 * i, 1.0, mhz) for i, mhz in enumerate(clocks)), "timed-end"])
        clock = gpu_clock.span_clock(log)
        self.assertEqual(clock.samples, 10)
        self.assertEqual(clock.p10_mhz, 778.0)
        self.assertEqual(clock.p90_mhz, 1296.0)

    def test_a_sample_in_which_the_gpu_was_off_does_not_count(self):
        log = "\n".join(["timed-start", sample(0.1, 1.0, 1296.0), sample(0.2, 0.0, 0.0), "timed-end"])
        clock = gpu_clock.span_clock(log)
        self.assertEqual(clock.samples, 1)
        self.assertEqual(clock.p10_mhz, 1296.0)

    def test_the_samples_between_two_spans_do_not_count(self):
        log = "\n".join([
            "timed-start", sample(0.1, 1.0, 1296.0), "timed-end",
            sample(0.2, 1.0, 388.8),
            "timed-start", sample(0.3, 1.0, 972.0), "timed-end",
        ])
        clock = gpu_clock.span_clock(log)
        self.assertAlmostEqual(clock.mhz, (1296.0 + 972.0) / 2)
        self.assertEqual(clock.samples, 2)

    def test_the_samples_of_a_span_that_never_closes_do_not_count(self):
        log = "\n".join([
            "timed-start", sample(0.1, 1.0, 1296.0), "timed-end",
            "timed-start", sample(0.2, 1.0, 648.0), sample(0.3, 1.0, 648.0),
        ])
        clock = gpu_clock.span_clock(log)
        self.assertEqual(clock.mhz, 1296.0)
        self.assertEqual(clock.samples, 1)

    def test_a_log_without_markers_has_an_unknown_clock(self):
        clock = gpu_clock.span_clock(sample(0.1, 1.0, 648.0))
        self.assertIsInstance(clock, gpu_clock.Unknown)
        self.assertIn("timed-start", clock.reason)

    def test_a_log_without_samples_of_gpu_clock_has_an_unknown_clock(self):
        clock = gpu_clock.span_clock("timed-start\nfirst frame\ntimed-end")
        self.assertIsInstance(clock, gpu_clock.Unknown)
        self.assertIn("no sample", clock.reason)

    def test_a_span_shorter_than_a_sample_has_an_unknown_clock(self):
        clock = gpu_clock.span_clock("\n".join([sample(0.1, 1.0, 648.0), "timed-start", "timed-end", sample(0.2, 1.0, 648.0)]))
        self.assertIsInstance(clock, gpu_clock.Unknown)
        self.assertIn("timed span", clock.reason)

    def test_a_system_where_gpu_clock_cannot_run_gives_its_reason(self):
        log = "\n".join(["clock unknown: gpu-clock reads IOReport, which only macOS has", "timed-start", "timed-end"])
        clock = gpu_clock.span_clock(log)
        self.assertIsInstance(clock, gpu_clock.Unknown)
        self.assertEqual(clock.reason, "gpu-clock reads IOReport, which only macOS has")


class ClockOfSamples(unittest.TestCase):
    """samples and clock_of: the one parser of gpu-clock's sample lines and the one mean over chosen samples, which the
    timed span and the window summary of tools/gpu-calibration/analyze.py both use."""

    def test_each_sample_line_gives_its_line_its_time_its_active_share_and_its_clock(self):
        found = gpu_clock.samples("\n".join(["first frame", sample(0.1, 0.5, 972.0)]))
        self.assertEqual(found, [gpu_clock.Sample(line=1, t=0.1, active=0.5, mhz=972.0)])

    def test_the_clock_of_samples_weighs_each_sample_by_its_active_share(self):
        found = gpu_clock.samples("\n".join([sample(0.1, 1.0, 1296.0), sample(0.2, 0.25, 648.0)]))
        self.assertAlmostEqual(gpu_clock.clock_of(found).mhz, (1296.0 + 0.25 * 648.0) / 1.25)

    def test_samples_without_active_time_have_an_unknown_clock(self):
        self.assertIsInstance(gpu_clock.clock_of(gpu_clock.samples(sample(0.1, 0.0, 0.0))), gpu_clock.Unknown)


class Record(unittest.TestCase):
    """The record in JSON: a known clock keeps its mean, spread and samples, and an unknown clock keeps its reason."""

    def test_a_known_clock_reads_back_as_it_was_written(self):
        clock = gpu_clock.Known(mhz=1257.25, p10_mhz=1241.0, p90_mhz=1272.0, samples=7)
        self.assertEqual(gpu_clock.from_json(gpu_clock.to_json(clock)), clock)

    def test_an_unknown_clock_reads_back_with_its_reason(self):
        clock = gpu_clock.Unknown("the log has no sample of gpu-clock")
        self.assertEqual(gpu_clock.from_json(gpu_clock.to_json(clock)), clock)

    def test_a_file_without_a_record_has_an_unknown_clock(self):
        self.assertIsInstance(gpu_clock.from_json(None), gpu_clock.Unknown)

    def test_the_line_of_an_unknown_clock_says_unknown_and_why(self):
        line = gpu_clock.Unknown("gpu-clock is not built").describe()
        self.assertIn("unknown", line)
        self.assertIn("gpu-clock is not built", line)

    def test_recording_a_log_writes_its_spans_clock_into_the_timings_and_keeps_the_runs_times(self):
        with tempfile.TemporaryDirectory() as folder:
            log = Path(folder, "run.log")
            log.write_text("\n".join(["timed-start", sample(0.1, 1.0, 1000.0), sample(0.2, 1.0, 1200.0), "timed-end"]))
            timings = Path(folder, "gpu-timings.json")
            timings.write_text(json.dumps({"frame": {"meanMs": 7.5}, "passes": []}))

            clock = gpu_clock.record(log, timings)

            report = json.loads(timings.read_text())
            self.assertEqual(report["frame"], {"meanMs": 7.5})
            self.assertEqual(gpu_clock.from_json(report["gpuClock"]), clock)
            self.assertEqual(clock.mhz, 1100.0)


class Comparison(unittest.TestCase):
    """compare: two timed spans compare only when their clocks differ by at most TOLERANCE, the share by which the
    higher clock exceeds the lower one."""

    def test_clocks_within_the_tolerance_compare(self):
        self.assertEqual(gpu_clock.compare(known(1241.0), known(1272.0)).verdict, gpu_clock.SAME_CLOCK)

    def test_clocks_beyond_the_tolerance_do_not_compare(self):
        self.assertEqual(gpu_clock.compare(known(800.0), known(1050.0)).verdict, gpu_clock.DIFFERENT_CLOCKS)

    def test_the_difference_is_the_share_by_which_the_higher_clock_exceeds_the_lower(self):
        self.assertAlmostEqual(gpu_clock.compare(known(1296.0), known(972.0)).difference, 1296.0 / 972.0 - 1)

    def test_a_difference_of_exactly_the_tolerance_still_compares(self):
        self.assertEqual(
            gpu_clock.compare(known(1000.0), known(1000.0 * (1 + gpu_clock.TOLERANCE))).verdict, gpu_clock.SAME_CLOCK
        )

    def test_an_unknown_clock_leaves_the_comparison_unchecked(self):
        check = gpu_clock.compare(known(1296.0), gpu_clock.Unknown("not recorded"))
        self.assertEqual(check.verdict, gpu_clock.UNCHECKED)
        self.assertIsNone(check.difference)

    def test_the_line_of_a_comparison_at_different_clocks_says_invalid_and_names_both_clocks(self):
        line = gpu_clock.compare(known(800.0), known(1050.0)).line()
        self.assertIn("INVALID", line)
        self.assertIn("800.0", line)
        self.assertIn("1050.0", line)

    def test_the_line_of_a_comparison_at_different_clocks_says_that_the_runs_must_run_again(self):
        line = gpu_clock.compare(known(800.0), known(1050.0)).line()
        self.assertIn("do not compare", line)
        self.assertIn("run them again", line)
        self.assertNotIn("Compare the estimates", line)


class Windowed(unittest.TestCase):
    """windowed_ms: the estimate of a time at the windowed clock, 1296 MHz, by the calibration report's rule."""

    def test_a_time_at_the_windowed_clock_stays_the_same(self):
        self.assertAlmostEqual(gpu_clock.windowed_ms(2.0, known(1296.0)), 2.0)

    def test_a_time_scales_by_the_spans_clock_over_the_windowed_clock(self):
        self.assertAlmostEqual(gpu_clock.windowed_ms(1.991, known(1257.0)), 1.991 * 1257.0 / 1296.0)

    def test_a_time_at_an_unknown_clock_has_no_estimate(self):
        self.assertIsNone(gpu_clock.windowed_ms(2.0, gpu_clock.Unknown("not recorded")))


class TimingsComparison(unittest.TestCase):
    """compare_timings: the lines that compare two runs' gpu-timings.json, pass by pass and for the frame span."""

    @staticmethod
    def timings(span: float, clock):
        report = {"frame": {"meanMs": span}, "passes": [{"pass": "G-buffer", "meanMs": span / 2}]}
        if clock is not None:
            report["gpuClock"] = gpu_clock.to_json(clock)
        return report

    def test_the_lines_say_both_clocks_and_whether_the_runs_compare(self):
        lines = gpu_clock.compare_timings(self.timings(10.0, known(800.0)), self.timings(9.0, known(1050.0)))
        self.assertIn("INVALID", lines[0])

    def test_each_time_comes_with_its_estimate_at_the_windowed_clock(self):
        lines = gpu_clock.compare_timings(self.timings(10.0, known(1296.0)), self.timings(8.0, known(648.0)))
        span = next(line for line in lines if line.startswith("| frame span"))
        self.assertIn("10.000", span)
        self.assertIn("4.000", span)

    def test_runs_without_a_record_compare_unchecked(self):
        lines = gpu_clock.compare_timings(self.timings(10.0, None), self.timings(9.0, None))
        self.assertIn("UNCHECKED", lines[0])


class Launcher(unittest.TestCase):
    """tools/gpu-clock.sh: it runs the command beside gpu-clock, or alone where gpu-clock cannot run, and exits with the
    command's code."""

    @staticmethod
    def launch(*command: str) -> subprocess.CompletedProcess:
        with tempfile.TemporaryDirectory() as empty:
            # A folder without gpu-clock, as on a checkout where nobody built it.
            env = {**os.environ, "GPU_CALIBRATION_BIN": empty}
            return subprocess.run(
                ["bash", str(Path(__file__).with_name("gpu-clock.sh")), *command],
                env=env, capture_output=True, text=True, check=False,
            )

    def test_without_gpu_clock_the_command_still_runs_and_the_clock_is_unknown_with_the_reason(self):
        result = self.launch("bash", "-c", "echo timed-start; echo the work; echo timed-end")
        lines = result.stdout.splitlines()
        self.assertIn("the work", lines)
        self.assertTrue(lines[-1].startswith("GPU clock over the timed span: unknown ("), lines[-1])
        self.assertIn("gpu-clock", lines[-1])

    def test_the_exit_code_is_the_commands(self):
        self.assertEqual(self.launch("bash", "-c", "echo timed-start; echo timed-end; exit 3").returncode, 3)


if __name__ == "__main__":
    unittest.main()
