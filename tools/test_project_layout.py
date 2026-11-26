#!/usr/bin/env python3
"""The canonical agent configuration and the home of Tertius design documents."""

import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).parents[1]


class ProjectLayout(unittest.TestCase):
    def test_agents_is_the_canonical_agent_configuration(self):
        agents = (ROOT / "AGENTS.md").read_text()
        claude = (ROOT / "CLAUDE.md").read_text()

        self.assertIn("This file is the canonical configuration for agents on Tertius", agents)
        self.assertIn("## Interpreting code quality metrics", agents)
        self.assertIn('"AGENTS.md"', (ROOT / "docfx.json").read_text())
        self.assertIn("[AGENTS.md](AGENTS.md) is the canonical configuration for agents", claude)
        self.assertNotIn("## Workflow", claude)

    def test_design_documents_live_only_in_the_github_wiki(self):
        tracked = subprocess.check_output(
            ["git", "-C", str(ROOT), "ls-tree", "-r", "--name-only", "HEAD"], text=True
        ).splitlines()
        self.assertFalse([path for path in tracked if path.startswith("docs/plans/")])
        self.assertIn("https://github.com/alexispurslane/tertius/wiki", (ROOT / "README.md").read_text())
        self.assertNotIn("docs/plans", (ROOT / "docfx.json").read_text())

        excluded = {".agents/skills/tertius-issues/SKILL.md", "tools/test_project_layout.py"}
        result = subprocess.run(
            ["git", "-C", str(ROOT), "grep", "-l", "-F", "docs/plans", "HEAD", "--", "."],
            capture_output=True,
            text=True,
        )
        self.assertIn(result.returncode, {0, 1})
        offenders = [
            line.removeprefix("HEAD:")
            for line in result.stdout.splitlines()
            if line.removeprefix("HEAD:") not in excluded
        ]
        self.assertEqual(offenders, [])


if __name__ == "__main__":
    unittest.main()
