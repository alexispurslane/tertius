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
        note = {
            "erosion": 0.1831042732532144,
            "verbosity": 0.024096024455666613,
            "highCcMass": 8855.840024192496,
            "highCcFunctionCount": 79,
            "verboseLines": 1608,
            "classMetrics": {
                "provenance": {"version": "type-metrics-v3", "references": "abc123"},
                "tcc": {"numerator": 7697, "denominator": 8671, "ratio": 0.8876715488409641},
                "typeCount": 1124,
                "bindingErrorCount": 3982,
                "depth": {"numerator": 12039, "denominator": 4718, "ratio": 2.5517168291649},
                "cohesionPopulation": {"included": 206, "notApplicable": 873, "incomplete": 45},
                "depthPopulation": {"included": 570, "notApplicable": 554, "incomplete": 0},
                "unresolvedStateAccessCount": 303,
            },
            "sloc": 66733,
        }

        text = pr_metrics.markdown(note, head="deadbeef", run_url="https://example.test/run/1")

        self.assertTrue(text.startswith("<!-- tertius-metrics -->\n"))
        self.assertIn("Tests and metrics passed for `deadbeef`", text)
        self.assertIn("[View the CI run](https://example.test/run/1)", text)
        self.assertIn("| Erosion | 0.1831 | Ratio of high CC function mass to all function mass |", text)
        self.assertIn("| High CC mass | 8855.84 | CC above 10 |", text)
        self.assertIn("| High CC functions | 79 | Functions with CC above 10 |", text)
        self.assertIn("| Verbosity | 0.0241 | Rule-flagged or cloned lines divided by SLOC |", text)
        self.assertIn("| Verbose lines | 1608 | Distinct flagged or cloned lines |", text)
        self.assertIn("| SLOC | 66733 | Source lines of code |", text)
        self.assertIn("| TCC | 0.8877 | 7697/8671 shared/possible method pairs |", text)
        self.assertIn("| Depth | 2.5517 | 12039/4718 WMC/API entries |", text)
        self.assertIn("| Cohesion population | 206 included, 873 not applicable, 45 incomplete | Type counts |", text)
        self.assertIn("| Depth population | 570 included, 554 not applicable, 0 incomplete | Type counts |", text)
        self.assertIn("| Binding errors | 3982 | Unresolved compiler bindings in source analysis |", text)
        self.assertIn("| Unresolved state access | 303 | Locations with unresolved state access |", text)
        self.assertIn("| Types measured | 1124 | Types in source analysis |", text)
        self.assertIn("| Metric version | type-metrics-v3 | Counting rules |", text)
        self.assertIn("| References | abc123 | Metadata references |", text)

    def test_an_older_note_without_class_metrics_reports_that_they_are_unavailable(self):
        note = {"erosion": 0.2, "verbosity": 0.03, "sloc": 100}

        text = pr_metrics.markdown(note, head="abc", run_url="https://example.test/run/2")

        self.assertIn("| TCC | Unavailable | No class metrics in this note |", text)
        self.assertIn("| Depth | Unavailable | No class metrics in this note |", text)
        self.assertIn("| High CC mass | Unavailable | No absolute count in this note |", text)


if __name__ == "__main__":
    unittest.main()
