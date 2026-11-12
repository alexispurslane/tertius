#!/usr/bin/env python3
"""Tool code. The queue script of the GPU jobs (tools/runpod/gpu_job.py): its caps, its audit log, its requests to
Runpod and the endpoint that it creates. A fake stands in for Runpod's API.

    nix develop --command python3 -I tools/runpod/test_gpu_job.py

The standard library and git only.
"""

import base64
import datetime
import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("gpu_job", Path(__file__).with_name("gpu_job.py"))
gpu_job = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gpu_job)

NOW = datetime.datetime(2026, 11, 12, 12, 0, tzinfo=datetime.timezone.utc)
RATE = 0.00016
REPOSITORY = "ghcr.io/x/tertius-gpu"


class FakeRunpod:
    """Runpod's API as the queue script sees it: the billed total, and jobs that end with the given status."""

    def __init__(self, billed=0.0, final=None, image=None):
        self.billed = billed
        self.image = image
        self.final = final or {"status": "COMPLETED", "executionTime": 50_000, "delayTime": 3_000,
                               "output": {"status": "ok", "steps": [{"command": "x", "exitCode": 0, "seconds": 1.0}],
                                          "log": "done\n", "files": {}, "skipped": {}}}
        self.submitted = []
        self.cancelled = []
        self.updated = []

    def endpoint(self, endpoint):
        return {"id": endpoint, "image": self.image}

    def update_endpoint(self, endpoint, body):
        self.updated.append((endpoint, body))

    def billed_total(self):
        return self.billed

    def submit(self, endpoint, body):
        self.submitted.append((endpoint, body))
        return f"job-{len(self.submitted)}"

    def status(self, endpoint, job):
        return {"id": job, **self.final}

    def cancel(self, endpoint, job):
        self.cancelled.append(job)


class Queue(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        root = Path(self.folder.name)
        self.repo = root / "repo"
        self.repo.mkdir()
        self.git("init", "-q")
        (self.repo / "a.txt").write_text("base\n")
        self.git("add", "-A")
        self.git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "base")
        self.base = self.git("rev-parse", "HEAD").strip()
        self.state = root / "state"
        self.config = gpu_job.Config(endpoint="ep1", image_repository=REPOSITORY, rate_per_second=RATE,
                                     daily_cap_usd=10.0, total_cap_usd=30.0, job_seconds=840,
                                     endpoint_timeout_seconds=900)
        self.out = root / "out"

    def tearDown(self):
        self.folder.cleanup()

    def git(self, *args):
        return subprocess.run(["git", *args], cwd=self.repo, check=True, capture_output=True, text=True).stdout

    def submit(self, api, *, trees=None, commands=("echo hi",), seconds=None, outputs=()):
        api.image = api.image or f"{REPOSITORY}:{self.base}"
        request = gpu_job.Request(agent="agent-7", trees=trees or {"main": self.base}, commands=list(commands),
                                  outputs=list(outputs), seconds=seconds or self.config.job_seconds, out=self.out)
        return gpu_job.submit(request, config=self.config, api=api, state=self.state, repo=self.repo,
                              now=lambda: NOW, poll_seconds=0, out=lambda *_: None)

    def log_lines(self):
        path = self.state / "jobs.jsonl"
        return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []

    def worst(self, seconds):
        return (seconds + gpu_job.START_ALLOWANCE_SECONDS) * RATE

    def logged(self, cost, when=NOW):
        self.state.mkdir(parents=True, exist_ok=True)
        with (self.state / "jobs.jsonl").open("a") as log:
            log.write(json.dumps({"time": when.isoformat(), "agent": "other", "costUsd": cost}) + "\n")


class Requests(Queue):
    """What the script sends to the endpoint."""

    def test_each_tree_is_sent_as_the_binary_diff_from_the_baked_commit_to_its_ref(self):
        (self.repo / "a.txt").write_text("branch\n")
        self.git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qam", "branch")
        api = FakeRunpod()
        self.submit(api, trees={"main": self.base, "branch": "HEAD"})
        trees = api.submitted[0][1]["input"]["trees"]
        self.assertEqual(trees["main"], "", "the baked commit itself needs no patch")
        self.assertIn("+branch", base64.b64decode(trees["branch"]).decode())

    def test_the_job_names_the_baked_commit_and_carries_its_time_limit_below_the_endpoints_timeout(self):
        api = FakeRunpod()
        self.submit(api, seconds=5000)
        endpoint, body = api.submitted[0]
        self.assertEqual(endpoint, "ep1")
        self.assertEqual(body["input"]["base"], self.base)
        self.assertLessEqual(body["input"]["seconds"], self.config.job_seconds)
        self.assertLessEqual(body["policy"]["executionTimeout"], self.config.endpoint_timeout_seconds * 1000)

    def test_a_job_is_not_sent_when_the_endpoints_image_tag_names_no_commit(self):
        api = FakeRunpod(image=f"{REPOSITORY}:latest")
        with self.assertRaises(gpu_job.NoBase):
            self.submit(api)
        self.assertEqual(api.submitted, [])

    def test_the_output_files_of_a_job_are_written_under_its_out_folder(self):
        output = FakeRunpod().final["output"]
        api = FakeRunpod(final={**FakeRunpod().final, "output": {**output, "files": {
            "branch/out/gpu-timings.json": base64.b64encode(b"{}").decode()}}})
        self.submit(api)
        self.assertEqual((self.out / "branch/out/gpu-timings.json").read_bytes(), b"{}")
        self.assertIn("done", (self.out / "result.json").read_text(), "the result keeps the log and the steps")


class Caps(Queue):
    """The script refuses a job whose worst case would pass $10 in a UTC day or $30 in total, before it sends it."""

    def test_a_job_whose_worst_case_passes_the_daily_cap_is_refused_and_not_sent(self):
        self.logged(10.0 - self.worst(840) + 0.01)
        api = FakeRunpod()
        result = self.submit(api)
        self.assertEqual(result, "refused")
        self.assertEqual(api.submitted, [])
        self.assertEqual(self.log_lines()[-1]["result"], "refused", "a refusal is in the audit log too")

    def test_spending_of_an_earlier_day_does_not_count_against_todays_cap(self):
        self.logged(9.99, when=NOW - datetime.timedelta(days=1))
        api = FakeRunpod()
        self.assertEqual(self.submit(api), "ok")

    def test_the_billed_total_counts_against_the_total_cap_even_when_the_log_holds_less(self):
        api = FakeRunpod(billed=30.0 - self.worst(840) + 0.01)
        self.assertEqual(self.submit(api), "refused")
        self.assertEqual(api.submitted, [])

    def test_jobs_in_flight_count_against_the_caps_with_their_worst_case(self):
        self.logged(10.0 - 2 * self.worst(840) + 0.01)
        gpu_job.reserve(self.state, "job-in-flight", self.worst(840), when=NOW)
        api = FakeRunpod()
        self.assertEqual(self.submit(api), "refused")


class AuditLog(Queue):
    """One line per job: agent, commands, GPU seconds, cost and result."""

    def test_a_finished_job_writes_one_line_with_its_agent_commands_gpu_seconds_cost_and_result(self):
        self.submit(FakeRunpod(), commands=["dotnet build", "echo done"])
        line, = self.log_lines()
        self.assertEqual(line["agent"], "agent-7")
        self.assertEqual(line["commands"], ["dotnet build", "echo done"])
        self.assertEqual(line["gpuSeconds"], 50.0)
        self.assertAlmostEqual(line["costUsd"], 50.0 * RATE)
        self.assertEqual(line["result"], "ok")

    def test_a_finished_job_frees_its_reservation(self):
        self.submit(FakeRunpod())
        self.assertEqual(gpu_job.reserved(self.state), 0.0)

    def test_a_job_that_runpod_timed_out_still_logs_its_gpu_seconds_and_cost(self):
        api = FakeRunpod(final={"status": "TIMED_OUT", "executionTime": 900_000, "delayTime": 1_000})
        self.assertEqual(self.submit(api), "TIMED_OUT")
        line, = self.log_lines()
        self.assertEqual(line["result"], "TIMED_OUT")
        self.assertAlmostEqual(line["costUsd"], 900.0 * RATE)

    def test_a_job_that_runpod_marks_failed_for_the_handlers_error_keeps_the_handlers_result(self):
        output = {**FakeRunpod().final["output"], "status": "failed", "error": "command 1 failed with exit code 2",
                  "steps": [{"command": "x", "exitCode": 2, "seconds": 1.0}]}
        api = FakeRunpod(final={**FakeRunpod().final, "status": "FAILED", "output": output})
        self.assertEqual(self.submit(api), "failed")
        result = json.loads((self.out / "result.json").read_text())
        self.assertEqual(result["runpodStatus"], "FAILED", "the result file keeps Runpod's status beside the handler's")
        self.assertEqual(result["status"], "failed")

    def test_a_failed_command_is_the_jobs_result(self):
        output = {**FakeRunpod().final["output"], "status": "failed",
                  "steps": [{"command": "x", "exitCode": 2, "seconds": 1.0}]}
        api = FakeRunpod(final={**FakeRunpod().final, "output": output})
        self.assertEqual(self.submit(api), "failed")
        self.assertEqual(self.log_lines()[0]["exitCodes"], [2])


class Endpoint(unittest.TestCase):
    """The endpoint that the script creates bounds the spending by itself too, and runs the image of one commit."""

    config = gpu_job.Config(endpoint="ep1", image_repository=REPOSITORY, rate_per_second=RATE, daily_cap_usd=10.0,
                            total_cap_usd=30.0, job_seconds=840, endpoint_timeout_seconds=900)

    def test_the_endpoint_runs_at_most_six_workers_with_the_execution_timeout_on_the_rtx_2000_ada_only(self):
        body = gpu_job.endpoint_request(self.config, "a" * 40)
        self.assertEqual(body["workers"]["max"], 6)
        self.assertEqual(body["workers"]["min"], 0, "no worker runs, and bills, without a job")
        self.assertEqual(body["timeout"], 900_000)
        self.assertEqual(body["gpu"]["pools"], ["AMPERE_16"])
        self.assertNotIn("NVIDIA RTX 2000 Ada Generation", body["gpu"]["excludedTypes"])
        self.assertEqual(len(body["gpu"]["excludedTypes"]), 3, "the pool's other three cards are excluded")
        self.assertEqual(body["image"], f"{REPOSITORY}:{'a' * 40}")

    def test_an_image_change_sends_only_the_image_of_the_named_commit(self):
        api = FakeRunpod()
        gpu_job.use_image(self.config, api, "b" * 40, pullable=lambda image: True)
        self.assertEqual(api.updated, [("ep1", {"image": f"{REPOSITORY}:{'b' * 40}"})])

    def test_an_image_that_the_registry_does_not_serve_without_a_login_is_not_used(self):
        api = FakeRunpod()
        with self.assertRaises(gpu_job.NoBase):
            gpu_job.use_image(self.config, api, "b" * 40, pullable=lambda image: False)
        self.assertEqual(api.updated, [])


if __name__ == "__main__":
    unittest.main()
