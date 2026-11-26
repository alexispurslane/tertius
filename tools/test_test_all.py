#!/usr/bin/env python3
"""The test runner's machine-readable summaries and CI's historical checkout."""

import unittest
from pathlib import Path

ROOT = Path(__file__).parents[1]


class TestAllAndCheckout(unittest.TestCase):
    def test_the_summary_parser_strips_ansi_colors_without_changing_test_execution(self):
        text = (ROOT / "tools/test-all.sh").read_text()

        self.assertIn("sed $'s/\\x1b\\[[0-9;]*m//g'", text)
        self.assertNotIn('--noColor', text)

    def test_every_ci_checkout_contains_the_historical_test_fixtures(self):
        text = (ROOT / ".github/workflows/tests.yml").read_text()

        self.assertEqual(text.count("fetch-depth: 0"), 2)


if __name__ == "__main__":
    unittest.main()
