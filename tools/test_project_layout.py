#!/usr/bin/env python3
"""The canonical agent configuration and the home of Tertius design documents."""

import unittest
from pathlib import Path

ROOT = Path(__file__).parents[1]


class ProjectLayout(unittest.TestCase):
    def test_agents_is_the_canonical_agent_configuration(self):
        agents = (ROOT / "AGENTS.md").read_text()
        claude = (ROOT / "CLAUDE.md").read_text()

        self.assertIn("This file is the canonical configuration for agents on Tertius", agents)
        self.assertIn("## Interpreting code quality metrics", agents)
        self.assertIn("[AGENTS.md](AGENTS.md) is the canonical configuration for agents", claude)
        self.assertNotIn("## Workflow", claude)

    def test_design_documents_live_only_in_the_github_wiki(self):
        self.assertFalse((ROOT / "docs/plans").exists())
        self.assertIn("https://github.com/alexispurslane/tertius/wiki", (ROOT / "README.md").read_text())
        self.assertNotIn("docs/plans", (ROOT / "docfx.json").read_text())


if __name__ == "__main__":
    unittest.main()
