"""Behavior tests of the staged commit report's slop section."""

import contextlib
import io
import runpy
import unittest


report = runpy.run_path("tools/commit_report.py")


class CommitReportCases(unittest.TestCase):
    def test_absolute_counts_show_their_change_beside_falling_ratios(self):
        baseline = {"commit": "abc1234", "note": {"erosion": 0.3, "verbosity": 0.04,
                                               "highCcMass": 100, "highCcFunctionCount": 10,
                                               "verboseLines": 30}}
        erosion = {"erosion": 0.25, "highCcMass": 120, "highCcFunctionCount": 12,
                   "complexityLimit": 10, "highCcFunctions": []}
        verbosity = {"verbosity": 0.03, "verboseLines": 40, "rules": [], "findings": [], "clones": []}

        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            report["report_erosion"]({}, erosion, baseline)
            report["report_verbosity"]({}, verbosity, baseline)

        text = output.getvalue()
        self.assertIn("Erosion 0.2500 (-0.0500", text)
        self.assertIn("High CC mass 120.0 (+20.0", text)
        self.assertIn("High CC functions 12 (+2", text)
        self.assertIn("Verbose lines 40 (+10", text)

    def test_old_baseline_has_no_invented_absolute_change(self):
        baseline = {"commit": "abc1234", "note": {"erosion": 0.3, "verbosity": 0.04}}
        erosion = {"erosion": 0.25, "highCcMass": 120, "highCcFunctionCount": 12,
                   "complexityLimit": 10, "highCcFunctions": []}
        verbosity = {"verbosity": 0.03, "verboseLines": 40, "rules": [], "findings": [], "clones": []}

        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            report["report_erosion"]({}, erosion, baseline)
            report["report_verbosity"]({}, verbosity, baseline)

        text = output.getvalue()
        self.assertIn("High CC mass 120.0\n", text)
        self.assertIn("High CC functions 12\n", text)
        self.assertIn("Verbose lines 40\n", text)
        self.assertNotIn("High CC mass 120.0 (", text)


class ClassReportCases(unittest.TestCase):
    @staticmethod
    def metric(numerator, denominator):
        return {"numerator": numerator, "denominator": denominator,
                "ratio": numerator / denominator if denominator else None}

    def render(self, now, before=None):
        import tempfile
        import json
        details = {"report": {
            "erosion": {"erosion": 0, "highCcMass": 0, "highCcFunctionCount": 0, "complexityLimit": 10, "highCcFunctions": []},
            "verbosity": {"verbosity": 0, "verboseLines": 0, "rules": [], "findings": [], "clones": []},
            "classMetrics": {"summary": now, "types": []}},
            "baseline": {"commit": "abc1234", "note": {"classMetrics": before}} if before else None}
        with tempfile.NamedTemporaryFile(mode="w", suffix=".json") as file:
            json.dump(details, file)
            file.flush()
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                report["report_slop"]({}, file.name)
            return output.getvalue()

    def summary(self):
        return {"provenance": {"version": "v1", "references": "refs"}, "tcc": self.metric(2, 5),
                "depth": self.metric(8, 4), "typeCount": 2}

    def test_tcc_shows_counts_and_comparable_deltas(self):
        now = self.summary()
        before = self.summary()
        before["tcc"] = self.metric(1, 4)
        before["depth"] = self.metric(6, 3)
        text = self.render(now, before)
        self.assertIn("TCC 0.4000", text)
        self.assertNotIn("Method groups", text)
        self.assertIn("Depth 2.0000 (8/4 WMC/API entries)", text)
        self.assertIn("WMC +2, API entries +1", text)
        self.assertIn("+0.1500", text)
        self.assertIn("2/5", text)
        self.assertIn("shared pairs +1, possible pairs +1", text)

    def test_zero_denominator_is_not_applicable_and_missing_baseline_has_no_delta(self):
        now = self.summary()
        now["tcc"] = self.metric(0, 0)
        now["depth"] = self.metric(2, 0)
        text = self.render(now)
        self.assertIn("TCC n/a (0/0 shared/possible method pairs)", text)
        self.assertIn("Depth n/a (2/0 WMC/API entries)", text)
        self.assertNotIn("shared pairs +", text)
        self.assertNotIn("WMC +", text)

    def test_missing_depth_is_unavailable_and_does_not_invent_a_delta(self):
        now = self.summary()
        del now["depth"]
        text = self.render(now)
        self.assertIn("Depth unavailable (legacy report)", text)
        self.assertNotIn("Depth n/a", text)
        current = self.render(self.summary(), now)
        self.assertIn("Depth 2.0000", current)
        self.assertNotIn("WMC +", current)

    def test_different_configuration_suppresses_comparison(self):
        now = self.summary()
        before = self.summary()
        before["provenance"]["references"] = "other"
        text = self.render(now, before)
        self.assertIn("configuration differs", text)
        self.assertNotIn("shared pairs +", text)


if __name__ == "__main__":
    unittest.main()
