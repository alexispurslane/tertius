#!/usr/bin/env python3
"""Tool code. The serverless worker's job handler (tools/runpod/handler.py): its trees, its commands and its outputs.

    nix develop --command python3 -I tools/runpod/test_handler.py

The standard library and git only. The tests run the commands in plain bash, where a worker runs them in the dev shell.
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

BASE = "1111111111111111111111111111111111111111"
SHELL = ["bash", "-c"]


def patch(before: dict, after: dict) -> str:
    """The base64 of `git diff --binary` from the files `before` to the files `after`, as gpu_job.py sends it."""
    with tempfile.TemporaryDirectory() as folder:
        repo = Path(folder)
        git = lambda *args: subprocess.run(["git", *args], cwd=repo, check=True, capture_output=True).stdout
        git("init", "-q")
        for name, text in before.items():
            (repo / name).write_text(text)
        git("add", "-A")
        git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "before", "--allow-empty")
        for name in before:
            (repo / name).unlink()
        for name, text in after.items():
            (repo / name).write_text(text)
        git("add", "-A")
        return base64.b64encode(git("diff", "--binary", "--cached")).decode()


class Job(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        root = Path(self.folder.name)
        self.baked = root / "baked"
        self.baked.mkdir()
        (self.baked / "BAKED_COMMIT").write_text(BASE + "\n")
        (self.baked / "a.txt").write_text("base\n")
        self.work = root / "work"

    def tearDown(self):
        self.folder.cleanup()

    def run_job(self, **job):
        job.setdefault("base", BASE)
        job.setdefault("trees", {"main": ""})
        job.setdefault("commands", [])
        job.setdefault("seconds", 30)
        return handler.handle(job, baked=self.baked, work=self.work, shell=SHELL,
                              limits=handler.Limits(seconds=job.pop("ceiling", 60), output_bytes=64, log_bytes=200))


class Trees(Job):
    """Each named tree is a copy of the baked source with its own patch."""

    def test_each_tree_holds_the_baked_source_with_its_own_patch_applied(self):
        result = self.run_job(trees={"main": "", "branch": patch({"a.txt": "base\n"}, {"a.txt": "branch\n"})},
                              commands=["cat main/a.txt branch/a.txt > both.txt"], outputs=["both.txt"])
        self.assertEqual(result["status"], "ok", result)
        self.assertEqual(base64.b64decode(result["files"]["both.txt"]).decode(), "base\nbranch\n")

    def test_a_patch_can_add_a_file(self):
        result = self.run_job(trees={"t": patch({"a.txt": "base\n"}, {"a.txt": "base\n", "new.txt": "added\n"})},
                              commands=["cat t/new.txt > out.txt"], outputs=["out.txt"])
        self.assertEqual(base64.b64decode(result["files"]["out.txt"]).decode(), "added\n")

    def test_a_patch_that_does_not_apply_fails_the_job_with_gits_message_and_runs_no_command(self):
        result = self.run_job(trees={"t": patch({"a.txt": "other\n"}, {"a.txt": "changed\n"})},
                              commands=["touch ran"])
        self.assertEqual(result["status"], "failed")
        self.assertIn("a.txt", result["error"])
        self.assertFalse((self.work / "ran").exists())

    def test_a_job_for_another_base_is_refused_before_any_tree_is_made(self):
        result = self.run_job(base="2" * 40, commands=["touch ran"])
        self.assertEqual(result["status"], "refused")
        self.assertIn(BASE, result["error"], "the error names the commit that the image holds")
        self.assertFalse(self.work.exists())

    def test_a_tree_name_that_is_not_a_plain_name_is_refused(self):
        result = self.run_job(trees={"../escape": ""})
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
        self.assertEqual(result["status"], "timed out")
        self.assertIn("1 s", result["error"])

    def test_a_job_that_asks_for_more_than_the_handlers_ceiling_gets_the_ceiling(self):
        result = self.run_job(commands=["sleep 30"], seconds=3600, ceiling=1)
        self.assertEqual(result["status"], "timed out")
        self.assertIn("1 s", result["error"])

    def test_a_background_process_stops_when_its_command_ends(self):
        result = self.run_job(commands=["(sleep 47 &)", "sleep 0.5; ! pgrep -f 'sleep 47'"])
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
