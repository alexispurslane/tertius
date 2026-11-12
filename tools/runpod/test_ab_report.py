#!/usr/bin/env python3
"""Tool code. The A/B report of a cloud job (tools/runpod/ab_report.py): the gain of each tree against the base tree,
read against the spread of the runs.

    python3 -I tools/runpod/test_ab_report.py

The standard library only.
"""

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("ab_report", Path(__file__).with_name("ab_report.py"))
ab_report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ab_report)


def timings(span: float, passes: dict) -> dict:
    """A gpu-timings.json of one headless run, as the demo writes it."""
    return {"frame": {"frames": 119, "medianMs": span},
            "passes": [{"pass": name, "frames": 119, "medianMs": ms} for name, ms in passes.items()]}


class Report(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.root = Path(self.folder.name)

    def tearDown(self):
        self.folder.cleanup()

    def run_of(self, tree: str, run: int, span: float, light: float, capture: float = 2.0):
        folder = self.root / "out" / tree / f"run-{run}"
        folder.mkdir(parents=True)
        (folder / "gpu-timings.json").write_text(json.dumps(timings(span, {"direct light": light, "capture": capture})))

    def test_each_tree_holds_the_frame_span_of_each_run_without_the_capture(self):
        self.run_of("main", 1, 11.0, 2.0, capture=2.5)
        self.run_of("main", 2, 11.2, 2.1, capture=2.3)
        runs = ab_report.read(self.root)
        self.assertEqual(sorted(runs), ["main"])
        self.assertEqual([round(run.span, 3) for run in runs["main"]], [8.5, 8.9],
                         "the headless readback is not the renderer's work (ter-fw73)")

    def test_a_variant_whose_runs_all_lie_below_the_bases_runs_is_a_gain_with_its_mac_estimate(self):
        for run, (base, variant) in enumerate([(10.0, 9.0), (10.2, 9.1), (10.1, 9.2)], 1):
            self.run_of("main", run, base + 2.0, 2.0)
            self.run_of("cut", run, variant + 2.0, 1.0)
        result = ab_report.compare(ab_report.read(self.root), base="main")["cut"]
        self.assertAlmostEqual(result.delta_ms, 9.1 - 10.1)
        self.assertAlmostEqual(result.relative, 9.1 / 10.1 - 1)
        self.assertTrue(result.separated)
        self.assertAlmostEqual(result.mac_ms, (9.1 - 10.1) * ab_report.MAC_FACTOR)

    def test_a_variant_whose_runs_overlap_the_bases_runs_is_within_the_noise(self):
        for run, (base, variant) in enumerate([(10.0, 9.9), (10.4, 10.3), (10.2, 10.0)], 1):
            self.run_of("main", run, base + 2.0, 2.0)
            self.run_of("same", run, variant + 2.0, 2.0)
        result = ab_report.compare(ab_report.read(self.root), base="main")["same"]
        self.assertFalse(result.separated)

    def test_the_passes_that_moved_most_come_first(self):
        for run in (1, 2):
            self.run_of("main", run, 12.0, 2.0)
            self.run_of("cut", run, 11.0, 1.0)
        moved = ab_report.compare(ab_report.read(self.root), base="main")["cut"].passes
        self.assertEqual(moved[0], ("direct light", 2.0, 1.0))


if __name__ == "__main__":
    unittest.main()
