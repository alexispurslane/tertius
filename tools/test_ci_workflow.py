#!/usr/bin/env python3
"""The GitHub Actions pull-request and main-branch test policy."""

import unittest
from pathlib import Path


class CiWorkflow(unittest.TestCase):
    def test_the_required_check_runs_cpu_checked_tests_and_reports_metrics(self):
        text = Path(__file__).parents[1].joinpath(".github", "workflows", "tests.yml").read_text()

        self.assertIn("types: [opened, ready_for_review, synchronize, reopened]", text)
        self.assertIn("synchronize", text)
        self.assertIn("reopened", text)
        self.assertIn("!github.event.pull_request.draft", text)
        self.assertIn("name: Tests and metrics", text)
        self.assertIn("bash tools/test-all.sh -c Checked -m Core,Simulation,Physics,Assets,Unions,Style,Slop", text)
        self.assertIn("python3 -I -m unittest discover -s tools -p 'test_*.py'", text)
        self.assertIn("python3 -I -m unittest discover -s tools/runpod -t tools/runpod -p 'test_*.py'", text)
        self.assertIn("Tertius.Slop.dll all .", text)
        self.assertIn("tools/pr_metrics.py", text)
        self.assertIn("<!-- tertius-metrics -->", text)
        self.assertIn("repos/$REPO/issues/comments/$id", text)
        self.assertIn("| .id' | tail -1)", text)

    def test_the_pr_job_shares_one_setup_and_caches_dependencies(self):
        text = Path(__file__).parents[1].joinpath(".github", "workflows", "tests.yml").read_text()

        self.assertEqual(text.count("ref: ${{ github.event.pull_request.head.sha }}"), 1)
        self.assertEqual(text.count("nix-community/cache-nix-action@v7"), 2)
        self.assertEqual(text.count("actions/cache@v4"), 2)
        self.assertIn("primary-key: nix-", text)
        self.assertIn("restore-prefixes-first-match: nix-", text)
        self.assertIn("path: ~/.nuget/packages", text)

    def test_checkouts_are_sparse_and_keep_the_small_cleanup(self):
        text = Path(__file__).parents[1].joinpath(".github", "workflows", "tests.yml").read_text()
        expected = (
            ".agents",
            ".config",
            ".github",
            ".githooks",
            "analyzers",
            "benchmarks",
            "content",
            "native",
            "scripts",
            "src",
            "tests",
            "tools",
        )

        blocks = text.split("sparse-checkout: |")[1:]
        self.assertEqual(len(blocks), 2)
        for block in blocks:
            entries = []
            for line in block.splitlines():
                if line.startswith("            "):
                    entries.append(line.strip())
                elif entries:
                    break
            self.assertEqual(tuple(entries), expected)
        self.assertNotIn("/usr/local/lib/android", text)
        self.assertIn("sudo rm -rf /opt/hostedtoolcache", text)
        self.assertIn("du -sh .", text)
        self.assertIn("duration:", text)

    def test_the_cpu_release_suite_runs_only_on_main(self):
        text = Path(__file__).parents[1].joinpath(".github", "workflows", "tests.yml").read_text()

        self.assertIn("branches: [main]", text)
        self.assertIn("github.event_name == 'push' && github.ref == 'refs/heads/main'", text)
        self.assertIn("bash tools/test-all.sh -c Release -m Core,Simulation,Physics,Assets,Unions,Style,Slop", text)


if __name__ == "__main__":
    unittest.main()
