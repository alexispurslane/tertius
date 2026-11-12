#!/usr/bin/env python3
"""Tool code (tools/README.md, "GPU jobs on Runpod"): the one script through which an agent runs a GPU job on the
Runpod serverless endpoint, within the spending caps.

    python3 -I tools/runpod/gpu_job.py submit --agent NAME --tree main=main --tree branch=HEAD \\
        --run 'cd branch && dotnet build src/Tertius.Demo -c Release' --output 'branch/out/*.json' --out DIR

Each tree is a copy of the source that the image baked, plus the binary diff from that commit to the tree's ref. The
script refuses a job before it sends it when the job's worst case would pass the daily cap or the total cap. It counts
the jobs of the audit log, the jobs in flight and Runpod's billed total. It writes one line per job, refusals too, to
the audit log, and the job's output files and result under --out.

The API key stays in its file (TERTIUS_RUNPOD_KEY_FILE). The script reads it for each request and never prints it.
The standard library only.
"""

import argparse
import base64
import datetime
import fcntl
import json
import os
import subprocess
import sys
import time
import urllib.request
import uuid
from dataclasses import dataclass, field
from pathlib import Path

CONFIG_FILE = Path(__file__).with_name("endpoint.json")
STATE = Path.home() / ".local/state/tertius/gpu-jobs"
JOB_API = "https://api.runpod.ai/v2"
ACCOUNT_API = "https://api.runpod.io/v2"

# A job's worst case bills its execution timeout plus a worker's start and its idle time after the job: a flex worker
# bills from its start until it stops, and the endpoint keeps it for workers.idleTimeout seconds after its last job.
START_ALLOWANCE_SECONDS = 120
IDLE_SECONDS = 10
# The handler's copy of the trees before the commands and its collection of the outputs after them run inside the
# execution timeout too, so the commands get this much less.
HANDLER_MARGIN_SECONDS = 60
# A job that waits longer than this in the queue expires unrun. Its reservation lasts this long plus its timeout.
QUEUE_SECONDS = 2 * 3600
FINAL = {"COMPLETED", "FAILED", "CANCELLED", "TIMED_OUT"}


@dataclass(frozen=True)
class Config:
    """endpoint: the Runpod endpoint id. base: the commit that the image baked. job_seconds: the most time that the
    commands of one job get. endpoint_timeout_seconds: the endpoint's execution timeout, the hard bound per job."""

    endpoint: str
    base: str
    image: str
    rate_per_second: float
    daily_cap_usd: float
    total_cap_usd: float
    job_seconds: int
    endpoint_timeout_seconds: int

    @staticmethod
    def load(path: Path = CONFIG_FILE) -> "Config":
        return Config(**json.loads(path.read_text()))


@dataclass
class Request:
    agent: str
    trees: dict  # tree name -> git ref
    commands: list
    outputs: list
    seconds: int
    out: Path
    shas: dict = field(default_factory=dict)


class RunpodApi:
    """The four calls that the script makes, with the key read from its file for each call."""

    def __init__(self, key_file: Path):
        self.key_file = key_file

    def call(self, method: str, url: str, body=None) -> dict:
        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(url, data=data, method=method, headers={
            "Authorization": "Bearer " + self.key_file.read_text().strip(), "Content-Type": "application/json"})
        with urllib.request.urlopen(request, timeout=60) as reply:
            return json.loads(reply.read() or b"{}")

    def billed_total(self) -> float:
        return float(self.call("GET", f"{ACCOUNT_API}/billing")["metadata"]["totals"]["totalAmount"])

    def submit(self, endpoint: str, body: dict) -> str:
        return self.call("POST", f"{JOB_API}/{endpoint}/run", body)["id"]

    def status(self, endpoint: str, job: str) -> dict:
        return self.call("GET", f"{JOB_API}/{endpoint}/status/{job}")

    def cancel(self, endpoint: str, job: str):
        self.call("POST", f"{JOB_API}/{endpoint}/cancel/{job}")

    def create_endpoint(self, body: dict) -> dict:
        return self.call("POST", f"{ACCOUNT_API}/serverless", body)


def execution_seconds(config: Config, seconds: int) -> int:
    """The job's execution timeout: its commands' time plus the handler's margin, within the endpoint's timeout."""
    return min(config.endpoint_timeout_seconds, min(seconds, config.job_seconds) + HANDLER_MARGIN_SECONDS)


def worst_case_usd(config: Config, seconds: int) -> float:
    return (execution_seconds(config, seconds) + START_ALLOWANCE_SECONDS) * config.rate_per_second


def log_lines(state: Path) -> list:
    path = state / "jobs.jsonl"
    return [json.loads(line) for line in path.read_text().splitlines() if line.strip()] if path.exists() else []


def append_log(state: Path, line: dict):
    state.mkdir(parents=True, exist_ok=True)
    with (state / "jobs.jsonl").open("a") as log:
        log.write(json.dumps(line) + "\n")


def reserve(state: Path, name: str, cost: float, *, when: datetime.datetime, seconds: float = QUEUE_SECONDS + 3600):
    """Hold `cost` against the caps until the job ends, or until `seconds` pass if the script dies first."""
    folder = state / "reserved"
    folder.mkdir(parents=True, exist_ok=True)
    expires = when + datetime.timedelta(seconds=seconds)
    (folder / f"{name}.json").write_text(json.dumps({"costUsd": cost, "expires": expires.isoformat()}))


def release(state: Path, name: str):
    (state / "reserved" / f"{name}.json").unlink(missing_ok=True)


def reserved(state: Path, when: datetime.datetime | None = None) -> float:
    folder = state / "reserved"
    if not folder.exists():
        return 0.0
    holds = [json.loads(path.read_text()) for path in folder.glob("*.json")]
    return sum(hold["costUsd"] for hold in holds
               if when is None or datetime.datetime.fromisoformat(hold["expires"]) > when)


def refusal(config: Config, state: Path, api, worst: float, now: datetime.datetime) -> str | None:
    """Why the job would pass a cap, or None. The billed total is the authority for the total cap, but Runpod's
    billing lags its jobs, so the audit log's sum counts when it is the larger."""
    lines = log_lines(state)
    today = now.astimezone(datetime.timezone.utc).date()
    spent_today = sum(line.get("costUsd", 0.0) for line in lines
                      if datetime.datetime.fromisoformat(line["time"]).astimezone(datetime.timezone.utc).date() == today)
    held = reserved(state, now)
    spent = max(sum(line.get("costUsd", 0.0) for line in lines), api.billed_total())
    if spent_today + held + worst > config.daily_cap_usd:
        return (f"today's ${spent_today:.2f} plus ${held:.2f} in flight plus this job's worst case ${worst:.2f} "
                f"passes the daily cap of ${config.daily_cap_usd:.2f}")
    if spent + held + worst > config.total_cap_usd:
        return (f"the ${spent:.2f} spent plus ${held:.2f} in flight plus this job's worst case ${worst:.2f} "
                f"passes the total cap of ${config.total_cap_usd:.2f}")
    return None


def patches(request: Request, config: Config, repo: Path) -> dict:
    """Each tree's binary diff from the baked commit to its ref, base64-encoded, and its ref's commit in request.shas."""
    trees = {}
    for name, ref in request.trees.items():
        sha = git(repo, "rev-parse", "--verify", f"{ref}^{{commit}}").decode().strip()
        request.shas[name] = sha
        trees[name] = base64.b64encode(git(repo, "diff", "--binary", config.base, sha)).decode()
    return trees


def git(repo: Path, *args) -> bytes:
    return subprocess.run(["git", *args], cwd=repo, check=True, capture_output=True).stdout


def submit(request: Request, *, config: Config, api, state: Path, repo: Path, now=None, poll_seconds: float = 5.0,
           out=print) -> str:
    """Run one job and return its result: ok, failed, timed out or refused from the handler, refused by the caps, or
    Runpod's status when the job did not complete."""
    now = now or (lambda: datetime.datetime.now(datetime.timezone.utc))
    trees = patches(request, config, repo)
    seconds = min(request.seconds, config.job_seconds)
    execution = execution_seconds(config, seconds)
    worst = worst_case_usd(config, seconds)
    line = {"time": now().isoformat(), "agent": request.agent, "endpoint": config.endpoint, "base": config.base,
            "trees": {name: {"ref": ref, "sha": request.shas[name]} for name, ref in request.trees.items()},
            "commands": request.commands, "outputs": request.outputs, "worstCaseUsd": worst}
    hold = uuid.uuid4().hex
    state.mkdir(parents=True, exist_ok=True)
    # The lock spans the check and the reservation, so two agents cannot both take the last of a cap.
    with (state / "lock").open("w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        reason = refusal(config, state, api, worst, now())
        if reason is None:
            reserve(state, hold, worst, when=now(), seconds=QUEUE_SECONDS + execution + START_ALLOWANCE_SECONDS)
    if reason is not None:
        append_log(state, {**line, "jobId": None, "gpuSeconds": 0.0, "costUsd": 0.0, "result": "refused",
                           "error": reason})
        out(f"refused: {reason}")
        return "refused"
    try:
        body = {"input": {"base": config.base, "trees": trees, "commands": request.commands,
                          "outputs": request.outputs, "seconds": seconds},
                "policy": {"executionTimeout": execution * 1000, "ttl": QUEUE_SECONDS * 1000}}
        job = api.submit(config.endpoint, body)
        out(f"job {job} sent to endpoint {config.endpoint}; worst case ${worst:.2f}")
        status = wait(api, config.endpoint, job, poll_seconds)
        gpu_seconds = status.get("executionTime", 0) / 1000
        output = status.get("output") if isinstance(status.get("output"), dict) else {}
        result = output.get("status", "failed") if status["status"] == "COMPLETED" else status["status"]
        write_outputs(request.out, status, output)
        append_log(state, {**line, "jobId": job, "gpuSeconds": gpu_seconds,
                           "delaySeconds": status.get("delayTime", 0) / 1000,
                           "costUsd": gpu_seconds * config.rate_per_second, "result": result,
                           "exitCodes": [step["exitCode"] for step in output.get("steps", [])],
                           "error": output.get("error") or status.get("error")})
        out(f"job {job}: {result}, {gpu_seconds:.0f} GPU seconds, ${gpu_seconds * config.rate_per_second:.4f}; "
            f"result in {request.out}")
        return result
    finally:
        release(state, hold)


def wait(api, endpoint: str, job: str, poll_seconds: float) -> dict:
    """The job's final status. Ctrl-C cancels the job and still waits for its final status, which holds its cost."""
    try:
        while True:
            status = api.status(endpoint, job)
            if status["status"] in FINAL:
                return status
            time.sleep(poll_seconds)
    except KeyboardInterrupt:
        api.cancel(endpoint, job)
        while True:
            status = api.status(endpoint, job)
            if status["status"] in FINAL:
                return status
            time.sleep(max(poll_seconds, 1.0))


def write_outputs(folder: Path, status: dict, output: dict):
    folder.mkdir(parents=True, exist_ok=True)
    files = output.get("files", {})
    for name, data in files.items():
        path = (folder / name).resolve()
        if folder.resolve() not in path.parents:
            continue  # The handler sends only paths inside its work folder; a name that leaves --out is not written.
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(base64.b64decode(data))
    kept = {key: value for key, value in output.items() if key != "files"}
    (folder / "result.json").write_text(json.dumps(
        {"id": status.get("id"), "status": status["status"], "executionTime": status.get("executionTime"),
         "delayTime": status.get("delayTime"), "error": status.get("error"), "files": sorted(files), **kept},
        indent=2) + "\n")


def endpoint_request(config: Config, name: str = "tertius-gpu") -> dict:
    """The endpoint bounds the spending by itself too: no worker runs without a job, at most six run at once, and
    each job stops at the execution timeout."""
    return {"name": name, "type": "QUEUE", "image": config.image, "flashboot": True,
            "workers": {"min": 0, "max": 6, "idleTimeout": IDLE_SECONDS},
            "timeout": config.endpoint_timeout_seconds * 1000, "gpu": {"pools": ["AMPERE_16"]}}


def main(argv: list) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    job = commands.add_parser("submit", help="run one job on the endpoint")
    job.add_argument("--agent", required=True, help="who sends the job, for the audit log")
    job.add_argument("--tree", action="append", required=True, metavar="NAME=REF",
                     help="a tree in the job's work folder: the baked source plus the diff to REF")
    job.add_argument("--run", action="append", default=[], metavar="CMD", help="a command, run in the dev shell")
    job.add_argument("--output", action="append", default=[], metavar="GLOB", help="files to send back")
    job.add_argument("--timeout", type=int, default=None, help="seconds for all commands; at most the config's")
    job.add_argument("--out", type=Path, required=True, help="folder for the output files and result.json")
    commands.add_parser("endpoint-request", help="print the request that creates the endpoint, and send nothing")
    commands.add_parser("create-endpoint", help="create the endpoint; write its id into endpoint.json after")
    args = parser.parse_args(argv)
    config = Config.load()
    if args.command == "endpoint-request":
        print(json.dumps(endpoint_request(config), indent=2))
        return 0
    if "TERTIUS_RUNPOD_KEY_FILE" not in os.environ:
        print("set TERTIUS_RUNPOD_KEY_FILE to the file that holds the Runpod API key", file=sys.stderr)
        return 2
    api = RunpodApi(Path(os.environ["TERTIUS_RUNPOD_KEY_FILE"]))
    if args.command == "create-endpoint":
        if not config.image:
            print(f"{CONFIG_FILE} names no image yet", file=sys.stderr)
            return 2
        reply = api.create_endpoint(endpoint_request(config))
        print(json.dumps({"id": reply.get("id"), "name": reply.get("name")}))
        return 0
    if not (config.endpoint and config.base):
        print(f"{CONFIG_FILE} names no endpoint or no baked commit yet (tools/README.md, \"GPU jobs on Runpod\")",
              file=sys.stderr)
        return 2
    trees = dict(tree.split("=", 1) for tree in args.tree)
    request = Request(agent=args.agent, trees=trees, commands=args.run, outputs=args.output,
                      seconds=args.timeout or config.job_seconds, out=args.out)
    repo = Path(git(Path.cwd(), "rev-parse", "--show-toplevel").decode().strip())
    result = submit(request, config=config, api=api, state=STATE, repo=repo)
    return 0 if result == "ok" else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
