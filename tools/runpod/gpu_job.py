#!/usr/bin/env python3
"""Tool code (tools/README.md, "GPU jobs on Runpod"): the one script through which an agent runs a GPU job on the
Runpod serverless endpoint, within the spending caps.

    python3 -I tools/runpod/gpu_job.py submit --agent NAME --tree main=main --tree branch=HEAD \\
        --run 'cd branch && dotnet build src/Tertius.Demo -c Release' --output 'branch/out/*.json' --out DIR

Each tree is a copy of the source that the image baked, plus the binary diff from that commit to the tree's ref. The
script refuses a job before it sends it when the job's worst case would pass the daily cap or the total cap. It counts
the jobs of the audit log, the jobs in flight and Runpod's billed total. It writes one line per job, refusals too, to
the audit log, and the job's output files and result under --out.

The API key stays in its file, which ~/.config/tertius/runpod-key-file names. The script reads it for each request
and never prints it.
The standard library only.
"""

import argparse
import base64
import datetime
import fcntl
import json
import os
import re
import signal
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
from dataclasses import dataclass, field
from pathlib import Path

CONFIG_FILE = Path(__file__).with_name("endpoint.json")
STATE = Path.home() / ".local/state/tertius/gpu-jobs"
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
# The handler's copy of the trees before the commands and its collection of the outputs after them run inside the
# execution timeout too, so the commands get this much less.
HANDLER_MARGIN_SECONDS = 60
# A job that waits longer than this in the queue expires unrun.
QUEUE_SECONDS = 2 * 3600
FINAL = {"COMPLETED", "FAILED", "CANCELLED", "TIMED_OUT"}
COMMIT = re.compile(r"^[0-9a-f]{40}$")
# The endpoint runs on the RTX 2000 Ada only, so that the timings of different jobs come from one kind of card. The
# 16 GB pool also holds these three, and a card that Runpod adds to the pool later becomes eligible.
POOL = "AMPERE_16"
OTHER_CARDS = ["NVIDIA RTX A4000", "NVIDIA RTX A4500", "NVIDIA RTX 4000 Ada Generation"]
# Container disk in GB: the image's files, plus a copy of the baked source with its build outputs per tree.
DISK_GB = 60


class NoBase(Exception):
    """The endpoint's image names no baked commit, or the named image is not public."""


@dataclass(frozen=True)
class Config:
    """endpoint: the Runpod endpoint id. image_repository: the images, each tagged with the commit that it baked.
    rate_per_second: the price of a worker in USD per second. daily_cap_usd and total_cap_usd: the caps, per UTC day
    and in total. job_seconds: the most time that the commands of one job get. endpoint_timeout_seconds: the
    endpoint's execution timeout, the hard bound per job, at least job_seconds plus the handler's margin. The baked
    commit is not here: the endpoint's image tag is its one store."""

    endpoint: str
    image_repository: str
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

    def submit(self, endpoint: str, body: dict) -> str:
        return self.call("POST", f"{JOB_API}/{endpoint}/run", body)["id"]

    def status(self, endpoint: str, job: str) -> dict:
        return self.call("GET", f"{JOB_API}/{endpoint}/status/{job}")

    def cancel(self, endpoint: str, job: str):
        self.call("POST", f"{JOB_API}/{endpoint}/cancel/{job}")

    def endpoint(self, endpoint: str) -> dict:
        return self.call("GET", f"{ACCOUNT_API}/serverless/{endpoint}")

    def create_endpoint(self, body: dict) -> dict:
        return self.call("POST", f"{ACCOUNT_API}/serverless", body)

    def update_endpoint(self, endpoint: str, body: dict) -> dict:
        return self.call("PATCH", f"{ACCOUNT_API}/serverless/{endpoint}", body)

    def workers(self, endpoint: str) -> list:
        return self.call("GET", f"{ACCOUNT_API}/serverless/{endpoint}/workers")["workers"]


def baked_commit(config: Config, api) -> str:
    """The commit that the endpoint's image baked, from the image's tag."""
    image = api.endpoint(config.endpoint).get("image") or ""
    repository, _, tag = image.rpartition(":")
    if repository != config.image_repository or not COMMIT.match(tag):
        raise NoBase(f"the endpoint runs {image!r}, not {config.image_repository}:<commit>; "
                     f"run gpu_job.py use-image <commit>")
    return tag


def public_image(image: str) -> bool:
    """Whether the registry serves the image's manifest without a login, as Runpod pulls it."""
    host, _, rest = image.partition("/")
    name, _, tag = rest.rpartition(":")
    try:
        with urllib.request.urlopen(f"https://{host}/token?scope=repository:{name}:pull", timeout=30) as reply:
            token = json.loads(reply.read())["token"]
        request = urllib.request.Request(f"https://{host}/v2/{name}/manifests/{tag}", method="HEAD", headers={
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.oci.image.index.v1+json, application/vnd.oci.image.manifest.v1+json, "
                      "application/vnd.docker.distribution.manifest.v2+json"})
        with urllib.request.urlopen(request, timeout=30):
            return True
    except urllib.error.HTTPError:
        return False


def use_image(config: Config, api, commit: str, *, state: Path, pullable=public_image, poll_seconds: float = 10.0):
    """Point the endpoint at the image of `commit`, then stop all its workers and allow them again. Runpod replaces a
    worker of the old image only over time, and until then that worker takes jobs and refuses each one, because its
    baked commit differs. The stop would end a running job too, so the change needs an empty pending/, and it holds
    the lock, so no job starts during it. The maximum comes back from endpoint_request, its one store."""
    image = f"{config.image_repository}:{commit}"
    if not COMMIT.match(commit) or not pullable(image):
        raise NoBase(f"{image} is not a public image; the workflow gpu-image builds it (tools/README.md)")
    with locked(state):
        settle_lost(config, state, api, datetime.datetime.now(datetime.timezone.utc))
        if in_flight(state):
            raise NoBase("jobs are in flight; change the image when they have ended")
        api.update_endpoint(config.endpoint, {"image": image})
        api.update_endpoint(config.endpoint, {"workers": {"max": 0}})
        try:
            while api.workers(config.endpoint):
                time.sleep(poll_seconds)
        finally:
            most = endpoint_request(config, commit)["workers"]["max"]
            api.update_endpoint(config.endpoint, {"workers": {"max": most}})


def execution_seconds(config: Config, seconds: int) -> int:
    """The job's execution timeout: its commands' time plus the handler's margin, within the endpoint's timeout."""
    return min(config.endpoint_timeout_seconds, min(seconds, config.job_seconds) + HANDLER_MARGIN_SECONDS)


def cost_usd(config: Config, execution: float) -> float:
    """What a job of `execution` seconds bills at most: the same model for the worst case and for the logged cost."""
    return (execution + START_ALLOWANCE_SECONDS + IDLE_SECONDS) * config.rate_per_second


def worst_case_usd(config: Config, seconds: int) -> float:
    return cost_usd(config, execution_seconds(config, seconds))


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


def settle(state: Path, pending: dict, ending: dict) -> bool:
    """Move a job from pending/ to the log, under the lock. False if another run settled it first."""
    path = state / "pending" / f"{pending['name']}.json"
    if not path.exists():
        return False
    line = {key: value for key, value in pending.items() if key not in ("name", "expires")}
    append_log(state, {**line, **ending})
    path.unlink()
    return True


def ending(config: Config, worst: float, status: dict) -> dict:
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
            "costUsd": cost_usd(config, gpu_seconds) if measured else worst, "result": result,
            "exitCodes": [step["exitCode"] for step in output.get("steps", [])],
            "error": output.get("error") or status.get("error")}


def settle_lost(config: Config, state: Path, api, now: datetime.datetime):
    """Under the lock: settle each job in flight that ended while no run waited for it. A job past its expiry whose
    end Runpod does not report may have run, so it costs its worst case. So does a job that Runpod no longer knows."""
    for pending in in_flight(state):
        expired = datetime.datetime.fromisoformat(pending["expires"]) <= now
        if pending.get("jobId") is None:
            if expired:
                settle(state, pending, {"gpuSeconds": None, "costUsd": pending["worstCaseUsd"], "result": "lost"})
            continue
        try:
            status = api.status(pending["endpoint"], pending["jobId"])
        except urllib.error.HTTPError as error:
            if error.code != 404:
                continue
            status = {"id": pending["jobId"], "status": "UNKNOWN"}
        if status["status"] in FINAL | {"UNKNOWN"}:
            settle(state, pending, ending(config, pending["worstCaseUsd"], status))
        elif expired:
            # Past its queue time and its timeout, Runpod ran it or dropped it; its status no longer tells which.
            settle(state, pending, {"gpuSeconds": None, "costUsd": pending["worstCaseUsd"], "result": "lost"})


def refusal(config: Config, state: Path, api, worst: float, now: datetime.datetime) -> str | None:
    """Why the job would pass a cap, or None. For each cap the spending is the larger of the log's sum and Runpod's
    bill, because the bill lags the jobs and the log does not hold the account's other spending."""
    lines = log_lines(state)
    today = now.astimezone(datetime.timezone.utc).date()
    logged_today = sum(line.get("costUsd", 0.0) for line in lines
                       if datetime.datetime.fromisoformat(line["time"]).astimezone(datetime.timezone.utc).date() == today)
    spent_today = max(logged_today, api.billed_today())
    spent = max(sum(line.get("costUsd", 0.0) for line in lines), api.billed_total())
    held = sum(pending["worstCaseUsd"] for pending in in_flight(state))
    if spent_today + held + worst > config.daily_cap_usd:
        return (f"today's ${spent_today:.2f} plus ${held:.2f} in flight plus this job's worst case ${worst:.2f} "
                f"passes the daily cap of ${config.daily_cap_usd:.2f}")
    if spent + held + worst > config.total_cap_usd:
        return (f"the ${spent:.2f} spent plus ${held:.2f} in flight plus this job's worst case ${worst:.2f} "
                f"passes the total cap of ${config.total_cap_usd:.2f}")
    return None


def patches(request: Request, base: str, repo: Path) -> dict:
    """Each tree's binary diff from the baked commit to its ref, base64-encoded, and its ref's commit in request.shas."""
    trees = {}
    for name, ref in request.trees.items():
        sha = git(repo, "rev-parse", "--verify", f"{ref}^{{commit}}").decode().strip()
        request.shas[name] = sha
        trees[name] = base64.b64encode(git(repo, "diff", "--binary", base, sha)).decode()
    return trees


def git(repo: Path, *args) -> bytes:
    return subprocess.run(["git", *args], cwd=repo, check=True, capture_output=True).stdout


def submit(request: Request, *, config: Config, api, state: Path, repo: Path, now=None, poll_seconds: float = 5.0,
           out=print) -> str:
    """Run one job and return its result: ok, failed, timed out or refused from the handler, refused by the caps, or
    Runpod's status when the job did not complete. If the script stops before the job's final status, the job stays
    in pending/, with its id once Runpod accepted it, and the next run settles it."""
    now = now or (lambda: datetime.datetime.now(datetime.timezone.utc))
    base = baked_commit(config, api)
    trees = patches(request, base, repo)
    seconds = min(request.seconds, config.job_seconds)
    execution = execution_seconds(config, seconds)
    worst = worst_case_usd(config, seconds)
    pending = {"time": now().isoformat(), "agent": request.agent, "endpoint": config.endpoint, "base": base,
               "trees": {tree: {"ref": ref, "sha": request.shas[tree]} for tree, ref in request.trees.items()},
               "commands": request.commands, "outputs": request.outputs, "worstCaseUsd": worst, "jobId": None,
               "expires": (now() + datetime.timedelta(seconds=QUEUE_SECONDS + execution)).isoformat(),
               "name": uuid.uuid4().hex}
    with locked(state):
        settle_lost(config, state, api, now())
        reason = refusal(config, state, api, worst, now())
        if reason is None:
            write_pending(state, pending["name"], pending)
        else:
            line = {key: value for key, value in pending.items() if key not in ("name", "expires")}
            append_log(state, {**line, "gpuSeconds": 0.0, "costUsd": 0.0, "result": "refused", "error": reason})
    if reason is not None:
        out(f"refused: {reason}")
        return "refused"
    body = {"input": {"base": base, "trees": trees, "commands": request.commands, "outputs": request.outputs,
                      "seconds": seconds},
            "policy": {"executionTimeout": execution * 1000, "ttl": QUEUE_SECONDS * 1000}}
    try:
        pending["jobId"] = api.submit(config.endpoint, body)
    except urllib.error.HTTPError as error:
        # Runpod answered and made no job, so it bills nothing. A timeout or a lost connection stays ambiguous.
        with locked(state):
            settle(state, pending, {"gpuSeconds": 0.0, "costUsd": 0.0, "result": "rejected",
                                    "error": f"/run answered {error.code} {error.reason}"})
        raise
    with locked(state):
        write_pending(state, pending["name"], pending)
    out(f"job {pending['jobId']} sent to endpoint {config.endpoint}; worst case ${worst:.2f}")
    status = wait(api, config.endpoint, pending["jobId"], poll_seconds)
    settled = ending(config, worst, status)
    with locked(state):
        settle(state, pending, settled)
    write_outputs(request.out, status, settled["result"])
    out(f"job {pending['jobId']}: {settled['result']}, {settled['gpuSeconds']:.0f} GPU seconds, counted as "
        f"${settled['costUsd']:.4f}; result in {request.out}")
    return settled["result"]


def wait(api, endpoint: str, job: str, poll_seconds: float) -> dict:
    """The job's final status. Ctrl-C, SIGTERM or SIGHUP cancels the job and still waits for its final status, which
    holds its cost."""
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


def endpoint_request(config: Config, commit: str, name: str = "tertius-gpu") -> dict:
    """The endpoint bounds the spending by itself too: it keeps no minimum of workers, runs at most six at once, and
    stops each job at the execution timeout."""
    return {"name": name, "type": "QUEUE", "image": f"{config.image_repository}:{commit}", "disk": DISK_GB,
            "flashboot": "FLASHBOOT", "scaling": {"type": "QUEUE_DELAY", "queueDelay": 4},
            "workers": {"min": 0, "max": 6, "idleTimeout": IDLE_SECONDS},
            "timeout": config.endpoint_timeout_seconds * 1000,
            "gpu": {"pools": [POOL], "excludedTypes": OTHER_CARDS}}


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


def interrupt(signum, frame):
    raise KeyboardInterrupt


def main(argv: list) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    job = commands.add_parser("submit", help="run one job on the endpoint")
    job.add_argument("--agent", required=True, help="who sends the job, for the audit log")
    job.add_argument("--tree", action="append", required=True, metavar="NAME=REF",
                     help="a tree in the job's work folder: the baked source plus the diff to REF")
    job.add_argument("--run", action="append", default=[], metavar="CMD", help="a command, run in the dev shell")
    job.add_argument("--output", action="append", default=[], metavar="GLOB", help="files to send back")
    job.add_argument("--timeout", type=positive, default=None, help="seconds for all commands; at most the config's")
    job.add_argument("--out", type=Path, required=True, help="folder for the output files and result.json")
    for name, text in [("endpoint-request", "print the request that creates the endpoint, and send nothing"),
                       ("create-endpoint", "create the endpoint; write its id into endpoint.json after"),
                       ("use-image", "point the endpoint at the image of another commit")]:
        commands.add_parser(name, help=text).add_argument("commit", help="the full commit that the image baked")
    args = parser.parse_args(argv)
    config = Config.load()
    if args.command == "endpoint-request":
        print(json.dumps(endpoint_request(config, args.commit), indent=2))
        return 0
    key = key_file()
    if key is None:
        print(f"no key: write the path of the file that holds the Runpod API key into {KEY_POINTER}", file=sys.stderr)
        return 2
    api = RunpodApi(key)
    if args.command == "create-endpoint":
        if config.endpoint:
            print(f"{CONFIG_FILE} already names the endpoint {config.endpoint}", file=sys.stderr)
            return 2
        if not public_image(endpoint_request(config, args.commit)["image"]):
            print(f"{config.image_repository}:{args.commit} is not a public image", file=sys.stderr)
            return 2
        reply = api.create_endpoint(endpoint_request(config, args.commit))
        print(json.dumps({"id": reply.get("id"), "name": reply.get("name")}))
        return 0
    if not config.endpoint:
        print(f"{CONFIG_FILE} names no endpoint yet (tools/README.md, \"GPU jobs on Runpod\")", file=sys.stderr)
        return 2
    if args.command == "use-image":
        use_image(config, api, args.commit, state=STATE)
        print(f"the endpoint {config.endpoint} now runs {config.image_repository}:{args.commit}")
        return 0
    trees = dict(tree.split("=", 1) for tree in args.tree)
    request = Request(agent=args.agent, trees=trees, commands=args.run, outputs=args.output,
                      seconds=args.timeout or config.job_seconds, out=args.out)
    repo = Path(git(Path.cwd(), "rev-parse", "--show-toplevel").decode().strip())
    # An agent's tool stops a command with SIGTERM, and a closed terminal sends SIGHUP. Both cancel as Ctrl-C does.
    signal.signal(signal.SIGTERM, interrupt)
    signal.signal(signal.SIGHUP, interrupt)
    result = submit(request, config=config, api=api, state=STATE, repo=repo)
    return 0 if result == "ok" else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
