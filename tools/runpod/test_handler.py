#!/usr/bin/env python3
"""Tool code. The serverless worker's job handler (tools/runpod/handler.py): its trees, its commands and its outputs.

    python3 -I tools/runpod/test_handler.py

The standard library and git only. A local bare repository stands in for the public repository on GitHub, and the
tests run the commands in plain bash, where a worker runs them in the dev shell.
"""

import base64
import importlib.util
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("handler", Path(__file__).with_name("handler.py"))
handler = importlib.util.module_from_spec(spec)
spec.loader.exec_module(handler)

SHELL = ["bash", "-c"]
# The dev shell's files, as the image baked them.
SHELL_FILES = {"flake.nix": "{ outputs = _: { }; }\n", "flake.lock": "{}\n", "native/joltc.nix": "joltc\n"}


def git(cwd: Path, *args) -> str:
    return subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True, text=True).stdout.strip()


class Job(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        root = Path(self.folder.name)
        self.baked = root / "baked"
        for name, text in SHELL_FILES.items():
            (self.baked / name).parent.mkdir(parents=True, exist_ok=True)
            (self.baked / name).write_text(text)
        self.remote = root / "remote.git"
        git(root, "init", "-q", "--bare", str(self.remote))
        git(self.remote, "config", "uploadpack.allowFilter", "true")
        self.source = root / "source"
        git(root, "init", "-q", str(self.source))
        self.work = root / "work"
        self.main = self.commit({**SHELL_FILES, "a.txt": "main\n"})

    def tearDown(self):
        self.folder.cleanup()

    def commit(self, files: dict, ref: str | None = None) -> str:
        """A commit of exactly `files`, pushed to the stand-in for GitHub under `ref`; its id."""
        for path in self.source.rglob("*"):
            if path.is_file() and ".git" not in path.parts:
                path.unlink()
        for name, text in files.items():
            (self.source / name).parent.mkdir(parents=True, exist_ok=True)
            (self.source / name).write_text(text)
        git(self.source, "add", "-A")
        git(self.source, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "c", "--allow-empty")
        sha = git(self.source, "rev-parse", "HEAD")
        git(self.source, "push", "-q", str(self.remote), f"{sha}:{ref or self.ref(sha)}")
        return sha

    def ref(self, sha: str) -> str:
        return f"refs/gpu-jobs/2026-11-12/agent/{sha}"

    def run_job(self, **job):
        job.setdefault("protocol", handler.PROTOCOL)
        job.setdefault("repository", str(self.remote))
        job.setdefault("trees", {"main": {"sha": self.main, "ref": self.ref(self.main)}})
        job.setdefault("commands", [])
        job.setdefault("seconds", 30)
        return handler.handle(job, baked=self.baked, work=self.work, shell=SHELL,
                              limits=handler.Limits(seconds=job.pop("ceiling", 60), output_bytes=64, log_bytes=200))


class Trees(Job):
    """Each named tree holds the files of its commit, fetched from the public repository."""

    def test_each_tree_holds_the_files_of_its_own_commit(self):
        branch = self.commit({**SHELL_FILES, "a.txt": "branch\n"})
        result = self.run_job(trees={"main": {"sha": self.main, "ref": self.ref(self.main)},
                                     "branch": {"sha": branch, "ref": self.ref(branch)}},
                              commands=["cat main/a.txt branch/a.txt > both.txt"], outputs=["both.txt"])
        self.assertEqual(result["status"], "ok", result)
        self.assertEqual(base64.b64decode(result["files"]["both.txt"]).decode(), "main\nbranch\n")

    def test_a_tree_leaves_out_the_docs_and_the_tickets(self):
        sha = self.commit({**SHELL_FILES, "a.txt": "x\n", "docs/report.md": "research\n", ".tickets/t.md": "note\n"})
        result = self.run_job(trees={"t": {"sha": sha, "ref": self.ref(sha)}},
                              commands=["test -f t/a.txt && test ! -e t/docs && test ! -e t/.tickets"])
        self.assertEqual(result["status"], "ok", result["log"])

    def test_a_ref_that_names_another_commit_fails_the_job_and_runs_no_command(self):
        other = self.commit({**SHELL_FILES, "a.txt": "other\n"})
        result = self.run_job(trees={"t": {"sha": self.main, "ref": self.ref(other)}}, commands=["touch ran"])
        self.assertEqual(result["status"], "failed")
        self.assertIn(self.main, result["error"])
        self.assertFalse((self.work / "ran").exists())

    def test_a_commit_with_another_dev_shell_is_refused_before_any_command(self):
        for changed in ({"flake.lock": "{ changed }\n"}, {"native/joltc.nix": "other joltc\n"},
                        {"native/new.sh": "new\n"}):
            with self.subTest(changed=changed):
                sha = self.commit({**SHELL_FILES, **changed})
                result = self.run_job(trees={"t": {"sha": sha, "ref": self.ref(sha)}}, commands=["touch ../ran"])
                self.assertEqual(result["status"], "refused")
                self.assertIn("dev shell", result["error"])
                self.assertFalse((self.work / "ran").exists())
                self.tearDown()
                self.setUp()

    def test_a_job_of_another_protocol_is_refused_and_names_both(self):
        result = self.run_job(protocol=1)
        self.assertEqual(result["status"], "refused")
        self.assertIn("protocol 1", result["error"])
        self.assertIn(f"protocol {handler.PROTOCOL}", result["error"])

    def test_a_tree_name_that_is_not_a_plain_name_is_refused(self):
        result = self.run_job(trees={"../escape": {"sha": self.main, "ref": self.ref(self.main)}})
        self.assertEqual(result["status"], "refused")
        self.assertFalse(self.work.exists())


class Commands(Job):
    """The commands run in order, in the work folder, and stop at the first failure or at the job's time limit."""

    def test_every_command_runs_in_order_and_reports_its_exit_code(self):
        result = self.run_job(commands=["echo one >> order.txt", "echo two >> order.txt"], outputs=["order.txt"])
        self.assertEqual([step["exitCode"] for step in result["steps"]], [0, 0])
        self.assertEqual(base64.b64decode(result["files"]["order.txt"]).decode(), "one\ntwo\n")

    def test_the_first_failing_command_ends_the_job_and_the_rest_do_not_run(self):
        result = self.run_job(commands=["exit 3", "touch ran"])
        self.assertEqual(result["status"], "failed")
        self.assertEqual([step["exitCode"] for step in result["steps"]], [3])
        self.assertFalse((self.work / "ran").exists())

    def test_a_command_past_the_jobs_time_limit_is_stopped_and_the_job_says_so(self):
        result = self.run_job(commands=["sleep 30"], seconds=1)
        self.assertEqual(result["status"], "timed out")
        self.assertLess(result["steps"][0]["seconds"], 10)

    def test_the_job_limit_is_the_jobs_own_seconds(self):
        result = self.run_job(commands=["sleep 30"], seconds=1)
        self.assertIn("1 s", result["error"])

    def test_a_job_that_asks_for_more_than_the_handlers_ceiling_gets_the_ceiling(self):
        result = self.run_job(commands=["sleep 30"], seconds=3600, ceiling=1)
        self.assertEqual(result["status"], "timed out")
        self.assertIn("1 s", result["error"])

    def test_a_background_process_stops_when_its_command_ends(self):
        result = self.run_job(commands=["(sleep 47 &)", "sleep 0.5; ! pgrep -x -f 'sleep 47'"])
        self.assertEqual(result["status"], "ok", result["log"])

    def test_the_commands_do_not_see_runpods_variables(self):
        os.environ["RUNPOD_AI_API_KEY"] = "worker-key"
        try:
            result = self.run_job(commands=['test -z "${RUNPOD_AI_API_KEY:-}"'])
        finally:
            del os.environ["RUNPOD_AI_API_KEY"]
        self.assertEqual(result["status"], "ok", "a command such as env would copy the worker's key into the log")

    def test_the_log_keeps_the_end_of_the_output(self):
        result = self.run_job(commands=["for i in $(seq 1 100); do echo line-$i; done"])
        self.assertIn("line-100", result["log"])
        self.assertNotIn("line-1\n", result["log"], "200 bytes keep the end, not the start")
        self.assertLessEqual(len(result["log"].encode()), 200)


class Outputs(Job):
    """The job returns the files that its globs name, up to the output limit."""

    def test_files_past_the_output_limit_are_listed_with_their_size_instead_of_sent(self):
        result = self.run_job(commands=["printf small > a.out", "head -c 100 /dev/zero > b.out"], outputs=["*.out"])
        self.assertEqual(set(result["files"]), {"a.out"})
        self.assertEqual(result["skipped"], {"b.out": 100})

    def test_the_output_limit_counts_the_base64_size_that_the_result_carries(self):
        result = self.run_job(commands=["head -c 60 /dev/zero > c.out"], outputs=["*.out"])
        self.assertEqual(result["skipped"], {"c.out": 60}, "60 bytes take 80 in base64, past the limit of 64")

    def test_a_glob_outside_the_work_folder_is_refused(self):
        result = self.run_job(outputs=["../baked/a.txt"])
        self.assertEqual(result["status"], "refused")


if __name__ == "__main__":
    unittest.main()
