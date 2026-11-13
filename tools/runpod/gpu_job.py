#!/usr/bin/env python3
"""Tool code (tools/runpod/README.md): the one script through which an agent runs a GPU or CPU job on a Runpod serverless
endpoint, within the spending caps.

    python3 -I tools/runpod/gpu_job.py submit --agent NAME --tree main=main --tree branch=HEAD \\
        --run 'cd branch && dotnet build src/Tertius.Demo -c Release' --output 'branch/out/*.json' --out DIR

Each tree is the commit of its ref. The script scans the commits that GitHub does not hold yet for secrets, pushes
each one to a hidden ref of the public repository, and sends the job to the endpoint whose image has the commits' dev
shell, which it creates when none has. A worker fetches each commit by its hidden ref and makes sure that the ref
names the commit. The script refuses a job before it
sends it when the job's worst case would pass the daily cap or the total cap. It counts the jobs of the audit log, the
jobs in flight and Runpod's billed total. It writes one line per job, refusals too, to the audit log, and the job's
output files and result under --out.

The API key stays in its file, which ~/.config/tertius/runpod-key-file names. The script reads it for each request
and never prints it.
The standard library only.
"""

import argparse
import base64
import contextlib
import datetime
import fcntl
import json
import os
import re
import signal
import statistics
import subprocess
import sys
import time
import urllib.error
import urllib.request
import importlib.util
import tempfile
import uuid
from dataclasses import dataclass
from pathlib import Path

# The handler's definition of a dev shell is the one definition: python3 -I puts no folder of the script on the path.
_spec = importlib.util.spec_from_file_location("handler", Path(__file__).with_name("handler.py"))
handler = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(handler)

CONFIG_FILE = Path(__file__).with_name("endpoint.json")
STATE = Path.home() / ".local/state/tertius/gpu-jobs"
# The hours after the current one's start before an hour's bill counts as final (README.md, "The caps"). On 2026-11-12
# the bill of 17:00 UTC grew from 18:12 to 19:02 and then stayed, so a bill is final about one hour after its hour
# ends. With 2, the bill of 17:00 counts as final from 20:00 on, one hour of margin.
BILL_FINAL_HOURS = 2
# A file outside the repository that holds the path of the key's file, so that no agent needs to know or set it.
KEY_POINTER = Path.home() / ".config/tertius/runpod-key-file"
JOB_API = "https://api.runpod.ai/v2"
ACCOUNT_API = "https://api.runpod.io/v2"

# A job bills its execution time plus a worker's start and its idle time after the job: a flex worker bills from its
# start until it stops, and the endpoint keeps it for workers.idleTimeout seconds after its last job. The script counts
# both for every job, also for a job that a warm worker took, so it overcounts rather than undercounts. Runpod does not
# bill the image pull or a scaled-down worker (docs.runpod.io/serverless/pricing); the cold start measured 10 s.
START_ALLOWANCE_SECONDS = 60
IDLE_SECONDS = 10
# The handler's fetch of the trees before the commands and its collection of the outputs after them run inside the
# execution timeout too, so the commands get this much less.
HANDLER_MARGIN_SECONDS = 60
# A job that waits longer than this in the queue expires unrun.
QUEUE_SECONDS = 2 * 3600
FINAL = {"COMPLETED", "FAILED", "CANCELLED", "TIMED_OUT"}
# The handler protocol that this script speaks: the shape of a job's input. An image whose handler speaks another one
# is never used (tools/runpod/handler.py keeps the same number).
PROTOCOL = 3
HANDLER = "tools/runpod/handler.py"
# The paths whose files the image bakes into its dev shell. A commit that changes one of them needs another image.
# The workflow gpu-image lists them too, to build an image when they change on main.
SHELL_PATHS = handler.SHELL_PATHS
# The script's endpoints: tertius-<kind>-<12 characters of the image's commit>-<4 random hex digits>, one per kind and
# image, and the first endpoints, without the random part or with the bare name tertius-gpu. The script deletes no
# other endpoint. Runpod keeps the template name of a deleted or failed endpoint for up to 90 minutes and refuses a new
# endpoint of the same name ("endpoint template names must be unique", 2026-11-12), so each creation draws a new name.
OURS = re.compile(r"^tertius-(?:(gpu)|(gpu|cpu)-[0-9a-f]{12}(?:-[0-9a-f]{4})?)$")
# The hidden refs that publish the jobs' commits: refs/gpu-jobs/<UTC day>/<agent>/<commit>. Not a branch or a tag, so
# no clone fetches them. A ref goes after REF_DAYS days, long after its job's queue time and timeout.
JOB_REFS = "refs/gpu-jobs"
# Main as this repository last saw it on GitHub. The workflow gpu-image builds an image of each change of the image's
# files on main, and every job not of a branch with its own image runs on main's newest image.
MAIN = "refs/remotes/origin/main"
REF_DAYS = 3
COMMIT = re.compile(r"^[0-9a-f]{40}$")
# The machines of each kind. A GPU endpoint runs on the RTX 2000 Ada only, so that the timings of different jobs come
# from one kind of card. The 16 GB pool also holds these three, and a card that Runpod adds to the pool later becomes
# eligible. A CPU endpoint runs on compute-optimized workers of 2 vCPUs, for Stryker and the CPU-only test projects
# (Alexis's decision of 2026-11-12, ticket t8-yd4j).
POOL = "AMPERE_16"
OTHER_CARDS = ["NVIDIA RTX A4000", "NVIDIA RTX A4500", "NVIDIA RTX 4000 Ada Generation"]
# The container disk in GB: the image's files, plus each tree's checkout with its build outputs. Runpod allows a CPU
# worker of 2 vCPUs at most 20 GB (its error of 2026-11-12).
HARDWARE = {"gpu": {"gpu": {"pools": [POOL], "excludedTypes": OTHER_CARDS}, "disk": 60},
            "cpu": {"cpu": [{"id": "cpu3c", "vcpuCount": 2}], "disk": 20}}
# Runpod's quota of the account: the maximums of all its endpoints together may not pass this (its error of
# 2026-11-12). The kinds' workers in endpoint.json leave headroom under it, so that a new endpoint of an image switch
# fits beside a busy old one. A new endpoint gets what the quota leaves, at most its kind's workers, and the script
# never lowers another endpoint to make room.
WORKER_QUOTA = 10


class Refused(Exception):
    """The job cannot run as it is, and the script sends nothing. The message says why and what fixes it."""


@dataclass(frozen=True)
class Kind:
    """The settings of one kind of worker. rate_per_second: the price of a worker in USD per second. workers: the
    kind's share of the account's quota, the most workers that its endpoints run at once. job_seconds: the most time
    that the commands of one job get. endpoint_timeout_seconds: the endpoint's execution timeout, the hard bound per
    job, at least job_seconds plus the handler's margin."""

    rate_per_second: float
    workers: int
    job_seconds: int
    endpoint_timeout_seconds: int


@dataclass(frozen=True)
class Config:
    """image_repository: the images, each tagged with the commit that it baked. repository: the public repository that
    the workers fetch the trees' commits from. daily_caps_usd: the cap of each UTC day, for all kinds together, as a map
    from the first day of a cap ("2026-11-13") to its amount; a day has the cap of the latest date on or before it.
    total_cap_usd: the cap of all spending. kinds: gpu and cpu, each a Kind. The live endpoints are not here: Runpod's list of endpoints is
    their one store, and the script finds each by its name and image."""

    image_repository: str
    repository: str
    daily_caps_usd: dict
    total_cap_usd: float
    kinds: dict

    def daily_cap(self, day: datetime.date) -> float:
        dates = [start for start in self.daily_caps_usd if datetime.date.fromisoformat(start) <= day]
        if not dates:
            raise Refused(f"endpoint.json gives no daily cap for {day}")
        return self.daily_caps_usd[max(dates)]

    @staticmethod
    def load(path: Path = CONFIG_FILE) -> "Config":
        fields = json.loads(path.read_text())
        return Config(**{**fields, "kinds": {name: Kind(**kind) for name, kind in fields["kinds"].items()}})


@dataclass
class Request:
    agent: str
    trees: dict  # tree name -> git ref
    commands: list
    outputs: list
    seconds: int
    out: Path
    kind: str = "gpu"


class RunpodApi:
    """Runpod's API as the script uses it, with the key read from its file for each call."""

    def __init__(self, key_file: Path):
        self.key_file = key_file

    def call(self, method: str, url: str, body=None) -> dict:
        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(url, data=data, method=method, headers={
            "Authorization": "Bearer " + self.key_file.read_text().strip(), "Content-Type": "application/json"})
        with urllib.request.urlopen(request, timeout=60) as reply:
            return json.loads(reply.read() or b"{}")

    def billed_total(self) -> float:
        """The account's bill of the last 12 months, which holds its whole life. Without a window the API sums only the
        last 30 days, and a start time needs an end time too."""
        reply = self.call("GET", f"{ACCOUNT_API}/billing?bucketSize=month&lastN=12")
        return float(reply["metadata"]["totals"]["totalAmount"])

    def billed_today(self) -> float:
        """The account's bill of the current UTC day."""
        reply = self.call("GET", f"{ACCOUNT_API}/billing?bucketSize=day&lastN=1")
        return float(reply["metadata"]["totals"]["totalAmount"])

    def billed_hours(self, count: int, parts=("totalAmount",)) -> dict:
        """The start of each of the last `count` UTC hours that Runpod billed -> the sum of the record's `parts` in that
        hour: by default the whole bill, pods and storage too. The current hour has no record until it ends."""
        reply = self.call("GET", f"{ACCOUNT_API}/billing?bucketSize=hour&lastN={count}")
        return {datetime.datetime.fromisoformat(record["startTime"].replace("Z", "+00:00")):
                sum(float(record.get(part) or 0.0) for part in parts) for record in reply.get("records", [])}

    def submit(self, endpoint: str, body: dict) -> str:
        return self.call("POST", f"{JOB_API}/{endpoint}/run", body)["id"]

    def status(self, endpoint: str, job: str) -> dict:
        return self.call("GET", f"{JOB_API}/{endpoint}/status/{job}")

    def cancel(self, endpoint: str, job: str):
        self.call("POST", f"{JOB_API}/{endpoint}/cancel/{job}")

    def endpoints(self) -> list:
        """Every endpoint of the account: the list comes in pages."""
        found, url = [], f"{ACCOUNT_API}/serverless"
        while url:
            reply = self.call("GET", url)
            found += reply["endpoints"]
            cursor = (reply.get("pagination") or {}).get("nextCursor")
            url = f"{ACCOUNT_API}/serverless?cursor={cursor}" if (reply.get("pagination") or {}).get("hasNextPage") \
                and cursor else None
        return found

    def create_endpoint(self, body: dict) -> dict:
        return self.call("POST", f"{ACCOUNT_API}/serverless", body)

    def update_endpoint(self, endpoint: str, body: dict):
        self.call("PATCH", f"{ACCOUNT_API}/serverless/{endpoint}", body)

    def delete_endpoint(self, endpoint: str):
        self.call("DELETE", f"{ACCOUNT_API}/serverless/{endpoint}")

    def workers(self, endpoint: str) -> list:
        return self.call("GET", f"{ACCOUNT_API}/serverless/{endpoint}/workers")["workers"]


class Registry:
    """The public image registry, read without a login, as Runpod pulls from it."""

    def __init__(self, image_repository: str):
        self.host, _, self.name = image_repository.partition("/")

    def images(self) -> list:
        """The commits that have an image: the tags that are full commit ids."""
        with urllib.request.urlopen(f"https://{self.host}/token?scope=repository:{self.name}:pull", timeout=30) as reply:
            token = json.loads(reply.read())["token"]
        url, tags = f"https://{self.host}/v2/{self.name}/tags/list?n=1000", []
        while url:
            request = urllib.request.Request(url, headers={"Authorization": f"Bearer {token}"})
            with urllib.request.urlopen(request, timeout=30) as reply:
                tags += json.loads(reply.read()).get("tags") or []
                link = re.search(r"<([^>]+)>;\s*rel=\"next\"", reply.headers.get("Link") or "")
            url = f"https://{self.host}{link.group(1)}" if link else None
        return [tag for tag in tags if COMMIT.match(tag)]


class GitHub:
    """The public repository's hidden refs of the jobs. Reads go to the public URL, and writes go over SSH with the
    Mac's own key. --no-verify skips the repository's pre-push hook, which is for branches, not for these refs."""

    def __init__(self, repo: Path, repository: str):
        self.repo = repo
        self.url = repository
        self.push_url = re.sub(r"^https://github\.com/", "git@github.com:", repository)

    def push(self, sha: str, ref: str):
        git(self.repo, "push", "-q", "--no-verify", self.push_url, f"{sha}:{ref}")

    def new_commits(self, shas: list) -> list:
        """The commits of `shas` and their history that no branch or tag of GitHub holds, asked of GitHub itself:
        origin's tracking refs move also when a push reaches only the knot."""
        listing = git(self.repo, "ls-remote", "--heads", "--tags", self.url).decode()
        ids = "".join(line.split("\t")[0] + "\n" for line in listing.splitlines() if "\t" in line)
        known = subprocess.run(["git", "cat-file", "--batch-check=%(objectname) %(objecttype)"], cwd=self.repo,
                               input=ids, capture_output=True, text=True, check=True).stdout
        held = sorted({line.split()[0] for line in known.splitlines() if line.endswith(" commit")})
        return git(self.repo, "rev-list", *shas, "--not", *held).decode().split()

    def refs(self) -> list:
        listing = git(self.repo, "ls-remote", self.url, f"{JOB_REFS}/*").decode()
        return [line.split("\t")[1] for line in listing.splitlines() if "\t" in line]

    def delete(self, refs: list):
        if refs:
            git(self.repo, "push", "-q", "--no-verify", self.push_url, *[f":{ref}" for ref in refs])


def scan(repo: Path, new: list) -> list:
    """The secrets in the commits of `new`, the commits that GitHub does not hold yet: gitleaks' findings, redacted,
    and the Runpod key's own text. Each finding names its rule, file and commit, never the secret. gitleaks comes from
    the repository's pinned nixpkgs, with its default rules: a commit's own .gitleaks.toml, .gitleaksignore or
    gitleaks:allow comment cannot silence it. Its source is the repository's git folder, not a working tree, because
    gitleaks also reads a .gitleaksignore from its source folder."""
    if not new:
        return []
    source = (repo / git(repo, "rev-parse", "--git-common-dir").decode().strip()).resolve()
    with tempfile.TemporaryDirectory() as folder:
        rules = Path(folder) / "gitleaks.toml"
        rules.write_text("[extend]\nuseDefault = true\n")
        (Path(folder) / ".gitleaksignore").write_text("")
        report = subprocess.run(["nix", "shell", "--inputs-from", str(repo), "nixpkgs#gitleaks", "--command",
                                 "gitleaks", "git", str(source), "--log-opts", " ".join(["-m", "--no-walk", *new]),
                                 "--config", str(rules), "--gitleaks-ignore-path", folder, "--ignore-gitleaks-allow",
                                 "--redact", "--no-banner", "--report-format", "json", "--report-path", "-",
                                 "--exit-code", "0", "--log-level", "error"],
                                check=True, capture_output=True, text=True).stdout
    findings = [f"{leak['RuleID']} in {leak['File']} of {leak['Commit'][:12]}" for leak in json.loads(report or "[]")]
    key = key_file()
    if key is not None and key.exists():
        text = key.read_text().strip().encode()
        patch = git(repo, "log", "-p", "-m", "--no-walk", "--format=commit %H", *new)
        if text and text in patch:
            findings.append("the Runpod API key's text in one of the new commits")
    return findings


def shell_of(repo: Path, sha: str) -> tuple:
    """The dev shell of a commit: the object ids of the paths that the image bakes into it. Two commits with the same
    ids have the same shell. The ids also hold the files' modes, so this rule is stricter than the handler's byte
    comparison, and a commit that passes here passes on the worker too."""
    return tuple(git_maybe(repo, "rev-parse", "--verify", "-q", f"{sha}:{path}") for path in SHELL_PATHS)


def protocol_of(repo: Path, sha: str) -> int:
    """The handler protocol that the image of a commit speaks; 0 for a handler without one."""
    text = git_maybe(repo, "show", f"{sha}:{HANDLER}")
    found = re.search(r"^PROTOCOL = (\d+)$", text, re.MULTILINE)
    return int(found.group(1)) if found else 0


def have(repo: Path, config: Config, sha: str) -> bool:
    """Whether the commit is here, fetched from the public repository if it was not."""
    if git_maybe(repo, "cat-file", "-t", sha) == "commit":
        return True
    git_maybe(repo, "fetch", "-q", "--no-tags", config.repository, sha)
    return git_maybe(repo, "cat-file", "-t", sha) == "commit"


def images_for(config: Config, registry, repo: Path, shell: tuple, shas: list, main: str) -> list:
    """The commits of the images that can run the job, the best first, among the images that have the shell and speak
    this script's protocol. A branch's own image comes first: one that the job's commits descend from and that main does not hold,
    built from the branch's tag. Otherwise the newest image of main, so that all jobs of main's images share one
    endpoint. An image of another branch never runs the job. The newest by commit time, and of two at the same second
    the one with more history."""
    images = registry.images()
    missing = [image for image in images if not have(repo, config, image)]
    fitting = [image for image in images if image not in missing
               and protocol_of(repo, image) == PROTOCOL and shell_of(repo, image) == shell]
    on_main = [image for image in fitting if ancestor(repo, image, main)]
    own = [image for image in fitting if image not in on_main and any(ancestor(repo, image, sha) for sha in shas)]
    if not (own or on_main):
        unread = f" The commits of {len(missing)} images could not be fetched: {', '.join(missing)}." if missing else ""
        raise Refused(f"no image has the dev shell of {shas[0]} and the handler protocol {PROTOCOL}: build "
                      f"{config.image_repository}:{shas[0]} with the workflow gpu-image first (tools/runpod/README.md, "
                      f"\"A new dev shell\").{unread}")
    return sorted(own or on_main, key=lambda image: (int(git(repo, "show", "-s", "--format=%ct", image)),
                                                     int(git(repo, "rev-list", "--count", image))), reverse=True)


def ancestor(repo: Path, older: str, newer: str) -> bool:
    return subprocess.run(["git", "merge-base", "--is-ancestor", older, newer], cwd=repo).returncode == 0


# The file in the state folder that the orchestrator's watchdog writes before it sets every endpoint to 0 workers. The
# orchestrator removes it when it restarts them. An endpoint at 0 workers without it is on its way out: on 2026-11-13 a
# delete in Runpod's console held the CPU endpoint at 0 workers for 5 s before it was gone, and the script took that for
# the watchdog's stop (ticket t8-j5oc).
STOP_MARKER = "stopped-by-watchdog"


def ours(api) -> list:
    """The script's endpoints that can take jobs: not one at 0 workers, which a delete holds there on its way out. A
    watchdog stop is the marker's (refuse_if_stopped)."""
    return [endpoint for endpoint in api.endpoints()
            if OURS.match(endpoint.get("name", "")) and endpoint["workers"]["max"] > 0]


def kind_of(endpoint: dict) -> str:
    match = OURS.match(endpoint["name"])
    return match.group(1) or match.group(2)


def refuse_if_stopped(state: Path):
    """Refuse while the watchdog's marker says that it stopped the endpoints, of either kind."""
    if (state / STOP_MARKER).exists():
        raise Refused(f"the watchdog stopped the endpoints at 0 workers ({state / STOP_MARKER}); the orchestrator "
                      "restarts them")


def free_workers(endpoints: list, most: int, beside: str | None = None) -> int:
    """How many workers an endpoint can have beside the endpoint `beside`: what the account's quota leaves beside all
    other endpoints, at most `most`, its kind's workers."""
    return min(most, WORKER_QUOTA - sum(endpoint["workers"]["max"] for endpoint in endpoints
                                        if endpoint["id"] != beside))


def grow(api, state: Path, live: str, most: int):
    """Give the live endpoint its kind's workers once the quota leaves room: an endpoint made beside an old one got
    less."""
    endpoints = api.endpoints()
    refuse_if_stopped(state)
    mine = next((endpoint for endpoint in endpoints if endpoint["id"] == live and endpoint["workers"]["max"] > 0), None)
    if mine is None:
        raise Refused(f"the endpoint {live} went away during the routing")
    most = free_workers(endpoints, most, beside=live)
    if most > mine["workers"]["max"]:
        api.update_endpoint(live, {"workers": {"max": most}})


def route(config: Config, api, state: Path, images: list, kind: str) -> tuple:
    """Under the lock, after the jobs in flight are settled: the endpoint of `kind` that runs the job and its image, in
    three steps. Each step looks only at the endpoints of `kind`, apart from the account's quota. So an image switch of
    one kind never deletes the other kind's endpoint and takes its workers (user, 2026-11-12: a GPU switch never
    shrinks the CPU endpoint). A kind's idle old endpoint waits for a job of its own kind.

    1. Every endpoint of the kind goes that does not run the best of `images`, no job in flight names, and no worker
       runs on. So idle old endpoints give their workers back to the quota first.
    2. If no endpoint of the kind runs the best image, the script creates one with the workers that the quota leaves,
       at most the kind's workers. While it leaves none, the job runs on the kind's endpoint of the best image that has
       one, or is refused.
    3. The live endpoint grows to the kind's workers once the quota leaves room.

    While the watchdog's marker exists, it stopped the endpoints, so no job runs and nothing is deleted. The watchdog
    does not take the lock and writes its marker before it stops the endpoints, so the script reads the marker at every
    read of the list: the first, one before each deletion, one after a creation and one before the growth. A stop that
    one of them sees removes this run's new endpoint again. An endpoint at 0 workers without the marker is on its way
    out, and the routing leaves it alone (ours). The rule narrows the race with the watchdog but cannot close it
    (README.md, "The dev shell, the image and the endpoint")."""
    most = config.kinds[kind].workers
    endpoints = ours(api)
    refuse_if_stopped(state)
    best = f"{config.image_repository}:{images[0]}"
    busy = {pending["endpoint"] for pending in in_flight(state)}
    for endpoint in endpoints:
        if kind_of(endpoint) != kind or endpoint["image"] == best or endpoint["id"] in busy or any(
                worker.get("status") == "RUNNING" for worker in api.workers(endpoint["id"])):
            continue
        # A read of the list just before the deletion: the watchdog may have stopped the endpoints since the first
        # read, and a delete by hand may have taken this one on its way out.
        still = any(other["id"] == endpoint["id"] for other in ours(api))
        refuse_if_stopped(state)
        if still:
            api.delete_endpoint(endpoint["id"])
    endpoints = ours(api)
    refuse_if_stopped(state)
    running = {endpoint["image"]: endpoint["id"] for endpoint in endpoints if kind_of(endpoint) == kind}
    if best in running:
        grow(api, state, running[best], most)
        return running[best], images[0]
    room = free_workers(api.endpoints(), most)
    if room < 1:
        image = next((image for image in images if f"{config.image_repository}:{image}" in running), None)
        if image is None:
            raise Refused(f"the other endpoints hold the account's whole worker quota of {WORKER_QUOTA}, so no {kind} "
                          f"endpoint of {images[0]} fits until their jobs end")
        return running[f"{config.image_repository}:{image}"], image
    request = endpoint_request(config, kind, images[0])
    request["workers"]["max"] = room
    created = api.create_endpoint(request)["id"]
    try:
        refuse_if_stopped(state)
        grow(api, state, created, most)
    except (Refused, OSError):
        api.delete_endpoint(created)
        raise
    return created, images[0]


def execution_seconds(kind: Kind, seconds: int) -> int:
    """The job's execution timeout: its commands' time plus the handler's margin, within the endpoint's timeout."""
    return min(kind.endpoint_timeout_seconds, min(seconds, kind.job_seconds) + HANDLER_MARGIN_SECONDS)


def cost_usd(kind: Kind, execution: float) -> float:
    """What a job of `execution` seconds bills at most: the same model for the worst case and for the logged cost."""
    return (execution + START_ALLOWANCE_SECONDS + IDLE_SECONDS) * kind.rate_per_second


def worst_case_usd(kind: Kind, seconds: int) -> float:
    return cost_usd(kind, execution_seconds(kind, seconds))


# The script keeps two stores. jobs.jsonl holds one line per settled job: its cost is known, or it was refused. The
# folder pending/ holds one file per job in flight, with its worst case and, once Runpod accepted it, its id. A job
# moves from the folder to the log under the lock, in one step, so it always counts once.


class locked:
    """The state's lock. It spans every cap check, every change of pending/ and every append to the log."""

    def __init__(self, state: Path):
        state.mkdir(parents=True, exist_ok=True)
        self.path = state / "lock"

    def __enter__(self):
        self.file = self.path.open("w")
        fcntl.flock(self.file, fcntl.LOCK_EX)

    def __exit__(self, *_):
        self.file.close()


@contextlib.contextmanager
def owning(state: Path, name: str):
    """The lock of the run that waits for the job `name`, held from before its entry exists until it settles. The
    system releases it when that run dies, so a held lock means that the run still fetches the job's result."""
    folder = state / "pending"
    folder.mkdir(parents=True, exist_ok=True)
    path = folder / f"{name}.owner"
    with path.open("w") as file:
        fcntl.flock(file, fcntl.LOCK_EX)
        try:
            yield
        finally:
            path.unlink(missing_ok=True)


def owned(state: Path, name: str) -> bool:
    """Whether a live run holds the job's owner lock."""
    try:
        descriptor = os.open(state / "pending" / f"{name}.owner", os.O_RDONLY)
    except FileNotFoundError:
        return False
    try:
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        return True
    finally:
        os.close(descriptor)
    return False


def log_lines(state: Path) -> list:
    path = state / "jobs.jsonl"
    return [json.loads(line) for line in path.read_text().splitlines() if line.strip()] if path.exists() else []


def append_log(state: Path, line: dict):
    with (state / "jobs.jsonl").open("a") as log:
        log.write(json.dumps(line) + "\n")


def write_pending(state: Path, name: str, line: dict):
    folder = state / "pending"
    folder.mkdir(parents=True, exist_ok=True)
    # A temporary file and a rename, so that a signal during the write cannot leave half a file.
    temporary = folder / f".{name}.tmp"
    temporary.write_text(json.dumps({**line, "name": name}))
    os.replace(temporary, folder / f"{name}.json")


def in_flight(state: Path) -> list:
    folder = state / "pending"
    return [json.loads(path.read_text()) for path in sorted(folder.glob("*.json"))] if folder.exists() else []


def settle(state: Path, pending: dict, ending: dict, end: datetime.datetime) -> bool:
    """Move a job from pending/ to the log, under the lock, with the time at which the job's billing ended. False if
    another run settled it first."""
    path = state / "pending" / f"{pending['name']}.json"
    if not path.exists():
        return False
    line = {key: value for key, value in pending.items() if key not in ("name", "expires")}
    append_log(state, {**line, **ending, "ended": end.isoformat()})
    path.unlink()
    return True


def billing_end(pending: dict, status: dict, now: datetime.datetime) -> datetime.datetime:
    """When the job's billing ended: now, when the script sees the final status, or for a cancelled job its expiry,
    because Runpod's cancel does not stop a running handler, which bills until the job's own limit."""
    if status.get("status") == "CANCELLED" and pending.get("expires"):
        return max(now, datetime.datetime.fromisoformat(pending["expires"]))
    return now


def ending(kind: Kind, worst: float, status: dict) -> dict:
    """The settled part of a job's line, from Runpod's final status. A status without an execution time, such as a
    job whose result Runpod no longer keeps, costs its worst case. So does a cancelled job: Runpod's cancel does not
    stop a running handler, which bills until the job's own limit."""
    output = status.get("output") if isinstance(status.get("output"), dict) else {}
    # The SDK marks a job FAILED when the handler's result names an error, such as a failed command, so the handler's
    # own status wins whenever the job returned one.
    result = output.get("status") or ("failed" if status["status"] == "COMPLETED" else status["status"])
    gpu_seconds = status.get("executionTime", 0) / 1000
    measured = "executionTime" in status and status["status"] != "CANCELLED"
    return {"jobId": status.get("id"), "gpuSeconds": gpu_seconds, "delaySeconds": status.get("delayTime", 0) / 1000,
            "costUsd": cost_usd(kind, gpu_seconds) if measured else worst, "result": result,
            "exitCodes": [step["exitCode"] for step in output.get("steps", [])],
            "error": output.get("error") or status.get("error")}


def settle_lost(config: Config, state: Path, api, now: datetime.datetime):
    """Under the lock: settle each job in flight that ended while no run waited for it. A job whose run still holds
    its owner lock stays, so that its run gets its result and its endpoint stays. A job past its expiry whose
    end Runpod does not report may have run, so it costs its worst case. So does a job that Runpod no longer knows."""
    for pending in in_flight(state):
        if owned(state, pending["name"]):
            continue  # Its own run waits for it, and settles it with its result.
        expired = datetime.datetime.fromisoformat(pending["expires"]) <= now
        if pending.get("jobId") is None:
            if expired:
                settle(state, pending, {"gpuSeconds": None, "costUsd": pending["worstCaseUsd"], "result": "lost"}, now)
            continue
        try:
            status = api.status(pending["endpoint"], pending["jobId"])
        except urllib.error.HTTPError as error:
            if error.code != 404:
                continue
            status = {"id": pending["jobId"], "status": "UNKNOWN"}
        if status["status"] in FINAL | {"UNKNOWN"}:
            kind = config.kinds[pending.get("kind", "gpu")]
            settle(state, pending, ending(kind, pending["worstCaseUsd"], status), billing_end(pending, status, now))
        elif expired:
            # Past its queue time and its timeout, Runpod ran it or dropped it; its status no longer tells which.
            settle(state, pending, {"gpuSeconds": None, "costUsd": pending["worstCaseUsd"], "result": "lost"}, now)


def ended(line: dict) -> datetime.datetime:
    """When a logged job ended: the time that the script recorded, or for a line from before that record, its send
    time plus its wait and its execution."""
    if "ended" in line:
        return datetime.datetime.fromisoformat(line["ended"])
    return datetime.datetime.fromisoformat(line["time"]) + datetime.timedelta(
        seconds=(line.get("delaySeconds") or 0) + (line.get("gpuSeconds") or 0))


def hour_of(time: datetime.datetime) -> datetime.datetime:
    return time.astimezone(datetime.timezone.utc).replace(minute=0, second=0, microsecond=0)


def first_open_hour(now: datetime.datetime) -> datetime.datetime:
    """The first UTC hour whose bill can still grow: the hours before it are final (BILL_FINAL_HOURS)."""
    return hour_of(now) - datetime.timedelta(hours=BILL_FINAL_HOURS)


def ran(line: dict) -> bool:
    """A refused job was never sent, and a rejected one made no job, so neither ran or billed."""
    return line.get("result") not in ("refused", "rejected")


def counted(billed: float, hours: dict, lines: list, cutoff: datetime.datetime, since=None) -> float:
    """The spending that a cap counts in a window that starts at `since` (None: the account's whole life). `billed` is
    Runpod's bill of the window and `hours` maps the start of each recent UTC hour to its bill. The bill of an hour
    before `cutoff` is final, so it counts as it is. Each hour from `cutoff` on counts the larger of its own bill and
    the log's costs of the jobs that ended in it. So the count is never less than the bill, and a pod's bill in one
    hour does not hide the unbilled jobs of another. A job that ran across an hour's end counts whole in the hour of
    its end, beside the part of it that an earlier hour billed, so the count errs high. One hour that holds both
    spending that the log lacks and jobs whose bill is not in yet counts only the larger of the two."""
    def inside(time):
        return time >= cutoff and (since is None or time >= since)
    logged = {}
    for line in lines:
        if inside(end := ended(line)):
            logged[hour_of(end)] = logged.get(hour_of(end), 0.0) + line.get("costUsd", 0.0)
    open_hours = {start: amount for start, amount in hours.items() if inside(start)}
    return billed - sum(open_hours.values()) + sum(
        max(open_hours.get(hour, 0.0), logged.get(hour, 0.0)) for hour in set(open_hours) | set(logged))


def refusal(config: Config, state: Path, api, worst: float, now: datetime.datetime) -> str | None:
    """Why the job would pass a cap, or None. Each cap counts Runpod's bill of the hours whose bill is final, and the
    larger of the bill and the log's estimates for the hours after them (`counted`). The bill holds the account's
    other spending too, and the log covers the hours that the bill does not hold yet."""
    lines = log_lines(state)
    now = now.astimezone(datetime.timezone.utc)
    today = now.date()
    cutoff = first_open_hour(now)
    hours = api.billed_hours(BILL_FINAL_HOURS + 1)
    midnight = datetime.datetime.combine(today, datetime.time(), datetime.timezone.utc)
    spent_today = counted(api.billed_today(), hours, lines, cutoff, since=midnight)
    spent = counted(api.billed_total(), hours, lines, cutoff)
    held = sum(pending["worstCaseUsd"] for pending in in_flight(state))
    cap = config.daily_cap(today)
    if spent_today + held + worst > cap:
        return (f"today's ${spent_today:.2f} plus ${held:.2f} in flight plus this job's worst case ${worst:.2f} "
                f"passes the daily cap of ${cap:.2f}")
    if spent + held + worst > config.total_cap_usd:
        return (f"the ${spent:.2f} spent plus ${held:.2f} in flight plus this job's worst case ${worst:.2f} "
                f"passes the total cap of ${config.total_cap_usd:.2f}")
    return None


def commits(request: Request, repo: Path) -> dict:
    """tree name -> the commit of its ref."""
    return {name: git(repo, "rev-parse", "--verify", f"{ref}^{{commit}}").decode().strip()
            for name, ref in request.trees.items()}


def job_ref(agent: str, sha: str, now: datetime.datetime) -> str:
    plain = re.sub(r"[^A-Za-z0-9_-]+", "-", agent).strip("-") or "agent"
    return f"{JOB_REFS}/{now.astimezone(datetime.timezone.utc).date().isoformat()}/{plain}/{sha}"


def prune(github, now: datetime.datetime):
    """Delete the hidden refs of the days before the last REF_DAYS."""
    oldest = (now.astimezone(datetime.timezone.utc) - datetime.timedelta(days=REF_DAYS)).date().isoformat()
    day = re.compile(rf"^{JOB_REFS}/(\d{{4}}-\d{{2}}-\d{{2}})/")
    github.delete([ref for ref in github.refs() if (found := day.match(ref)) and found.group(1) < oldest])


def git(repo: Path, *args) -> bytes:
    return subprocess.run(["git", *args], cwd=repo, check=True, capture_output=True).stdout


def text(output) -> str:
    """A command's captured output as text, whether it was captured as bytes or as text."""
    return (output.decode(errors="replace") if isinstance(output, bytes) else output or "").strip()


def git_maybe(repo: Path, *args) -> str:
    """git's output, or "" when git fails, such as for a path that the commit does not have."""
    done = subprocess.run(["git", *args], cwd=repo, capture_output=True, text=True)
    return done.stdout.strip() if done.returncode == 0 else ""


def submit(request: Request, *, config: Config, api, registry, github, scan=scan, state: Path, repo: Path, now=None,
           poll_seconds: float = 5.0, out=print) -> str:
    """Run one job and return its result: ok, failed, timed out or refused from the handler, refused by the script, or
    Runpod's status when the job did not complete. If the script stops before the job's final status, the job stays
    in pending/, with its id once Runpod accepted it, and the next run settles it. The run holds the job's owner lock
    throughout. The order of the steps is the safety rule: nothing is published before the scan, the image's choice
    and the caps, and nothing is sent before the commits are published."""
    now = now or (lambda: datetime.datetime.now(datetime.timezone.utc))
    name = uuid.uuid4().hex
    with owning(state, name):
        kind = config.kinds[request.kind]
        seconds = min(request.seconds, kind.job_seconds)
        execution = execution_seconds(kind, seconds)
        worst = worst_case_usd(kind, seconds)
        pending = {"time": now().isoformat(), "agent": request.agent, "kind": request.kind, "endpoint": None,
                   "image": None,
                   "trees": {tree: {"ref": ref} for tree, ref in request.trees.items()},
                   "commands": request.commands, "outputs": request.outputs, "worstCaseUsd": worst, "jobId": None,
                   "expires": (now() + datetime.timedelta(seconds=QUEUE_SECONDS + execution)).isoformat(), "name": name}
        try:
            shas = commits(request, repo)
            trees = {tree: {"sha": sha, "ref": job_ref(request.agent, sha, now())} for tree, sha in shas.items()}
            pending["trees"] = {tree: {"ref": request.trees[tree], **trees[tree]} for tree in trees}
            shells = {shell_of(repo, sha) for sha in shas.values()}
            if len(shells) > 1:
                raise Refused(f"the trees have {len(shells)} dev shells, and a job runs in one; give the trees the same "
                              f"{', '.join(SHELL_PATHS)}")
            findings = scan(repo, github.new_commits(sorted(set(shas.values()))))
            if findings:
                raise Refused(f"the secrets scan found {'; '.join(findings)}; nothing was published")
            images = images_for(config, registry, repo, shells.pop(), list(shas.values()),
                                         git(repo, "rev-parse", "--verify", MAIN).decode().strip())
            pending["image"] = images[0]
            with locked(state):
                settle_lost(config, state, api, now())
                reason = refusal(config, state, api, worst, now())
                if reason is not None:
                    raise Refused(reason)
                pending["endpoint"], pending["image"] = route(config, api, state, images, request.kind)
                write_pending(state, name, pending)
        except (Refused, subprocess.CalledProcessError, OSError, ValueError, KeyError) as error:
            # Before the record of the job in flight, nothing was published or sent. A failed git command, registry or
            # Runpod call refuses the job too, and the log says why.
            reason = str(error)
            if isinstance(error, subprocess.CalledProcessError):
                reason = f"{' '.join(map(str, error.cmd))} failed: {text(error.stderr)}"
            line = {key: value for key, value in pending.items() if key not in ("name", "expires")}
            with locked(state):
                append_log(state, {**line, "gpuSeconds": 0.0, "costUsd": 0.0, "result": "refused", "error": reason})
            out(f"refused: {reason}")
            return "refused"
        body = {"input": {"protocol": PROTOCOL, "repository": config.repository, "trees": trees,
                          "commands": request.commands, "outputs": request.outputs, "seconds": seconds},
                "policy": {"executionTimeout": execution * 1000, "ttl": QUEUE_SECONDS * 1000}}
        try:
            for tree in trees.values():
                github.push(tree["sha"], tree["ref"])
            pending["jobId"] = api.submit(pending["endpoint"], body)
        except (urllib.error.HTTPError, subprocess.CalledProcessError) as error:
            # A failed push sends nothing, and Runpod answered /run without a job, so neither bills. A timeout or a lost
            # connection of /run stays ambiguous, and the job stays in flight until it expires.
            reason = (f"/run answered {error.code} {error.reason}" if isinstance(error, urllib.error.HTTPError)
                      else f"the push of the trees' commits failed: {text(error.stderr)}")
            with locked(state):
                settle(state, pending, {"gpuSeconds": 0.0, "costUsd": 0.0, "result": "rejected", "error": reason},
                       now())
            raise
        with locked(state):
            write_pending(state, name, pending)
        out(f"job {pending['jobId']} sent to endpoint {pending['endpoint']}; worst case ${worst:.2f}")
        try:
            prune(github, now())
        except subprocess.CalledProcessError as error:
            out(f"the old hidden refs stay for now: {text(error.stderr)}")
        status = wait(api, pending["endpoint"], pending["jobId"], poll_seconds)
        settled = ending(kind, worst, status)
        with locked(state):
            settle(state, pending, settled, billing_end(pending, status, now()))
        write_outputs(request.out, status, settled["result"])
        out(f"job {pending['jobId']}: {settled['result']}, {settled['gpuSeconds']:.0f} seconds of execution, counted as "
            f"${settled['costUsd']:.4f}; result in {request.out}")
        return settled["result"]


def wait(api, endpoint: str, job: str, poll_seconds: float) -> dict:
    """The job's final status. Ctrl-C, SIGTERM or SIGHUP cancels the job and still waits for its final status, which
    holds its cost. The cancel then ignores those signals, so a second one cannot cut it short and leave the job
    running to its limit."""
    try:
        while True:
            status = api.status(endpoint, job)
            if status["status"] in FINAL:
                return status
            time.sleep(poll_seconds)
    except KeyboardInterrupt:
        for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            signal.signal(number, signal.SIG_IGN)
        api.cancel(endpoint, job)
        while True:
            status = api.status(endpoint, job)
            if status["status"] in FINAL:
                return status
            time.sleep(max(poll_seconds, 1.0))


def write_outputs(folder: Path, status: dict, result: str):
    output = status.get("output") if isinstance(status.get("output"), dict) else {}
    folder.mkdir(parents=True, exist_ok=True)
    files = output.get("files", {})
    for name, data in files.items():
        path = (folder / name).resolve()
        if folder.resolve() not in path.parents:
            continue  # The handler sends only paths inside its work folder; a name that leaves --out is not written.
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(base64.b64decode(data))
    kept = {key: value for key, value in output.items() if key not in ("files", "status")}
    (folder / "result.json").write_text(json.dumps(
        {"id": status.get("id"), "status": result, "runpodStatus": status["status"],
         "executionTime": status.get("executionTime"), "delayTime": status.get("delayTime"),
         "error": status.get("error"), "files": sorted(files), **kept},
        indent=2) + "\n")


def endpoint_request(config: Config, kind: str, commit: str) -> dict:
    """The endpoint of one kind and image. It bounds the spending by itself too: it keeps no minimum of workers, runs at
    most the kind's share at once, and stops each job at the kind's execution timeout."""
    return {"name": f"tertius-{kind}-{commit[:12]}-{uuid.uuid4().hex[:4]}", "type": "QUEUE", "image": f"{config.image_repository}:{commit}",
            "flashboot": "FLASHBOOT", "scaling": {"type": "QUEUE_DELAY", "queueDelay": 4},
            "workers": {"min": 0, "max": config.kinds[kind].workers, "idleTimeout": IDLE_SECONDS},
            "timeout": config.kinds[kind].endpoint_timeout_seconds * 1000, **HARDWARE[kind]}


def key_file() -> Path | None:
    """The file that holds the API key: TERTIUS_RUNPOD_KEY_FILE, else the path that KEY_POINTER holds. An agent runs
    the script and never reads the key itself."""
    if os.environ.get("TERTIUS_RUNPOD_KEY_FILE"):
        return Path(os.environ["TERTIUS_RUNPOD_KEY_FILE"])
    return Path(KEY_POINTER.read_text().strip()) if KEY_POINTER.exists() else None


def positive(text: str) -> int:
    value = int(text)
    if value <= 0:
        raise argparse.ArgumentTypeError(f"{text} is not a positive number of seconds")
    return value


# Student's t at 97.5 % for 1 to 30 degrees of freedom, for the 95 % interval of a mean.
T_975 = [12.7062, 4.3027, 3.1824, 2.7764, 2.5706, 2.4469, 2.3646, 2.3060, 2.2622, 2.2281, 2.2010, 2.1788, 2.1604,
         2.1448, 2.1314, 2.1199, 2.1098, 2.1009, 2.0930, 2.0860, 2.0796, 2.0739, 2.0687, 2.0639, 2.0595, 2.0555, 2.0518,
         2.0484, 2.0452, 2.0423]


def t_975(degrees: int) -> float:
    """Student's t at 97.5 %: the table up to 30 degrees of freedom, then the Cornish-Fisher series around the normal
    quantile, which is within 0.0001 of the exact value there."""
    if degrees <= len(T_975):
        return T_975[degrees - 1]
    z, n = 1.959964, float(degrees)
    return (z + (z ** 3 + z) / (4 * n) + (5 * z ** 5 + 16 * z ** 3 + 3 * z) / (96 * n ** 2)
            + (3 * z ** 7 + 19 * z ** 5 + 17 * z ** 3 - 15 * z) / (384 * n ** 3))


def summary(values: list) -> dict:
    """The count, mean, standard deviation, half width of the 95 % interval of the mean, median, minimum and maximum
    of the values. One value has no spread, so its deviation and interval are None."""
    deviation = statistics.stdev(values) if len(values) > 1 else None
    return {"count": len(values), "mean": statistics.fmean(values), "sd": deviation,
            "ci95": t_975(len(values) - 1) * deviation / len(values) ** 0.5 if deviation is not None else None,
            "median": statistics.median(values), "min": min(values), "max": max(values)}


def cost_groups(lines: list, config: Config) -> dict:
    """group name -> the settled estimate and the execution-only cost of each job that ran in it. The groups are all
    jobs, each kind and each agent. A refused or rejected job never ran. A lost job has no execution time, so it has
    only its settled cost. A line from before the kinds is a GPU job."""
    groups = {}
    for line in lines:
        if not ran(line):
            continue
        kind = line.get("kind", "gpu")
        for name in ("all", f"kind {kind}", f"agent {line.get('agent')}"):
            group = groups.setdefault(name, {"settled": [], "execution": []})
            group["settled"].append(line.get("costUsd", 0.0))
            if line.get("gpuSeconds") is not None:
                group["execution"].append(line["gpuSeconds"] * config.kinds[kind].rate_per_second)
    order = ["all"] * ("all" in groups) + sorted(name for name in groups if name.startswith("kind ")) + sorted(
        name for name in groups if name.startswith("agent "))
    return {name: groups[name] for name in order}


def bill_against_estimate(lines: list, hours: dict, cutoff: datetime.datetime) -> dict:
    """Runpod's bill of the final hours, from the hour of the first job that ran up to `cutoff`, beside the settled
    estimate of the jobs that ended in those hours. Runpod bills by the hour, not by the job, so only the sums
    compare. A job that ran across an hour's end can put part of its bill in the hour after its end."""
    jobs = [line for line in lines if ran(line)]
    if not jobs:
        return {"start": None, "end": cutoff, "billed": 0.0, "jobs": 0, "settled": 0.0}
    start = hour_of(min(datetime.datetime.fromisoformat(line["time"]) for line in jobs))
    if hours:
        start = max(start, min(hours))  # The bill may not reach back to the first job.
    inside = [line for line in jobs if start <= ended(line) < cutoff]
    return {"start": start, "end": cutoff, "jobs": len(inside),
            "billed": sum(amount for hour, amount in hours.items() if start <= hour < cutoff),
            "settled": sum(line.get("costUsd", 0.0) for line in inside)}


SERVERLESS = ("serverlessGpuAmount", "serverlessCpuAmount", "serverlessDiskAmount", "serverlessFeeAmount")


def read_bill(api, lines: list, now: datetime.datetime):
    """bill_against_estimate over the hours of the log, with the bill of serverless work alone, or why Runpod's bill
    could not be read."""
    times = [datetime.datetime.fromisoformat(line["time"]) for line in lines]
    if not times:
        return "the audit log is empty"
    count = int((now - min(times)).total_seconds() // 3600) + 2
    try:
        hours = api.billed_hours(count, SERVERLESS)
    except (OSError, urllib.error.URLError, KeyError, ValueError) as error:
        return f"the billing request failed: {error}"
    return bill_against_estimate(lines, hours, first_open_hour(now))


def stats_text(lines: list, config: Config, compared) -> str:
    """The report of `gpu_job.py stats`: a table of each cost figure per group, then the bill's line. `compared` is
    bill_against_estimate's result, or the reason why the bill was not read."""
    def money(value):
        return "-" if value is None else f"{value:.4f}"
    groups = cost_groups(lines, config)
    count = len(groups["all"]["settled"]) if "all" in groups else 0
    out = [f"Cost per job that ran, in US dollars, from the audit log ({count} jobs)."]
    for figure, title in (("settled", "Settled estimate: (execution + 70 s) times the kind's price, or the worst case"),
                          ("execution", "Execution only: the execution time times the kind's price")):
        out += ["", title + ":",
                f"{'group':<32}{'jobs':>5}{'mean':>9}{'sd':>9}{'±95%':>9}{'median':>9}{'min':>9}{'max':>9}"]
        for name, group in groups.items():
            if group[figure]:
                s = summary(group[figure])
                out.append(f"{name:<32}{s['count']:>5}" + "".join(
                    f"{money(s[key]):>9}" for key in ("mean", "sd", "ci95", "median", "min", "max")))
    out.append("")
    if isinstance(compared, str):
        out.append(f"Runpod's bill: not read ({compared}).")
    elif compared["jobs"] == 0:
        out.append("Runpod's bill: no job ended in a final hour yet.")
    else:
        ratio = (f", so the bill is {compared['billed'] / compared['settled']:.2f} times the estimate"
                 if compared["settled"] > 0 else "")
        out.append(f"Runpod's bill of serverless work in the final hours from {compared['start']:%Y-%m-%d %H:00} to "
                   f"{compared['end']:%Y-%m-%d %H:00} UTC: ${compared['billed']:.4f} for the {compared['jobs']} jobs "
                   f"that ended in them, ${compared['billed'] / compared['jobs']:.4f} per job. Their settled estimate: "
                   f"${compared['settled']:.4f}{ratio}.")
    return "\n".join(out)


def interrupt(signum, frame):
    raise KeyboardInterrupt


def main(argv: list) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    job = commands.add_parser("submit", help="run one job on an endpoint of its kind")
    job.add_argument("--agent", required=True, help="who sends the job, for the audit log")
    job.add_argument("--tree", action="append", required=True, metavar="NAME=REF",
                     help="a tree in the job's work folder: the commit of REF")
    job.add_argument("--run", action="append", default=[], metavar="CMD", help="a command, run in the dev shell")
    job.add_argument("--output", action="append", default=[], metavar="GLOB", help="files to send back")
    job.add_argument("--kind", choices=["gpu", "cpu"], default="gpu",
                     help="gpu: the RTX 2000 Ada (the default); cpu: 2 vCPUs, for Stryker and the CPU-only tests")
    job.add_argument("--timeout", type=positive, default=None,
                     help="seconds for all commands; at most the kind's job_seconds in endpoint.json")
    job.add_argument("--out", type=Path, required=True, help="folder for the output files and result.json")
    request = commands.add_parser("endpoint-request", help="print the request that creates the endpoint of a kind and "
                                  "an image, and send nothing")
    request.add_argument("commit", help="the full commit that the image baked")
    request.add_argument("--kind", choices=["gpu", "cpu"], default="gpu")
    commands.add_parser("stats", help="print the cost per job that ran from the audit log, and the bill beside it; "
                        "it sends no job and changes nothing")
    args = parser.parse_args(argv)
    config = Config.load()
    if args.command == "endpoint-request":
        print(json.dumps(endpoint_request(config, args.kind, args.commit), indent=2))
        return 0
    key = key_file()
    if args.command == "stats":
        lines = log_lines(STATE)
        compared = (read_bill(RunpodApi(key), lines, datetime.datetime.now(datetime.timezone.utc)) if key is not None
                    else f"no key: {KEY_POINTER} names no key file")
        print(stats_text(lines, config, compared))
        return 0
    if key is None:
        print(f"no key: write the path of the file that holds the Runpod API key into {KEY_POINTER}", file=sys.stderr)
        return 2
    trees = dict(tree.split("=", 1) for tree in args.tree)
    request = Request(agent=args.agent, trees=trees, commands=args.run, outputs=args.output,
                      seconds=args.timeout or config.kinds[args.kind].job_seconds, out=args.out, kind=args.kind)
    repo = Path(git(Path.cwd(), "rev-parse", "--show-toplevel").decode().strip())
    # An agent's tool stops a command with SIGTERM, and a closed terminal sends SIGHUP. Both cancel as Ctrl-C does.
    signal.signal(signal.SIGTERM, interrupt)
    signal.signal(signal.SIGHUP, interrupt)
    result = submit(request, config=config, api=RunpodApi(key), registry=Registry(config.image_repository),
                    github=GitHub(repo, config.repository), state=STATE, repo=repo)
    return 0 if result == "ok" else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
