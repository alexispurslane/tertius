"""Behavior tests of the staged commit report's findings and project addendum."""

import contextlib
import copy
import io
import json
import runpy
import tempfile
import unittest
from unittest.mock import patch


report = runpy.run_path("tools/commit_report.py")


def metric(numerator, denominator):
    return {"numerator": numerator, "denominator": denominator,
            "ratio": numerator / denominator if denominator else None}


def details():
    summary = {"provenance": {"version": "v1", "references": "refs"}, "tcc": metric(2, 5),
               "depth": metric(8, 4), "typeCount": 2}
    return {"report": {
        "erosion": {"erosion": 0.25, "highCcMass": 120, "highCcFunctionCount": 12,
                    "complexityLimit": 10, "highCcFunctions": [
                        {"file": "src/A.cs", "line": 10, "endLine": 30, "name": "A.Run", "cc": 12,
                         "sloc": 20, "mass": 53.7}]},
        "verbosity": {"verbosity": 0.03, "verboseLines": 40,
                      "rules": [{"id": "V1", "message": "Simplify this expression"}],
                      "findings": [{"file": "src/A.cs", "line": 15, "endLine": 15, "rule": "V1"}],
                      "clones": [{"first": {"file": "src/B.cs", "line": 2, "endLine": 8},
                                  "second": {"file": "src/A.cs", "line": 12, "endLine": 18}}]},
        "classMetrics": {"summary": summary, "types": []}},
        "baseline": {"commit": "abc1234", "note": {"erosion": 0.25, "verbosity": 0.03,
                                                    "classMetrics": copy.deepcopy(summary)}}}


def render(data, changed=None, length=False):
    output, errors = io.StringIO(), io.StringIO()
    with tempfile.NamedTemporaryFile(mode="w", suffix=".json") as file:
        file.write(data) if isinstance(data, str) else json.dump(data, file)
        file.flush()
        args = ["commit_report.py", "--slop", file.name] if data is not None else ["commit_report.py"]
        with patch.dict(report["main"].__globals__, changed_lines=lambda: changed or {},
                        report_length=lambda _: length), patch("sys.argv", args), \
                contextlib.redirect_stdout(output), contextlib.redirect_stderr(errors):
            report["main"]()
    return output.getvalue(), errors.getvalue()


class CommitReportCases(unittest.TestCase):
    def test_touched_findings_and_prompt_precede_ratios_even_when_ratios_are_flat_or_falling(self):
        for old_erosion, old_verbosity in ((0.25, 0.03), (0.3, 0.04)):
            with self.subTest(old_erosion=old_erosion):
                data = details()
                data["baseline"]["note"].update(erosion=old_erosion, verbosity=old_verbosity)
                text, errors = render(data, {"src/A.cs": {15}})
                self.assertEqual(errors, "")
                for finding in ("A.Run: CC 12, SLOC 20, mass 53.7", "V1: Simplify this expression",
                                "src/B.cs:2-8 and src/A.cs:12-18"):
                    self.assertIn(finding, text)
                    self.assertLess(text.index(finding), text.index("Refactor now:"))
                self.assertIn("Project metrics", text)
                self.assertLess(text.index("Refactor now:"), text.index("Project metrics"))
                self.assertIn("Erosion 0.2500", text)
                self.assertNotIn("High CC mass", text)
                self.assertNotIn("Verbose lines", text)
                self.assertEqual(text.count("abc1234"), 1)

    def test_untouched_problems_do_not_appear_or_prompt(self):
        text, errors = render(details(), {"src/A.cs": {40}})
        self.assertEqual(errors, "")
        self.assertNotIn("A.Run", text)
        self.assertNotIn("V1:", text)
        self.assertNotIn("src/B.cs", text)
        self.assertNotIn("Refactor now:", text)
        self.assertIn("Project metrics", text)

    def test_touching_either_copy_lists_the_clone(self):
        text, _ = render(details(), {"src/B.cs": {3}})
        self.assertIn("src/B.cs:2-8 and src/A.cs:12-18", text)
        self.assertIn("Refactor now:", text)
        self.assertNotIn("A.Run", text)

    def test_length_alone_still_prompts_without_a_measurement(self):
        text, errors = render(None, length=True)
        self.assertEqual(errors, "")
        self.assertIn("Refactor now:", text)
        self.assertNotIn("Project metrics", text)

    def test_summary_failure_does_not_suppress_the_refactoring_prompt(self):
        data = details()
        data["report"]["classMetrics"]["summary"]["tcc"]["ratio"] = "invalid"
        text, errors = render(data, {"src/A.cs": {15}})
        self.assertIn("Refactor now:", text)
        self.assertIn("report failed", errors)

    def test_each_kind_of_finding_is_sufficient_to_prompt(self):
        for kind in ("highCcFunctions", "findings", "clones"):
            with self.subTest(kind=kind):
                data = details()
                if kind != "highCcFunctions":
                    data["report"]["erosion"]["highCcFunctions"] = []
                for key in ("findings", "clones"):
                    if key != kind:
                        data["report"]["verbosity"][key] = []
                text, errors = render(data, {"src/A.cs": {15}})
                self.assertEqual(errors, "")
                self.assertIn("Refactor now:", text)

    def test_invalid_details_do_not_suppress_the_length_prompt(self):
        text, errors = render("{invalid json", length=True)
        self.assertIn("Refactor now:", text)
        self.assertNotIn("Project metrics", text)
        self.assertIn("report failed", errors)

    def test_failure_in_one_finding_section_keeps_the_other_findings_and_prompt(self):
        data = details()
        del data["report"]["erosion"]["highCcFunctions"]
        text, errors = render(data, {"src/A.cs": {15}})
        self.assertIn("V1: Simplify this expression", text)
        self.assertIn("Refactor now:", text)
        self.assertIn("Project metrics", text)
        self.assertIn("report failed", errors)

    def test_a_change_too_small_to_display_does_not_show_negative_zero(self):
        data = details()
        data["baseline"]["note"]["erosion"] = 0.25000001
        text, errors = render(data)
        self.assertEqual(errors, "")
        self.assertIn("Erosion 0.2500 (+0.0000)", text)
        self.assertNotIn("-0.0000", text)


class ClassReportCases(unittest.TestCase):
    def test_project_addendum_shows_only_ratios_and_comparable_deltas(self):
        data = details()
        data["baseline"]["note"]["classMetrics"].update(tcc=metric(1, 4), depth=metric(6, 3))
        text, errors = render(data)
        self.assertEqual(errors, "")
        self.assertIn("TCC 0.4000 (+0.1500)", text)
        self.assertIn("Depth 2.0000 (+0.0000)", text)
        for absolute in ("8/4", "2/5", "shared pairs", "WMC", "API entries", "Type metrics v1"):
            self.assertNotIn(absolute, text)

    def test_zero_denominator_is_not_applicable_and_missing_baseline_has_no_delta(self):
        data = details()
        data["baseline"] = None
        data["report"]["classMetrics"]["summary"].update(tcc=metric(0, 0), depth=metric(2, 0))
        text, errors = render(data)
        self.assertEqual(errors, "")
        self.assertIn("TCC n/a\n", text)
        self.assertIn("Depth n/a\n", text)
        self.assertNotIn("(+", text)
        self.assertNotIn("(-", text)

    def test_missing_type_metrics_are_unavailable(self):
        data = details()
        del data["report"]["classMetrics"]
        text, errors = render(data)
        self.assertEqual(errors, "")
        self.assertIn("Type metrics unavailable (legacy report)", text)

    def test_missing_depth_is_unavailable_and_does_not_invent_a_delta(self):
        data = details()
        del data["report"]["classMetrics"]["summary"]["depth"]
        text, _ = render(data)
        self.assertIn("Depth unavailable (legacy report)", text)
        self.assertNotIn("Depth n/a", text)
        data = details()
        del data["baseline"]["note"]["classMetrics"]["depth"]
        text, _ = render(data)
        self.assertIn("Depth 2.0000\n", text)

    def test_different_configuration_suppresses_type_comparison_and_keeps_warning_after_values(self):
        data = details()
        data["baseline"]["note"]["classMetrics"]["provenance"]["references"] = "other"
        data["report"]["classMetrics"]["summary"]["bindingErrorCount"] = 7
        text, errors = render(data)
        self.assertEqual(errors, "")
        self.assertIn("TCC 0.4000\n", text)
        self.assertIn("Depth 2.0000\n", text)
        self.assertIn("Binding errors: 7", text)
        self.assertIn("configuration differs", text)
        self.assertLess(text.index("Depth 2.0000"), text.index("Binding errors:"))


if __name__ == "__main__":
    unittest.main()
