#!/usr/bin/env python3
"""Tool code. The tests of tools/code-tree.sh, the code hash of the test record (tools/README.md, "The test record"): a
file whose staged content differs from both its working tree and HEAD leaves the hash as it was, when the file is not
code.

    nix develop --command python3 -I tools/test_code_tree.py

The standard library only. Each test makes a new git repository under /private/tmp with a copy of the script in its
tools folder, so no test touches the index of this worktree.
"""

import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("code-tree.sh")
SCRATCH = "/private/tmp" if os.path.isdir("/private/tmp") else None
# A hook that runs these tests sets these, and they would point git at the index of this worktree.
GIT_LOCATION = ("GIT_DIR", "GIT_INDEX_FILE", "GIT_WORK_TREE", "GIT_PREFIX")


class Repository:
    """A new git repository with the script in tools/, a code file, a Markdown file in the code and a file outside it."""

    def __init__(self, root: Path):
        self.root = root
        self.env = {key: value for key, value in os.environ.items() if key not in GIT_LOCATION}
        self.env.update(GIT_AUTHOR_NAME="test", GIT_AUTHOR_EMAIL="test@example.com", GIT_COMMITTER_NAME="test",
                        GIT_COMMITTER_EMAIL="test@example.com", GIT_CONFIG_GLOBAL=os.devnull,
                        GIT_CONFIG_SYSTEM=os.devnull)
        self.git("init", "-q")
        (root / "tools").mkdir()
        shutil.copy(SCRIPT, root / "tools" / "code-tree.sh")
        self.write("tools/code.txt", "code\n")
        self.write("tools/README.md", "tool notes\n")
        self.write("notes.txt", "notes\n")
        self.git("add", "-A")
        self.git("commit", "-q", "-m", "start")

    def git(self, *args: str) -> str:
        return subprocess.run(["git", *args], cwd=self.root, env=self.env, check=True, capture_output=True,
                              text=True).stdout

    def write(self, path: str, text: str):
        (self.root / path).write_text(text)

    def stage_part_of(self, path: str):
        """Stages one edit of `path` and makes a second edit on disk, so that the index differs from disk and HEAD."""
        self.write(path, "staged edit\n")
        self.git("add", "--", path)
        self.write(path, "working tree edit\n")

    def code_tree(self, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run(["bash", "tools/code-tree.sh", *args], cwd=self.root, env=self.env, capture_output=True,
                              text=True, check=False)

    def code_hash(self, *args: str) -> str:
        run = self.code_tree(*args)
        if run.returncode != 0:
            raise AssertionError(f"code-tree.sh {' '.join(args)} failed with {run.returncode}: {run.stderr}")
        return run.stdout.splitlines()[0]


class APartlyStagedFileThatIsNotCode(unittest.TestCase):
    """The two removals of the script: the paths outside the code list, then the Markdown files inside it."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="tertius-code-tree-test.", dir=SCRATCH))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.repo = Repository(self.root)

    def test_a_partly_staged_file_outside_the_code_leaves_the_staged_hash_as_it_was(self):
        before = self.repo.code_hash("--staged")
        self.repo.stage_part_of("notes.txt")
        self.assertEqual(self.repo.code_hash("--staged"), before)

    def test_a_partly_staged_markdown_file_inside_the_code_leaves_the_staged_hash_as_it_was(self):
        before = self.repo.code_hash("--staged")
        self.repo.stage_part_of("tools/README.md")
        self.assertEqual(self.repo.code_hash("--staged"), before)

    def test_a_partly_staged_file_outside_the_code_leaves_the_working_tree_hash_as_it_was(self):
        before = self.repo.code_hash()
        self.repo.stage_part_of("notes.txt")
        self.repo.stage_part_of("tools/README.md")
        self.assertEqual(self.repo.code_hash(), before)

    def test_the_script_leaves_the_real_index_as_it_was(self):
        self.repo.stage_part_of("tools/README.md")
        staged = self.repo.git("ls-files", "--stage")
        self.repo.code_tree("--staged")
        self.repo.code_tree()
        self.assertEqual(self.repo.git("ls-files", "--stage"), staged)


if __name__ == "__main__":
    unittest.main()
