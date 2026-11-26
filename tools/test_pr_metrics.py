#!/usr/bin/env python3
"""The pull-request metrics comment (tools/pr_metrics.py): every measure and its supporting evidence."""

import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("pr_metrics", Path(__file__).with_name("pr_metrics.py"))
pr_metrics = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pr_metrics)


class MetricsComment(unittest.TestCase):
    def test_the_comment_has_every_metric_and_its_supporting_evidence(self):
        report = {
            "erosion": {
                "complexityLimit": 10,
                "erosion": 0.1831042732532144,
                "totalMass": 20687.9,
                "highCcMass": 8855.840024192496,
                "functionCount": 4012,
                "highCcFunctionCount": 79,
                "sloc": 66733,
                "fileCount": 268,
            },
            "verbosity": {
                "verbosity": 0.024096024455666613,
                "verboseLines": 1608,
                "flaggedLines": 1500,
                "cloneLines": 108,
                "sloc": 66733,
            },
            "classMetrics": {
                "summary": {
                    "provenance": {"version": "type-metrics-v3", "references": "abc123"},
                    "tcc": {"numerator": 7697, "denominator": 8671, "ratio": 0.8876715488409641},
                    "typeCount": 1124,
                    "bindingErrorCount": 3982,
                    "depth": {"numerator": 12039, "denominator": 4718, "ratio": 2.5517168291649},
                    "cohesionPopulation": {"included": 206, "notApplicable": 873, "incomplete": 45},
                    "depthPopulation": {"included": 570, "notApplicable": 554, "incomplete": 0},
                    "unresolvedStateAccessCount": 303,
                }
            },
        }

        text = pr_metrics.markdown(report, head="deadbeef", run_url="https://example.test/run/1")

        self.assertTrue(text.startswith("<!-- tertius-metrics -->\n"))
        self.assertIn("CPU Checked and Python tools tests passed for `deadbeef`", text)
        self.assertIn("[View the CI run](https://example.test/run/1)", text)
        self.assertIn("| Erosion | 0.183 | High CC mass 8855.8 of 20687.9, from 79 of 4012 functions above CC 10 |", text)
        self.assertIn("| Verbosity | 0.024 | 1608 of 66733 lines: 1500 flagged, 108 cloned |", text)
        self.assertIn("| TCC | 0.8877 | 7697/8671 shared/possible method pairs |", text)
        self.assertIn("| Depth | 2.5517 | 12039/4718 WMC/API entries |", text)
        self.assertIn("| SLOC | 66733 | 268 files |", text)
        self.assertIn("| Cohesion population | 206 included, 873 not applicable, 45 incomplete | Type counts |", text)
        self.assertIn("| Depth population | 570 included, 554 not applicable, 0 incomplete | Type counts |", text)
        self.assertIn("| Binding errors | 3982 | Unresolved compiler bindings in source analysis |", text)
        self.assertIn("| Unresolved state access | 303 | Locations with unresolved state access |", text)
        self.assertIn("| Types measured | 1124 | Types in source analysis |", text)
        self.assertIn("| Metric version | type-metrics-v3 | Counting rules |", text)
        self.assertIn("| References | abc123 | Metadata references |", text)

    def test_tool_logs_before_the_json_do_not_change_the_report(self):
        report = pr_metrics.loads('nix download log\n{"erosion": {"erosion": 0.2}, "verbosity": {}}\n')

        self.assertEqual(report["erosion"]["erosion"], 0.2)

    def test_an_older_report_without_class_metrics_reports_that_they_are_unavailable(self):
        report = {"erosion": {"erosion": 0.2, "sloc": 100}, "verbosity": {"verbosity": 0.03}}

        text = pr_metrics.markdown(report, head="abc", run_url="https://example.test/run/2")

        self.assertIn("| TCC | Unavailable | No class metrics in this report |", text)
        self.assertIn("| Depth | Unavailable | No class metrics in this report |", text)
        self.assertIn("| Erosion | 0.200 | High CC mass unavailable; function counts unavailable |", text)


if __name__ == "__main__":
    unittest.main()
