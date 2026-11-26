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
        self.assertFalse((ROOT / "docs/plans").exists())
        self.assertIn("https://github.com/alexispurslane/tertius/wiki", (ROOT / "README.md").read_text())
        self.assertNotIn("docs/plans", (ROOT / "docfx.json").read_text())

        excluded = {".agents/skills/tertius-issues/SKILL.md", "tools/test_project_layout.py"}
        offenders = []
        for relative in subprocess.check_output(["git", "-C", str(ROOT), "ls-files"], text=True).splitlines():
            if relative in excluded:
                continue
            path = ROOT / relative
            if path.is_file() and "docs/plans" in path.read_text(errors="ignore"):
                offenders.append(relative)
        self.assertEqual(offenders, [])


if __name__ == "__main__":
    unittest.main()
