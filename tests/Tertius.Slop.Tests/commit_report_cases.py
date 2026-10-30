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


if __name__ == "__main__":
    unittest.main()
