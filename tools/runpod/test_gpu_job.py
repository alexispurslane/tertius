#!/usr/bin/env python3
"""Tool code. The queue script of the GPU jobs (tools/runpod/gpu_job.py): its caps, its audit log, the commits that
it publishes, the endpoint that it picks or creates for a job's dev shell, and its requests to Runpod. Fakes stand in
for Runpod's API, the image registry, GitHub and the secrets scan.

    python3 -I tools/runpod/test_gpu_job.py

The standard library and git only.
"""

import base64
import datetime
import importlib.util
import json
import signal
import subprocess
import tempfile
import unittest
import urllib.error
from pathlib import Path

spec = importlib.util.spec_from_file_location("gpu_job", Path(__file__).with_name("gpu_job.py"))
gpu_job = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gpu_job)

NOW = datetime.datetime(2026, 11, 12, 12, 0, tzinfo=datetime.timezone.utc)
RATE = 0.00016
CPU_RATE = 0.00002
QUOTA = 10  # the account's workers, summed over the maximums of all its endpoints
REPOSITORY = "ghcr.io/x/tertius-gpu"
SHELL_FILES = {"flake.nix": "{ outputs = _: { }; }\n", "flake.lock": "{}\n", "native/joltc.nix": "joltc\n"}
HANDLER = {"tools/runpod/handler.py": "PROTOCOL = 3\n"}


def config(repository="https://github.com/x/tertius.git"):
    return gpu_job.Config(image_repository=REPOSITORY, repository=repository, daily_caps_usd={"2026-01-01": 10.0},
                          total_cap_usd=30.0,
                          kinds={"gpu": gpu_job.Kind(rate_per_second=RATE, workers=6, job_seconds=840,
                                                     endpoint_timeout_seconds=900),
                                 "cpu": gpu_job.Kind(rate_per_second=CPU_RATE, workers=4, job_seconds=7140,
                                                     endpoint_timeout_seconds=7200)})


class FakeRunpod:
    """Runpod's API as the queue script sees it: the bill, the endpoints, and jobs that end with the given status."""

    def __init__(self, billed=0.0, final=None, billed_today=0.0, statuses=None, endpoints=None, hours=None):
        self.billed = billed
        self.today = billed_today
        self.hours = hours or {}  # the start of a UTC hour -> its bill, for the last hours
        self.statuses = statuses or {}  # job id -> its status, or an exception that the status call raises
        self.final = final or {"status": "COMPLETED", "executionTime": 50_000, "delayTime": 3_000,
                               "output": {"status": "ok", "steps": [{"command": "x", "exitCode": 0, "seconds": 1.0}],
                                          "log": "done\n", "files": {}, "skipped": {}}}
        self.live = list(endpoints or [])
        self.running = {}  # endpoint id -> its running workers
        self.listings = 0
        self.stop_at_listing = None  # (n, endpoint id): the watchdog stops that endpoint before the n-th listing
        self.submitted = []
        self.cancelled = []
        self.created = []
        self.deleted = []
        self.updated = []

    def billed_total(self):
        return self.billed

    def billed_today(self):
        return self.today

    def billed_hours(self, count):
        return dict(self.hours)

    def endpoints(self):
        self.listings += 1
        if self.stop_at_listing and self.stop_at_listing[0] == self.listings:
            for endpoint in self.live:
                if endpoint["id"] == self.stop_at_listing[1]:
                    endpoint["workers"] = {**endpoint["workers"], "max": 0}
        return [dict(endpoint, workers=dict(endpoint["workers"])) for endpoint in self.live]

    def create_endpoint(self, body):
        held = sum(live["workers"]["max"] for live in self.live)
        if held + body["workers"]["max"] > QUOTA:
            raise urllib.error.HTTPError("url", 400, "Max workers across all endpoints must not exceed your workers "
                                         "quota", {}, None)
        endpoint = {"id": f"ep-new-{len(self.created) + 1}", "name": body["name"], "image": body["image"],
                    "workers": body["workers"]}
        self.created.append(body)
        self.live.append(endpoint)
        return endpoint

    def update_endpoint(self, endpoint, body):
        self.updated.append((endpoint, body))
        for live in self.live:
            if live["id"] == endpoint:
                if sum(other["workers"]["max"] for other in self.live if other is not live) + \
                        body["workers"]["max"] > QUOTA:
                    raise urllib.error.HTTPError("url", 400, "quota", {}, None)
                live["workers"] = {**live["workers"], **body["workers"]}

    def delete_endpoint(self, endpoint):
        self.deleted.append(endpoint)
        self.live = [live for live in self.live if live["id"] != endpoint]

    def workers(self, endpoint):
        return self.running.get(endpoint, [])

    submit_error = None

    def submit(self, endpoint, body):
        if self.submit_error:
            raise self.submit_error
        self.submitted.append((endpoint, body))
        return f"job-{len(self.submitted)}"

    def status(self, endpoint, job):
        status = self.statuses.get(job, self.final)
        if isinstance(status, Exception):
            raise status
        return {"id": job, **status}

    def cancel(self, endpoint, job):
        self.cancelled.append(job)


class FakeRegistry:
    """The public images, one tag per baked commit."""

    def __init__(self, commits):
        self.commits = list(commits)

    def images(self):
        return list(self.commits)


class FakeGitHub:
    """The public repository: the hidden refs that the script pushes, lists and deletes."""

    def __init__(self, refs=(), known=()):
        self.pushed = []
        self.live = list(refs)
        self.deleted = []
        self.known = set(known)  # the commits that GitHub's branches and tags already hold

    def new_commits(self, shas):
        return [sha for sha in shas if sha not in self.known]

    def push(self, sha, ref):
        self.pushed.append((sha, ref))
        self.live.append(ref)

    def refs(self):
        return list(self.live)

    def delete(self, refs):
        self.deleted.extend(refs)
        self.live = [ref for ref in self.live if ref not in refs]


class Queue(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        root = Path(self.folder.name)
        self.repo = root / "repo"
        self.repo.mkdir()
        self.git("init", "-q")
        self.base = self.commit({**SHELL_FILES, **HANDLER, "a.txt": "base\n"})
        self.on_main(self.base)
        self.state = root / "state"
        self.config = config()
        self.out = root / "out"
        self.findings = []
        self.scanned = []

    def tearDown(self):
        self.folder.cleanup()

    def git(self, *args):
        return subprocess.run(["git", *args], cwd=self.repo, check=True, capture_output=True, text=True).stdout

    def commit(self, files: dict) -> str:
        """A commit whose files are exactly `files`; its id. Each commit is one second younger than the last."""
        for path in self.repo.rglob("*"):
            if path.is_file() and ".git" not in path.parts:
                path.unlink()
        for name, text in files.items():
            (self.repo / name).parent.mkdir(parents=True, exist_ok=True)
            (self.repo / name).write_text(text)
        self.git("add", "-A")
        self.clock = getattr(self, "clock", 1_700_000_000) + 1
        date = f"{self.clock} +0000"
        subprocess.run(["git", "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "c", "--allow-empty"],
                       cwd=self.repo, check=True, env={"GIT_COMMITTER_DATE": date, "GIT_AUTHOR_DATE": date,
                                                       "PATH": "/usr/bin:/bin:/usr/local/bin:/opt/homebrew/bin"})
        return self.git("rev-parse", "HEAD").strip()

    def on_main(self, sha):
        """Main as the repository last saw it on GitHub."""
        self.git("update-ref", gpu_job.MAIN, sha)

    def endpoint(self, commit, id="ep1", most=6, kind=None):
        name = f"tertius-{kind}-{commit[:12]}" if kind else "tertius-gpu"
        return {"id": id, "name": name, "image": f"{REPOSITORY}:{commit}", "workers": {"min": 0, "max": most}}

    def api(self, **options):
        options.setdefault("endpoints", [self.endpoint(self.base)])
        return FakeRunpod(**options)

    def scan(self, repo, shas):
        self.scanned.append(list(shas))
        return list(self.findings)

    def submit(self, api, *, trees=None, commands=("echo hi",), seconds=None, outputs=(), images=None, github=None,
               kind="gpu"):
        self.github = github or FakeGitHub()
        request = gpu_job.Request(agent="agent-7", trees=trees or {"main": self.base}, commands=list(commands),
                                  outputs=list(outputs), seconds=seconds or self.config.kinds[kind].job_seconds,
                                  out=self.out, kind=kind)
        return gpu_job.submit(request, config=self.config, api=api, registry=FakeRegistry(images or [self.base]),
                              github=self.github, scan=self.scan, state=self.state, repo=self.repo,
                              now=lambda: NOW, poll_seconds=0, out=lambda *_: None)

    def log_lines(self):
        path = self.state / "jobs.jsonl"
        return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []

    def worst(self, seconds):
        return (seconds + gpu_job.START_ALLOWANCE_SECONDS + gpu_job.IDLE_SECONDS) * RATE

    def pending(self, name, *, job=None, when=NOW, expires=3600, endpoint="ep1"):
        """A job in flight that another run of the script sent."""
        gpu_job.write_pending(self.state, name, {"time": when.isoformat(), "agent": "other", "jobId": job,
                                                 "endpoint": endpoint, "worstCaseUsd": self.worst(840),
                                                 "expires": (when + datetime.timedelta(seconds=expires)).isoformat()})

    def logged(self, cost, when=NOW, ended=None):
        self.state.mkdir(parents=True, exist_ok=True)
        line = {"time": when.isoformat(), "agent": "other", "costUsd": cost}
        if ended is not None:
            line["ended"] = ended.isoformat()
        with (self.state / "jobs.jsonl").open("a") as log:
            log.write(json.dumps(line) + "\n")


class Requests(Queue):
    """What the script publishes and sends."""

    def test_each_tree_is_sent_as_its_commit_under_a_hidden_ref_that_is_pushed_before_the_job(self):
        branch = self.commit({**SHELL_FILES, **HANDLER, "a.txt": "branch\n"})
        api = self.api()
        self.submit(api, trees={"main": self.base, "branch": branch})
        trees = api.submitted[0][1]["input"]["trees"]
        self.assertEqual({name: tree["sha"] for name, tree in trees.items()}, {"main": self.base, "branch": branch})
        self.assertEqual(sorted(self.github.pushed), sorted((tree["sha"], tree["ref"]) for tree in trees.values()))
        self.assertEqual(trees["branch"]["ref"], f"refs/gpu-jobs/2026-11-12/agent-7/{branch}")

    def test_the_job_carries_the_protocol_and_the_public_repository(self):
        api = self.api()
        self.submit(api)
        job = api.submitted[0][1]["input"]
        self.assertEqual(job["protocol"], gpu_job.PROTOCOL)
        self.assertEqual(job["repository"], self.config.repository)

    def test_the_job_carries_its_time_limit_below_the_endpoints_timeout(self):
        api = self.api()
        self.submit(api, seconds=5000)
        endpoint, body = api.submitted[0]
        self.assertEqual(endpoint, "ep1")
        self.assertLessEqual(body["input"]["seconds"], self.config.kinds["gpu"].job_seconds)
        self.assertLessEqual(body["policy"]["executionTimeout"], self.config.kinds["gpu"].endpoint_timeout_seconds * 1000)

    def test_the_output_files_of_a_job_are_written_under_its_out_folder(self):
        output = FakeRunpod().final["output"]
        api = self.api(final={**FakeRunpod().final, "output": {**output, "files": {
            "branch/out/gpu-timings.json": base64.b64encode(b"{}").decode()}}})
        self.submit(api)
        self.assertEqual((self.out / "branch/out/gpu-timings.json").read_bytes(), b"{}")
        self.assertIn("done", (self.out / "result.json").read_text(), "the result keeps the log and the steps")


class Secrets(Queue):
    """A job's commits become public, so a secret in them stops the job before anything leaves this Mac."""

    def test_a_finding_of_the_scan_refuses_the_job_and_pushes_and_sends_nothing(self):
        self.findings = ["generic-api-key in a.txt"]
        api = self.api()
        self.assertEqual(self.submit(api), "refused")
        self.assertEqual((self.github.pushed, api.submitted), ([], []))
        self.assertIn("generic-api-key", self.log_lines()[-1]["error"])

    def test_the_scan_sees_every_commit_of_the_job_that_github_does_not_hold(self):
        branch = self.commit({**SHELL_FILES, **HANDLER, "a.txt": "branch\n"})
        self.submit(self.api(), trees={"main": self.base, "branch": branch}, github=FakeGitHub(known=[self.base]))
        self.assertEqual(self.scanned[0], [branch])

    def test_a_scan_that_cannot_run_refuses_the_job_in_the_audit_log(self):
        def broken(repo, shas):
            raise subprocess.CalledProcessError(1, ["nix", "shell"], stderr="cannot fetch gitleaks")
        self.scan = broken
        api = self.api()
        self.assertEqual(self.submit(api), "refused")
        self.assertEqual((api.submitted, self.github.pushed), ([], []))
        self.assertIn("cannot fetch gitleaks", self.log_lines()[-1]["error"])
        self.assertEqual(list((self.state / "pending").glob("*")), [], "a refused job leaves no file in flight")

    def test_a_tree_whose_ref_names_no_commit_is_refused_in_the_audit_log(self):
        api = self.api()
        self.assertEqual(self.submit(api, trees={"main": "no-such-branch"}), "refused")
        self.assertEqual((api.submitted, self.github.pushed), ([], []))
        self.assertIn("no-such-branch", self.log_lines()[-1]["error"])

    def test_hidden_refs_older_than_the_three_days_before_today_are_deleted(self):
        old = f"refs/gpu-jobs/2026-11-08/a/{self.base}"
        last = f"refs/gpu-jobs/2026-11-09/a/{self.base}"
        self.submit(self.api(), github=FakeGitHub(refs=[old, last]))
        self.assertEqual(self.github.deleted, [old], "the refs of today and of the three days before it stay")


class DevShells(Queue):
    """A job runs only on an endpoint whose image has the dev shell of its commits."""

    def test_a_job_runs_on_the_endpoint_whose_image_has_its_dev_shell(self):
        branch = self.commit({**SHELL_FILES, **HANDLER, "a.txt": "branch\n"})
        api = self.api()
        self.submit(api, trees={"main": self.base, "branch": branch})
        self.assertEqual((api.submitted[0][0], api.created), ("ep1", []), "a change outside the shell needs no image")

    def test_a_job_of_a_new_dev_shell_gets_a_new_endpoint_of_its_image_and_the_idle_old_one_goes(self):
        shell = self.commit({**SHELL_FILES, **HANDLER, "flake.lock": "{ newer }\n"})
        api = self.api()
        self.submit(api, trees={"main": shell}, images=[self.base, shell])
        created, = api.created
        self.assertEqual(created["image"], f"{REPOSITORY}:{shell}")
        self.assertEqual(api.submitted[0][0], "ep-new-1")
        self.assertEqual(api.deleted, ["ep1"])
        live, = api.live
        self.assertEqual((live["workers"]["min"], live["workers"]["max"]), (0, 6), "the old endpoint's workers came back")

    def test_a_new_endpoint_beside_a_busy_one_gets_the_workers_that_the_quota_leaves(self):
        shell = self.commit({**SHELL_FILES, **HANDLER, "flake.lock": "{ newer }\n"})
        self.pending("running", job="job-running", endpoint="ep1")
        api = self.api(statuses={"job-running": {"status": "IN_PROGRESS"}})
        self.assertEqual(self.submit(api, trees={"main": shell}, images=[self.base, shell]), "ok")
        self.assertEqual({live["id"]: live["workers"]["max"] for live in api.live}, {"ep1": 6, "ep-new-1": QUOTA - 6})

    def test_the_next_job_after_the_old_endpoint_goes_gives_the_new_one_its_six_workers(self):
        shell = self.commit({**SHELL_FILES, **HANDLER, "flake.lock": "{ newer }\n"})
        api = self.api(endpoints=[self.endpoint(self.base), self.endpoint(shell, id="ep2", most=4)])
        self.submit(api, trees={"main": shell}, images=[self.base, shell])
        self.assertEqual({live["id"]: live["workers"]["max"] for live in api.live}, {"ep2": 6})

    def test_a_job_runs_on_an_older_fitting_endpoint_while_the_quota_has_no_room_for_the_newest_image(self):
        middle = self.commit({**SHELL_FILES, "tools/runpod/handler.py": "PROTOCOL = 3\n# fix 1\n"})
        newest = self.commit({**SHELL_FILES, "tools/runpod/handler.py": "PROTOCOL = 3\n# fix 2\n"})
        self.on_main(newest)
        self.pending("running", job="job-running", endpoint="ep1")
        self.pending("also-running", job="job-also-running", endpoint="ep2")
        api = self.api(endpoints=[self.endpoint(self.base), self.endpoint(middle, id="ep2", most=4)],
                       statuses={"job-running": {"status": "IN_PROGRESS"}, "job-also-running": {"status": "IN_QUEUE"}})
        self.assertEqual(self.submit(api, trees={"main": newest}, images=[self.base, middle, newest]), "ok")
        self.assertEqual((api.submitted[0][0], api.created), ("ep2", []))
        self.assertEqual(self.log_lines()[-1]["image"], middle)

    def test_idle_endpoints_of_an_old_dev_shell_that_hold_the_quota_go_before_the_new_one_is_made(self):
        middle = self.commit({**SHELL_FILES, "tools/runpod/handler.py": "PROTOCOL = 3\n# fix 1\n"})
        shell = self.commit({**SHELL_FILES, **HANDLER, "flake.lock": "{ newer }\n"})
        api = self.api(endpoints=[self.endpoint(self.base), self.endpoint(middle, id="ep2", most=4)])
        self.assertEqual(self.submit(api, trees={"main": shell}, images=[self.base, middle, shell]), "ok")
        self.assertEqual(sorted(api.deleted), ["ep1", "ep2"])
        self.assertEqual([(live["id"], live["workers"]["max"]) for live in api.live], [("ep-new-1", 6)])

    def test_an_endpoint_that_the_watchdog_stops_before_it_grows_is_not_grown(self):
        api = self.api(endpoints=[self.endpoint(self.base, most=4)])
        api.stop_at_listing = (3, "ep1")
        self.assertEqual(self.submit(api), "refused")
        self.assertEqual((api.updated, api.submitted), ([], []))

    def test_the_live_endpoint_grows_only_into_the_room_that_other_endpoints_leave(self):
        other = {"id": "cpu", "name": "someone-else", "image": "x", "workers": {"min": 0, "max": 2}}
        api = self.api(endpoints=[self.endpoint(self.base, most=4), other])
        self.submit(api)
        self.assertEqual({live["id"]: live["workers"]["max"] for live in api.live}, {"ep1": 6, "cpu": 2})

    def test_a_job_that_the_caps_refuse_still_logs_the_image_that_it_would_have_used(self):
        self.logged(10.0)
        self.assertEqual(self.submit(self.api()), "refused")
        self.assertEqual(self.log_lines()[-1]["image"], self.base)

    def test_a_job_is_refused_when_other_endpoints_hold_the_whole_worker_quota(self):
        shell = self.commit({**SHELL_FILES, **HANDLER, "flake.lock": "{ newer }\n"})
        self.pending("running", job="job-running", endpoint="ep1")
        other = {"id": "cpu", "name": "someone-else", "image": "x", "workers": {"min": 0, "max": 4}}
        api = self.api(endpoints=[self.endpoint(self.base), other],
                       statuses={"job-running": {"status": "IN_PROGRESS"}})
        self.assertEqual(self.submit(api, trees={"main": shell}, images=[self.base, shell]), "refused")
        self.assertEqual((api.created, api.submitted), ([], []))
        self.assertIn("worker quota", self.log_lines()[-1]["error"])

    def test_an_endpoint_with_a_job_in_flight_stays(self):
        shell = self.commit({**SHELL_FILES, **HANDLER, "flake.lock": "{ newer }\n"})
        self.pending("running", job="job-running", endpoint="ep1")
        api = self.api(statuses={"job-running": {"status": "IN_PROGRESS"}})
        self.submit(api, trees={"main": shell}, images=[self.base, shell])
        self.assertEqual(api.deleted, [])

    def test_an_endpoint_with_a_running_worker_stays(self):
        shell = self.commit({**SHELL_FILES, **HANDLER, "flake.lock": "{ newer }\n"})
        api = self.api()
        api.running["ep1"] = [{"id": "w1", "status": "RUNNING"}]
        self.submit(api, trees={"main": shell}, images=[self.base, shell])
        self.assertEqual(api.deleted, [])

    def test_a_job_of_a_dev_shell_without_an_image_is_refused_and_names_the_missing_image(self):
        shell = self.commit({**SHELL_FILES, **HANDLER, "native/joltc.nix": "joltc 2\n"})
        api = self.api()
        self.assertEqual(self.submit(api, trees={"main": shell}), "refused")
        self.assertEqual((api.submitted, api.created, self.github.pushed), ([], [], []))
        self.assertIn(f"{REPOSITORY}:{shell}", self.log_lines()[-1]["error"])

    def test_trees_of_two_dev_shells_are_refused(self):
        shell = self.commit({**SHELL_FILES, **HANDLER, "flake.nix": "{ other }\n"})
        api = self.api()
        self.assertEqual(self.submit(api, trees={"main": self.base, "branch": shell}, images=[self.base, shell]),
                         "refused")
        self.assertIn("dev shell", self.log_lines()[-1]["error"])

    def test_the_newest_image_of_the_dev_shell_replaces_an_older_one(self):
        fixed = self.commit({**SHELL_FILES, "tools/runpod/handler.py": "PROTOCOL = 3\n# a fix\n"})
        api = self.api()
        self.submit(api, trees={"main": fixed}, images=[self.base, fixed])
        self.assertEqual(api.created[0]["image"], f"{REPOSITORY}:{fixed}")

    def test_an_image_of_another_protocol_is_never_used(self):
        old = self.commit({**SHELL_FILES, "tools/runpod/handler.py": "PROTOCOL = 1\n"})
        api = self.api(endpoints=[self.endpoint(old)])
        self.submit(api, images=[old, self.base])
        self.assertEqual(api.created[0]["image"], f"{REPOSITORY}:{self.base}")
        self.assertEqual(api.deleted, ["ep1"])

    def test_an_image_of_a_side_branch_does_not_replace_the_image_that_the_jobs_commits_descend_from(self):
        side = self.commit({**SHELL_FILES, "tools/runpod/handler.py": "PROTOCOL = 3\n# an experiment\n"})
        self.git("checkout", "-q", "--detach", self.base)
        main = self.commit({**SHELL_FILES, **HANDLER, "a.txt": "main moves on\n"})
        self.on_main(main)
        api = self.api()
        self.submit(api, trees={"main": main}, images=[self.base, side])
        self.assertEqual((api.submitted[0][0], api.created, api.deleted), ("ep1", [], []))

    def test_a_job_from_an_older_commit_of_main_runs_on_mains_newest_image_with_the_other_jobs(self):
        newer = self.commit({**SHELL_FILES, "tools/runpod/handler.py": "PROTOCOL = 3\n# a fix on main\n"})
        self.on_main(newer)
        api = self.api(endpoints=[self.endpoint(newer)])
        self.submit(api, trees={"main": self.base}, images=[self.base, newer])
        self.assertEqual((api.submitted[0][0], api.created, api.deleted), ("ep1", [], []))

    def test_a_stop_of_the_new_endpoint_seen_after_its_creation_refuses_the_job_and_removes_it(self):
        shell = self.commit({**SHELL_FILES, **HANDLER, "flake.lock": "{ newer }\n"})
        api = self.api()
        api.stop_at_listing = (5, "ep-new-1")
        self.assertEqual(self.submit(api, trees={"main": shell}, images=[self.base, shell]), "refused")
        self.assertEqual((api.created[0]["image"], api.live, api.submitted), (f"{REPOSITORY}:{shell}", [], []))

    def test_an_endpoint_that_the_script_did_not_name_is_never_deleted(self):
        shell = self.commit({**SHELL_FILES, **HANDLER, "flake.lock": "{ newer }\n"})
        mine = {**self.endpoint(self.base, id="ep-hand", most=2), "name": "tertius-gpu-by-hand"}
        api = self.api(endpoints=[self.endpoint(self.base), mine])
        self.submit(api, trees={"main": shell}, images=[self.base, shell])
        self.assertEqual(api.deleted, ["ep1"])

    def test_a_job_whose_own_run_still_waits_for_it_keeps_its_endpoint_and_its_entry(self):
        shell = self.commit({**SHELL_FILES, **HANDLER, "flake.lock": "{ newer }\n"})
        self.pending("waiting", job="job-done", endpoint="ep1")
        api = self.api(statuses={"job-done": {"status": "COMPLETED", "executionTime": 1_000}})
        with gpu_job.owning(self.state, "waiting"):
            self.submit(api, trees={"main": shell}, images=[self.base, shell])
        self.assertEqual(api.deleted, [], "its run still fetches the result from ep1")
        self.assertEqual([held["jobId"] for held in gpu_job.in_flight(self.state)], ["job-done"])

    def test_an_endpoint_that_the_watchdog_stops_during_the_routing_is_kept_and_the_job_refused(self):
        shell = self.commit({**SHELL_FILES, **HANDLER, "flake.lock": "{ newer }\n"})
        api = self.api()
        api.stop_at_listing = (2, "ep1")
        self.assertEqual(self.submit(api, trees={"main": shell}, images=[self.base, shell]), "refused")
        self.assertEqual(api.submitted, [])
        self.assertEqual([endpoint["id"] for endpoint in api.live], ["ep1"], "the stop stays, and no endpoint "
                         "of 6 workers is left beside it")
        self.assertIn("0 workers", self.log_lines()[-1]["error"])

    def test_an_endpoint_stopped_at_zero_workers_refuses_every_job_and_creates_no_endpoint(self):
        api = self.api(endpoints=[self.endpoint(self.base, most=0)])
        self.assertEqual(self.submit(api), "refused")
        self.assertEqual((api.submitted, api.created), ([], []))
        self.assertIn("0 workers", self.log_lines()[-1]["error"])


class Caps(Queue):
    """The script refuses a job whose worst case would pass $10 in a UTC day or $30 in total, before it sends it."""

    def test_a_job_whose_worst_case_passes_the_daily_cap_is_refused_and_not_sent(self):
        self.logged(10.0 - self.worst(840) + 0.01)
        api = self.api()
        result = self.submit(api)
        self.assertEqual(result, "refused")
        self.assertEqual(api.submitted, [])
        self.assertEqual(self.log_lines()[-1]["result"], "refused", "a refusal is in the audit log too")

    def test_each_utc_day_has_the_cap_of_the_latest_date_on_or_before_it(self):
        self.config = gpu_job.Config(**{**vars(self.config), "daily_caps_usd": {"2026-11-11": 10.0, "2026-11-13": 3.0}})
        self.logged(2.99)
        self.assertEqual(self.submit(self.api()), "ok", "on 2026-11-12 the cap is still $10, though $3 would refuse")
        tomorrow = gpu_job.Config.daily_cap(self.config, datetime.date(2026, 11, 13))
        self.assertEqual(tomorrow, 3.0)

    def test_spending_of_an_earlier_day_does_not_count_against_todays_cap(self):
        self.logged(9.99, when=NOW - datetime.timedelta(days=1))
        api = self.api()
        self.assertEqual(self.submit(api), "ok")

    def test_the_billed_total_counts_against_the_total_cap_even_when_the_log_holds_less(self):
        api = self.api(billed=30.0 - self.worst(840) + 0.01)
        self.assertEqual(self.submit(api), "refused")
        self.assertEqual(api.submitted, [])

    def test_jobs_in_flight_count_against_the_caps_with_their_worst_case(self):
        self.logged(10.0 - 2 * self.worst(840) + 0.01)
        self.pending("in-flight", job="job-running")
        api = self.api(statuses={"job-running": {"status": "IN_PROGRESS"}})
        self.assertEqual(self.submit(api), "refused")

    def test_todays_bill_counts_against_the_daily_cap_even_when_the_log_holds_less(self):
        api = self.api(billed_today=10.0 - self.worst(840) + 0.01)
        self.assertEqual(self.submit(api), "refused")
        self.assertEqual(api.submitted, [])


class BilledHours(Queue):
    """A cap counts Runpod's bill for each hour whose bill is final, and the larger of the bill and the log's estimates
    for the hours after it. So the log's flat start and idle allowance stops counting once the bill is in, and a cap
    never counts less than the bill."""

    LAG = datetime.timedelta(hours=gpu_job.BILL_FINAL_HOURS)

    def hour(self, back):
        """The start of the UTC hour `back` hours before NOW's hour."""
        return NOW.replace(minute=0, second=0, microsecond=0) - datetime.timedelta(hours=back)

    def test_a_job_that_ended_in_a_final_hour_counts_at_the_bill_and_not_at_the_logs_estimate(self):
        when = NOW - self.LAG - datetime.timedelta(hours=2)
        self.logged(9.95, when=when, ended=when + datetime.timedelta(minutes=10))
        api = self.api(billed_today=2.0, billed=2.0)
        self.assertEqual(self.submit(api), "ok", "the bill of $2 is final, so the log's $9.95 no longer counts")

    def test_a_job_that_ended_after_the_last_final_hour_counts_at_the_larger_of_the_log_and_the_bill(self):
        self.logged(9.95, when=NOW - datetime.timedelta(minutes=20), ended=NOW - datetime.timedelta(minutes=10))
        api = self.api(billed_today=0.5, billed=0.5, hours={self.hour(0): 0.5})
        self.assertEqual(self.submit(api), "refused")

    def test_an_open_hour_whose_bill_passes_the_log_counts_at_its_bill(self):
        self.logged(0.1, when=NOW - datetime.timedelta(minutes=20), ended=NOW - datetime.timedelta(minutes=10))
        api = self.api(billed_today=9.95, billed=9.95, hours={self.hour(0): 9.95})
        self.assertEqual(self.submit(api), "refused", "a cap never counts less than the bill")

    def test_a_job_that_started_before_the_cutoff_and_ended_after_it_counts_whole_in_the_open_part(self):
        self.logged(9.95, when=NOW - self.LAG - datetime.timedelta(hours=2), ended=NOW - datetime.timedelta(minutes=5))
        api = self.api(billed_today=1.0, billed=1.0)
        self.assertEqual(self.submit(api), "refused")

    def test_an_older_line_without_an_end_ends_after_its_wait_and_its_execution(self):
        when = NOW - self.LAG - datetime.timedelta(hours=1)
        self.state.mkdir(parents=True, exist_ok=True)
        with (self.state / "jobs.jsonl").open("a") as log:
            log.write(json.dumps({"time": when.isoformat(), "agent": "other", "costUsd": 9.95, "delaySeconds": 60,
                                  "gpuSeconds": 3600 + 1800}) + "\n")
        self.assertEqual(self.submit(self.api(billed_today=1.0, billed=1.0)), "refused",
                         "it ended 30 minutes before NOW, after the last final hour")

    def test_the_total_cap_counts_the_bill_of_final_hours_on_earlier_days_too(self):
        when = NOW - datetime.timedelta(days=1)
        self.logged(29.95, when=when, ended=when + datetime.timedelta(minutes=10))
        self.assertEqual(self.submit(self.api(billed=5.0)), "ok")

    def test_each_open_hour_counts_the_larger_of_its_own_bill_and_its_own_jobs(self):
        self.logged(8.9, when=NOW - datetime.timedelta(minutes=20), ended=NOW - datetime.timedelta(minutes=10))
        api = self.api(billed_today=1.0, billed=1.0, hours={self.hour(gpu_job.BILL_FINAL_HOURS): 1.0})
        self.assertEqual(self.submit(api), "refused",
                         "a pod's $1 in the first open hour does not hide $8.90 of jobs in the last one")

    def test_a_job_that_ended_exactly_at_the_cutoff_counts_in_the_open_hours(self):
        cutoff = self.hour(gpu_job.BILL_FINAL_HOURS)
        self.logged(9.95, when=cutoff - datetime.timedelta(minutes=30), ended=cutoff)
        self.assertEqual(self.submit(self.api(billed_today=1.0, billed=1.0)), "refused")

    def test_an_open_hour_before_midnight_does_not_count_against_the_new_days_cap(self):
        lines = [{"time": "2026-11-11T23:10:00+00:00", "ended": "2026-11-11T23:30:00+00:00", "costUsd": 5.0}]
        hours = {datetime.datetime(2026, 11, 11, 23, tzinfo=datetime.timezone.utc): 0.1,
                 datetime.datetime(2026, 11, 12, 0, tzinfo=datetime.timezone.utc): 0.2}
        cutoff = datetime.datetime(2026, 11, 11, 22, tzinfo=datetime.timezone.utc)
        midnight = datetime.datetime(2026, 11, 12, tzinfo=datetime.timezone.utc)
        self.assertAlmostEqual(gpu_job.counted(0.2, hours, lines, cutoff, since=midnight), 0.2)
        self.assertAlmostEqual(gpu_job.counted(0.3, hours, lines, cutoff), 5.2)

    def test_a_cancelled_job_ends_at_its_expiry_because_its_handler_bills_until_its_limit(self):
        self.submit(self.api(final={"status": "CANCELLED"}))
        line, = self.log_lines()
        self.assertGreater(datetime.datetime.fromisoformat(line["ended"]), NOW + datetime.timedelta(seconds=840))

    def test_each_settled_line_records_when_the_job_ended(self):
        self.submit(self.api())
        line, = self.log_lines()
        self.assertEqual(datetime.datetime.fromisoformat(line["ended"]), NOW)


class Statistics(unittest.TestCase):
    """`gpu_job.py stats` reads the audit log only and prints the cost per job that ran, with its spread."""

    def test_a_summary_gives_the_mean_the_spread_and_the_95_percent_interval_of_the_mean(self):
        summary = gpu_job.summary([1.0, 2.0, 3.0, 4.0])
        self.assertEqual((summary["count"], summary["mean"], summary["median"], summary["min"], summary["max"]),
                         (4, 2.5, 2.5, 1.0, 4.0))
        self.assertAlmostEqual(summary["sd"], 1.2909944, places=6)
        # Student's t of 3 degrees of freedom at 97.5 % is 3.182446, times the standard error 1.2909944 / 2.
        self.assertAlmostEqual(summary["ci95"], 3.182446 * 1.2909944 / 2, places=4)

    def test_the_interval_uses_students_t_and_comes_close_to_the_normal_for_many_jobs(self):
        self.assertAlmostEqual(gpu_job.t_975(1), 12.7062, places=3)
        self.assertAlmostEqual(gpu_job.t_975(10), 2.2281, places=3)
        self.assertAlmostEqual(gpu_job.t_975(58), 2.0017, places=3)
        self.assertAlmostEqual(gpu_job.t_975(1000), 1.9623, places=3)

    def test_one_job_has_a_mean_and_no_spread(self):
        summary = gpu_job.summary([0.5])
        self.assertEqual((summary["count"], summary["mean"], summary["sd"], summary["ci95"]), (1, 0.5, None, None))

    def test_the_median_of_an_odd_count_is_its_middle_value(self):
        self.assertEqual(gpu_job.summary([5.0, 1.0, 3.0])["median"], 3.0)

    def test_the_bill_counts_the_final_hours_from_the_first_jobs_hour_against_the_jobs_that_ended_in_them(self):
        first = datetime.datetime(2026, 11, 12, 12, 10, tzinfo=datetime.timezone.utc)
        lines = [{"time": first.isoformat(), "ended": (first + datetime.timedelta(minutes=5)).isoformat(),
                  "result": "ok", "costUsd": 0.10},
                 {"time": first.isoformat(), "ended": (first + datetime.timedelta(hours=1)).isoformat(),
                  "result": "ok", "costUsd": 0.20},
                 {"time": first.isoformat(), "ended": (first + datetime.timedelta(hours=3)).isoformat(),
                  "result": "ok", "costUsd": 0.40},
                 {"time": first.isoformat(), "result": "refused", "costUsd": 0.0}]
        hour = lambda h: datetime.datetime(2026, 11, 12, h, tzinfo=datetime.timezone.utc)  # noqa: E731
        hours = {hour(2): 0.28, hour(12): 0.05, hour(13): 0.15, hour(14): 0.0, hour(15): 0.30}
        compared = gpu_job.bill_against_estimate(lines, hours, cutoff=hour(15))
        self.assertEqual((compared["start"], compared["end"], compared["jobs"]), (hour(12), hour(15), 2))
        self.assertAlmostEqual(compared["billed"], 0.20, msg="a pod's hour before the first job does not count")
        self.assertAlmostEqual(compared["settled"], 0.30, msg="the job that ended after the cutoff does not count")

    def test_the_report_names_the_bill_beside_the_estimate_and_survives_a_zero_estimate(self):
        hour = datetime.datetime(2026, 11, 12, 12, tzinfo=datetime.timezone.utc)
        compared = {"start": hour, "end": hour + datetime.timedelta(hours=3), "billed": 0.12, "jobs": 2,
                    "settled": 0.0}
        text = gpu_job.stats_text(self.lines(), config(), compared)
        self.assertIn("$0.1200 for the 2 jobs", text)
        self.assertIn("kind cpu", text)
        self.assertIn("not read (the audit log is empty)", gpu_job.stats_text([], config(), "the audit log is empty"))

    def test_the_bill_window_starts_no_earlier_than_the_oldest_hour_that_runpod_returned(self):
        first = datetime.datetime(2026, 11, 12, 1, 10, tzinfo=datetime.timezone.utc)
        lines = [{"time": first.isoformat(), "ended": first.isoformat(), "result": "ok", "costUsd": 0.5},
                 {"time": first.isoformat(), "ended": (first + datetime.timedelta(hours=11)).isoformat(),
                  "result": "ok", "costUsd": 0.2}]
        hours = {datetime.datetime(2026, 11, 12, 12, tzinfo=datetime.timezone.utc): 0.1}
        compared = gpu_job.bill_against_estimate(lines, hours, cutoff=datetime.datetime(2026, 11, 12, 15,
                                                                                         tzinfo=datetime.timezone.utc))
        self.assertEqual((compared["jobs"], compared["settled"]), (1, 0.2),
                         "a job in an hour that the bill did not return does not count against it")

    def lines(self):
        return [{"agent": "a", "kind": "gpu", "result": "ok", "costUsd": 0.02, "gpuSeconds": 50.0},
                {"agent": "a", "kind": "gpu", "result": "failed", "costUsd": 0.04, "gpuSeconds": 180.0},
                {"agent": "b", "kind": "cpu", "result": "ok", "costUsd": 0.01, "gpuSeconds": 430.0},
                {"agent": "b", "result": "lost", "costUsd": 0.15, "gpuSeconds": None},
                {"agent": "b", "kind": "gpu", "result": "refused", "costUsd": 0.0},
                {"agent": "b", "kind": "gpu", "result": "rejected", "costUsd": 0.0, "gpuSeconds": 0.0}]

    def test_the_jobs_that_ran_count_and_refusals_and_rejections_do_not(self):
        groups = gpu_job.cost_groups(self.lines(), config())
        self.assertEqual(groups["all"]["settled"], [0.02, 0.04, 0.01, 0.15])

    def test_the_execution_figure_is_the_execution_time_at_the_price_of_the_jobs_kind(self):
        groups = gpu_job.cost_groups(self.lines(), config())
        self.assertEqual(groups["all"]["execution"], [50.0 * RATE, 180.0 * RATE, 430.0 * CPU_RATE],
                         "a lost job has no execution time, and a line without a kind is a GPU job")

    def test_the_jobs_group_by_kind_and_by_agent(self):
        groups = gpu_job.cost_groups(self.lines(), config())
        self.assertEqual(groups["kind gpu"]["settled"], [0.02, 0.04, 0.15])
        self.assertEqual(groups["kind cpu"]["settled"], [0.01])
        self.assertEqual(groups["agent b"]["settled"], [0.01, 0.15])


class AuditLog(Queue):
    """One line per job: agent, commands, GPU seconds, cost and result."""

    def test_a_finished_job_writes_one_line_with_its_agent_commands_gpu_seconds_cost_and_result(self):
        self.submit(self.api(), commands=["dotnet build", "echo done"])
        line, = self.log_lines()
        self.assertEqual(line["agent"], "agent-7")
        self.assertEqual(line["commands"], ["dotnet build", "echo done"])
        self.assertEqual(line["gpuSeconds"], 50.0)
        self.assertAlmostEqual(line["costUsd"], (50.0 + gpu_job.START_ALLOWANCE_SECONDS + gpu_job.IDLE_SECONDS) * RATE,
                               msg="the cost counts a worker's start and its idle time, as the worst case does")
        self.assertEqual(line["result"], "ok")

    def test_a_finished_job_leaves_nothing_in_flight(self):
        self.submit(self.api())
        self.assertEqual(gpu_job.in_flight(self.state), [])

    def test_a_job_whose_wait_fails_stays_in_flight_with_its_id(self):
        api = self.api(statuses={"job-1": urllib.error.URLError("network down")})
        with self.assertRaises(urllib.error.URLError):
            self.submit(api)
        held, = gpu_job.in_flight(self.state)
        self.assertEqual(held["jobId"], "job-1")
        self.assertEqual(self.log_lines(), [], "the job's line waits for its cost")

    def test_the_next_run_settles_a_finished_job_that_another_run_lost(self):
        self.pending("lost", job="job-lost")
        api = self.api(statuses={"job-lost": {"status": "COMPLETED", "executionTime": 30_000,
                                                "output": {"status": "ok", "steps": []}}})
        self.submit(api)
        lost = [line for line in self.log_lines() if line.get("jobId") == "job-lost"]
        self.assertEqual(len(lost), 1)
        self.assertAlmostEqual(lost[0]["costUsd"], (30.0 + gpu_job.START_ALLOWANCE_SECONDS + gpu_job.IDLE_SECONDS) * RATE)
        self.assertEqual(gpu_job.in_flight(self.state), [])

    def test_a_job_that_another_run_still_waits_for_stays_in_flight(self):
        self.pending("running", job="job-running")
        self.submit(self.api(statuses={"job-running": {"status": "IN_PROGRESS"}}))
        self.assertEqual([held["jobId"] for held in gpu_job.in_flight(self.state)], ["job-running"])

    def test_a_lost_job_that_runpod_no_longer_knows_is_settled_at_its_worst_case(self):
        self.pending("forgotten", job="job-forgotten")
        gone = urllib.error.HTTPError("url", 404, "Not Found", {}, None)
        self.submit(self.api(statuses={"job-forgotten": gone}))
        forgotten, = [line for line in self.log_lines() if line.get("jobId") == "job-forgotten"]
        self.assertAlmostEqual(forgotten["costUsd"], self.worst(840))

    def test_a_job_in_flight_without_an_id_is_settled_at_its_worst_case_when_it_expires(self):
        self.pending("unsent", when=NOW - datetime.timedelta(hours=4), expires=3600)
        self.submit(self.api())
        unsent = [line for line in self.log_lines() if line["agent"] == "other"]
        self.assertEqual(len(unsent), 1)
        self.assertAlmostEqual(unsent[0]["costUsd"], self.worst(840))

    def test_a_second_signal_during_the_cancel_does_not_lose_the_cancel(self):
        class Api:
            def __init__(self):
                self.calls, self.handlers = [], None

            def status(self, endpoint, job):
                self.calls.append("status")
                if self.calls == ["status"]:
                    raise KeyboardInterrupt
                return {"status": "CANCELLED"}

            def cancel(self, endpoint, job):
                self.calls.append("cancel")
                self.handlers = [signal.getsignal(number) for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP)]

        before = [signal.getsignal(number) for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP)]
        api = Api()
        try:
            self.assertEqual(gpu_job.wait(api, "ep1", "job-1", 0.0)["status"], "CANCELLED")
        finally:
            for number, handler in zip((signal.SIGINT, signal.SIGTERM, signal.SIGHUP), before):
                signal.signal(number, handler)
        self.assertEqual(api.calls, ["status", "cancel", "status"])
        self.assertEqual(api.handlers, [signal.SIG_IGN] * 3, "a second Ctrl-C or SIGTERM must not cut the cancel short")

    def test_a_cancelled_job_costs_its_worst_case_because_the_cancel_does_not_stop_its_commands(self):
        api = self.api(final={"status": "CANCELLED", "executionTime": 10_000})
        self.submit(api)
        line, = self.log_lines()
        self.assertAlmostEqual(line["costUsd"], line["worstCaseUsd"])

    def test_a_job_that_runpod_rejects_costs_nothing_and_leaves_nothing_in_flight(self):
        api = self.api()
        api.submit_error = urllib.error.HTTPError("url", 409, "Conflict", {}, None)
        with self.assertRaises(urllib.error.HTTPError):
            self.submit(api)
        self.assertEqual(gpu_job.in_flight(self.state), [])
        line, = self.log_lines()
        self.assertEqual((line["result"], line["costUsd"]), ("rejected", 0.0))

    def test_a_job_in_flight_with_an_id_is_settled_at_its_worst_case_when_it_expires(self):
        self.pending("stuck", job="job-stuck", when=NOW - datetime.timedelta(hours=4), expires=3600)
        self.submit(self.api(statuses={"job-stuck": {"status": "IN_QUEUE"}}))
        stuck, = [line for line in self.log_lines() if line.get("jobId") == "job-stuck"]
        self.assertAlmostEqual(stuck["costUsd"], self.worst(840))

    def test_a_cancelled_job_without_an_execution_time_costs_its_worst_case(self):
        api = self.api(final={"status": "CANCELLED"})
        self.assertEqual(self.submit(api), "CANCELLED")
        line, = self.log_lines()
        self.assertGreater(line["costUsd"], 0.0)
        self.assertAlmostEqual(line["costUsd"], line["worstCaseUsd"])

    def test_a_job_that_runpod_timed_out_still_logs_its_gpu_seconds_and_cost(self):
        api = self.api(final={"status": "TIMED_OUT", "executionTime": 900_000, "delayTime": 1_000})
        self.assertEqual(self.submit(api), "TIMED_OUT")
        line, = self.log_lines()
        self.assertEqual(line["result"], "TIMED_OUT")
        self.assertAlmostEqual(line["costUsd"], (900.0 + gpu_job.START_ALLOWANCE_SECONDS + gpu_job.IDLE_SECONDS) * RATE)

    def test_a_job_that_runpod_marks_failed_for_the_handlers_error_keeps_the_handlers_result(self):
        output = {**FakeRunpod().final["output"], "status": "failed", "error": "command 1 failed with exit code 2",
                  "steps": [{"command": "x", "exitCode": 2, "seconds": 1.0}]}
        api = self.api(final={**FakeRunpod().final, "status": "FAILED", "output": output})
        self.assertEqual(self.submit(api), "failed")
        result = json.loads((self.out / "result.json").read_text())
        self.assertEqual(result["runpodStatus"], "FAILED", "the result file keeps Runpod's status beside the handler's")
        self.assertEqual(result["status"], "failed")

    def test_a_failed_command_is_the_jobs_result(self):
        output = {**FakeRunpod().final["output"], "status": "failed",
                  "steps": [{"command": "x", "exitCode": 2, "seconds": 1.0}]}
        api = self.api(final={**FakeRunpod().final, "output": output})
        self.assertEqual(self.submit(api), "failed")
        self.assertEqual(self.log_lines()[0]["exitCodes"], [2])


class Kinds(Queue):
    """A job runs on GPU workers or on CPU workers. Each kind has its own endpoints, workers, price and time limit,
    under the same caps and audit log."""

    def test_a_cpu_job_runs_on_the_cpu_endpoint_of_its_image_with_four_workers_beside_the_gpu_endpoint(self):
        api = self.api()
        self.assertEqual(self.submit(api, kind="cpu"), "ok")
        created, = api.created
        self.assertTrue(created["name"].startswith("tertius-cpu-"))
        self.assertNotIn("gpu", created)
        self.assertEqual(api.submitted[0][0], "ep-new-1")
        self.assertEqual({live["id"]: live["workers"]["max"] for live in api.live}, {"ep1": 6, "ep-new-1": 4})

    def test_a_cpu_job_counts_the_cpu_price(self):
        self.submit(self.api(), kind="cpu")
        line, = self.log_lines()
        self.assertEqual(line["kind"], "cpu")
        self.assertAlmostEqual(line["costUsd"], (50.0 + gpu_job.START_ALLOWANCE_SECONDS + gpu_job.IDLE_SECONDS) * CPU_RATE)
        self.assertAlmostEqual(line["worstCaseUsd"], (7140 + gpu_job.HANDLER_MARGIN_SECONDS
                                                     + gpu_job.START_ALLOWANCE_SECONDS + gpu_job.IDLE_SECONDS) * CPU_RATE)

    def test_a_cpu_job_gets_the_longer_time_limit_of_the_cpu_endpoint(self):
        api = self.api()
        self.submit(api, kind="cpu", seconds=5000)
        body = api.submitted[0][1]
        self.assertEqual(body["input"]["seconds"], 5000)
        self.assertEqual(body["policy"]["executionTimeout"], (5000 + gpu_job.HANDLER_MARGIN_SECONDS) * 1000)

    def test_a_gpu_image_switch_keeps_the_cpu_endpoints_four_and_runs_on_the_busy_old_gpu_endpoint(self):
        newer = self.commit({**SHELL_FILES, "tools/runpod/handler.py": "PROTOCOL = 3\n# a fix on main\n"})
        self.on_main(newer)
        self.pending("running", job="job-running", endpoint="ep1")
        api = self.api(endpoints=[self.endpoint(self.base), self.endpoint(self.base, id="ep-cpu", most=4, kind="cpu")],
                       statuses={"job-running": {"status": "IN_PROGRESS"}})
        self.assertEqual(self.submit(api, trees={"main": newer}, images=[self.base, newer]), "ok")
        self.assertEqual((api.submitted[0][0], api.created, api.updated, api.deleted), ("ep1", [], [], []))

    def test_a_route_of_one_kind_leaves_the_endpoints_of_the_other_kind_alone(self):
        newer = self.commit({**SHELL_FILES, "tools/runpod/handler.py": "PROTOCOL = 3\n# a fix on main\n"})
        self.on_main(newer)
        api = self.api(endpoints=[self.endpoint(self.base)])
        self.submit(api, trees={"main": newer}, images=[self.base, newer], kind="cpu")
        self.assertEqual(api.deleted, [], "a switch of one kind never takes the other kind's workers")
        self.assertEqual(api.submitted[0][0], "ep-new-1")

    def test_a_route_of_one_kind_keeps_the_other_kinds_endpoint_of_the_same_image(self):
        api = self.api(endpoints=[self.endpoint(self.base), self.endpoint(self.base, id="ep-cpu", most=3, kind="cpu")])
        self.assertEqual(self.submit(api), "ok")
        self.assertEqual(api.deleted, [])

    def test_a_bare_cpu_name_is_not_one_of_the_scripts_endpoints(self):
        names = ["tertius-gpu", "tertius-cpu", f"tertius-cpu-{'a' * 12}", f"tertius-gpu-{'a' * 12}-0f0f", "tertius-gpux"]
        self.assertEqual([bool(gpu_job.OURS.match(name)) for name in names], [True, False, True, True, False])

    def test_a_stopped_cpu_endpoint_refuses_gpu_jobs_too(self):
        api = self.api(endpoints=[self.endpoint(self.base), self.endpoint(self.base, id="ep-cpu", most=0, kind="cpu")])
        self.assertEqual(self.submit(api), "refused")
        self.assertIn("0 workers", self.log_lines()[-1]["error"])


class Configuration(unittest.TestCase):
    """endpoint.json and the image's handler agree."""

    def test_the_handlers_ceiling_lets_every_kind_use_its_whole_time_limit(self):
        configured = gpu_job.Config.load()
        self.assertGreaterEqual(gpu_job.handler.Limits().seconds,
                                max(kind.job_seconds for kind in configured.kinds.values()),
                                "a CPU job of the official Stryker run needs its 7,140 s on the worker too")

    def test_the_kinds_leave_headroom_under_the_quota_for_an_image_switch(self):
        configured = gpu_job.Config.load()
        self.assertLess(sum(kind.workers for kind in configured.kinds.values()), gpu_job.WORKER_QUOTA)


class EndpointRequest(unittest.TestCase):
    """The endpoint that the script creates bounds the spending by itself too."""

    def test_the_gpu_endpoint_runs_at_most_six_workers_with_the_execution_timeout_on_the_rtx_2000_ada_only(self):
        body = gpu_job.endpoint_request(config(), "gpu", "a" * 40)
        self.assertEqual(body["workers"]["max"], 6)
        self.assertEqual(body["workers"]["min"], 0, "no worker runs, and bills, without a job")
        self.assertEqual(body["timeout"], 900_000)
        self.assertEqual(body["gpu"]["pools"], ["AMPERE_16"])
        self.assertNotIn("NVIDIA RTX 2000 Ada Generation", body["gpu"]["excludedTypes"])
        self.assertEqual(len(body["gpu"]["excludedTypes"]), 3, "the pool's other three cards are excluded")
        self.assertEqual(body["image"], f"{REPOSITORY}:{'a' * 40}")
        self.assertRegex(body["name"], f"^tertius-gpu-{'a' * 12}-[0-9a-f]{{4}}$")

    def test_the_cpu_endpoint_runs_at_most_four_compute_optimized_workers_of_two_vcpus(self):
        body = gpu_job.endpoint_request(config(), "cpu", "a" * 40)
        self.assertEqual((body["workers"]["min"], body["workers"]["max"]), (0, 4))
        self.assertEqual(body["timeout"], 7_200_000)
        self.assertEqual(body["cpu"], [{"id": "cpu3c", "vcpuCount": 2}])
        self.assertNotIn("gpu", body)
        self.assertRegex(body["name"], f"^tertius-cpu-{'a' * 12}-[0-9a-f]{{4}}$")

    def test_two_requests_for_one_image_get_two_names_because_runpod_keeps_a_deleted_endpoints_name(self):
        self.assertNotEqual(gpu_job.endpoint_request(config(), "cpu", "a" * 40)["name"],
                            gpu_job.endpoint_request(config(), "cpu", "a" * 40)["name"])


if __name__ == "__main__":
    unittest.main()
