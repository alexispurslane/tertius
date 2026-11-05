#!/usr/bin/env python3
"""Tool code. GPU clock records, valid timing comparisons and timing-wrapper output, using supplied reports and logs.

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
            original = {
                "device": "supplied device", "width": 160, "height": 90,
                "frame": {"frames": 20, "meanMs": 7.5, "medianMs": 7.0, "p95Ms": 10.0},
                "passes": [{"pass": "capture", "frames": 20, "meanMs": 1.0, "medianMs": 0.5, "p95Ms": 2.0}],
                "runs": [{"first": "capture", "last": "capture", "timing": {"meanMs": 1.0}}],
                "stampsLatchLate": True, "warning": "Raw pass values are not valid alone.",
            }
            timings.write_text(json.dumps(original))

            clock = gpu_clock.record(log, timings)

            report = json.loads(timings.read_text())
            self.assertEqual({key: value for key, value in report.items() if key != "gpuClock"}, original)
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
    """compare_timings: frame spans, requested ranges and only valid individual-pass comparisons."""

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

    def test_legacy_reports_keep_individual_pass_comparisons(self):
        lines = gpu_clock.compare_timings(self.timings(10.0, known(1296.0)), self.timings(8.0, known(1296.0)))
        self.assertIn("| G-buffer | 5.000 | 4.000 | -1.000 | 5.000 | 4.000 |", lines)

    def test_ordinary_driver_reports_keep_individual_pass_comparisons(self):
        reference = self.timings(10.0, known(1296.0))
        other = self.timings(8.0, known(1296.0))
        reference.update(stampsLatchLate=False, warning=None)
        other.update(stampsLatchLate=False, warning=None)

        lines = gpu_clock.compare_timings(reference, other)

        self.assertIn("| G-buffer | 5.000 | 4.000 | -1.000 | 5.000 | 4.000 |", lines)

    def test_a_late_driver_in_either_report_hides_all_individual_pass_comparisons(self):
        warning = "Individual pass times are omitted because timestamps can include neighbouring work."
        for reference_late, other_late in [(True, True), (True, False), (False, True)]:
            with self.subTest(reference_late=reference_late, other_late=other_late):
                reference = self.timings(10.0, known(1296.0))
                other = self.timings(8.0, known(1296.0))
                reference.update(stampsLatchLate=reference_late, warning=warning if reference_late else None)
                other.update(stampsLatchLate=other_late, warning=warning if other_late else None)
                reference["passes"].append({"pass": "only reference", "meanMs": 3.0})
                other["passes"].append({"pass": "only other", "meanMs": 4.0})

                lines = gpu_clock.compare_timings(reference, other)

                rows = [line for line in lines if line.startswith("| ")]
                self.assertEqual(len(rows), 2, rows)
                self.assertIn("| frame span | 10.000 | 8.000 | -2.000 | 10.000 | 8.000 |", lines)
                self.assertIn(warning, "\n".join(lines))

    def test_a_report_warning_without_a_driver_flag_still_prevents_individual_pass_comparisons(self):
        reference = self.timings(10.0, None)
        other = self.timings(8.0, None)
        other["warning"] = "Raw individual pass values are not valid alone."

        lines = gpu_clock.compare_timings(reference, other)

        self.assertFalse(any(line.startswith("| G-buffer |") for line in lines), lines)
        self.assertIn(other["warning"], "\n".join(lines))

    def test_a_late_driver_flag_without_a_warning_still_explains_omitted_pass_comparisons(self):
        reference = self.timings(10.0, None)
        other = self.timings(8.0, None)
        reference["stampsLatchLate"] = True

        lines = gpu_clock.compare_timings(reference, other)

        self.assertFalse(any(line.startswith("| G-buffer |") for line in lines), lines)
        self.assertIn("Individual pass comparisons are omitted", "\n".join(lines))

    def test_requested_ranges_use_the_reported_per_frame_aggregate_means(self):
        for late in [False, True]:
            with self.subTest(stamps_latch_late=late):
                reference = self.timings(10.0, known(1296.0))
                other = self.timings(8.0, known(648.0))
                reference.update(stampsLatchLate=late, runs=[
                    {"first": "bloom", "last": "capture", "timing": {"meanMs": 7.5}},
                    {"first": "shadow maps", "last": "direct light", "timing": {"meanMs": 3.0}},
                ])
                other.update(stampsLatchLate=late, runs=[
                    {"first": "bloom", "last": "capture", "timing": {"meanMs": 6.25}},
                    {"first": "bloom", "last": "bloom", "timing": {"meanMs": 2.0}},
                ])

                lines = gpu_clock.compare_timings(reference, other)

                ranges = [line for line in lines if line.startswith("| run ")]
                self.assertEqual(ranges, [
                    "| run bloom..capture | 7.500 | 6.250 | -1.250 | 7.500 | 3.125 |",
                    "| run shadow maps..direct light | 3.000 |  |  | 3.000 |  |",
                    "| run bloom..bloom |  | 2.000 |  |  | 1.000 |",
                ])

    def test_a_requested_range_can_be_compared_with_a_legacy_report_without_ranges(self):
        reference = self.timings(10.0, None)
        other = self.timings(8.0, None)
        other["runs"] = [{"first": "bloom", "last": "capture", "timing": {"meanMs": 2.0}}]

        lines = gpu_clock.compare_timings(reference, other)

        self.assertIn("| run bloom..capture |  | 2.000 |  |  |  |", lines)
        self.assertIn("| G-buffer | 5.000 | 4.000 | -1.000 |  |  |", lines)

    def test_the_comparison_command_keeps_only_valid_rows_and_explanations(self):
        with tempfile.TemporaryDirectory() as folder:
            reference = Path(folder, "reference.json")
            other = Path(folder, "other.json")
            report = self.timings(10.0, None)
            report.update(
                stampsLatchLate=True, warning="Only the frame span and neighbouring sums are valid.",
                runs=[{"first": "bloom", "last": "capture", "timing": {"meanMs": 3.0}}],
            )
            reference.write_text(json.dumps(report))
            other.write_text(json.dumps(self.timings(8.0, None)))

            result = subprocess.run(
                ["python3", "-I", str(Path(__file__).with_name("gpu_clock.py")), "compare", str(reference), str(other)],
                capture_output=True, text=True, check=False, timeout=10,
            )

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn(report["warning"], result.stdout)
            self.assertIn("| frame span | 10.000 | 8.000 | -2.000 |", result.stdout)
            self.assertIn("| run bloom..capture | 3.000 |  |  |", result.stdout)
            self.assertNotIn("| G-buffer |", result.stdout)


class DemoTiming(unittest.TestCase):
    """The real timing wrapper forwards report warnings, with external build and renderer commands substituted."""

    @staticmethod
    def launch(report: dict, log: str, *options: str) -> subprocess.CompletedProcess:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            commands = root / "commands"
            commands.mkdir()
            git = commands / "git"
            git.write_text('#!/usr/bin/env bash\nprintf "%s\\n" "$DEMO_TEST_ROOT"\n')
            git.chmod(0o755)
            dotnet = commands / "dotnet"
            dotnet.write_text("#!/usr/bin/env bash\nexit 0\n")
            dotnet.chmod(0o755)
            demo = root / "src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo"
            demo.parent.mkdir(parents=True)
            demo.write_text(
                '#!/usr/bin/env bash\ncp "$DEMO_TEST_ROOT/report.json" "$DEMO_TEST_ROOT/out/gpu-timings.json"\n'
                'cat "$DEMO_TEST_ROOT/demo.log"\n'
            )
            demo.chmod(0o755)
            (root / "report.json").write_text(json.dumps(report))
            (root / "demo.log").write_text(log)
            script = root / "script.json"
            script.write_text("{}")
            env = {
                **os.environ,
                "PATH": str(commands) + os.pathsep + os.environ["PATH"],
                "DEMO_TEST_ROOT": folder,
                "GPU_CALIBRATION_BIN": str(root / "no-gpu-clock"),
                "TERTIUS_GPU_LOCK": str(root / "gpu.lock"),
                "TERTIUS_GPU_QUEUE": str(root / "gpu.queue"),
            }
            return subprocess.run(
                ["bash", str(Path(__file__).with_name("demo-timing.sh").resolve()), str(root / "out"), str(script), *options],
                env=env, cwd=root, capture_output=True, text=True, check=False, timeout=30,
            )

    def test_a_late_driver_explanation_reaches_the_terminal_with_or_without_requested_ranges(self):
        warning = "A pass can include neighbouring work. Individual pass times are omitted; raw values are not valid alone."
        for requested in [False, True]:
            with self.subTest(requested_ranges=requested):
                report = {"frame": {"meanMs": 10.0}, "passes": [], "stampsLatchLate": True, "warning": warning}
                span = "GPU frame span: mean 10.000 ms"
                run = "GPU run bloom..capture: mean 3.000 ms"
                report["runs"] = [{"first": "bloom", "last": "capture", "timing": {"meanMs": 3.0}}] if requested else []
                log = "\n".join(["timed-start", "timed-end", warning, span, *([run] if requested else [])]) + "\n"

                result = self.launch(report, log, *(["--gpu-run", "bloom..capture"] if requested else []))

                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(result.stdout.splitlines().count(warning), 1, result.stdout)
                self.assertIn(span, result.stdout)
                self.assertEqual(run in result.stdout, requested)
                self.assertTrue(result.stdout.splitlines()[-1].startswith("GPU clock over the timed span:"))

    def test_an_ordinary_driver_keeps_its_individual_pass_output_without_a_warning(self):
        report = {"frame": {"meanMs": 10.0}, "passes": [], "stampsLatchLate": False, "warning": None, "runs": []}
        capture = "GPU capture: mean 3.000 ms"
        span = "GPU frame span: mean 10.000 ms"

        result = self.launch(report, "\n".join(["timed-start", "timed-end", capture, span]) + "\n")

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.splitlines()[:2], [capture, span])
        self.assertTrue(result.stdout.splitlines()[-1].startswith("GPU clock over the timed span:"))

    def test_a_legacy_report_without_validity_fields_keeps_its_individual_pass_output(self):
        report = {"frame": {"meanMs": 10.0}, "passes": []}
        capture = "GPU capture: mean 3.000 ms"
        span = "GPU frame span: mean 10.000 ms"

        result = self.launch(report, "\n".join(["timed-start", "timed-end", capture, span]) + "\n")

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stderr, "", "a legacy report needs no warning field")
        self.assertEqual(result.stdout.splitlines()[:2], [capture, span])
        self.assertTrue(result.stdout.splitlines()[-1].startswith("GPU clock over the timed span:"))


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
