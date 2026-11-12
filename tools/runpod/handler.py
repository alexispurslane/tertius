"""Tool code (tools/runpod/README.md): the job handler of the serverless worker, without the Runpod SDK.
tools/runpod/worker.py hands each job's input to `handle`, and tools/runpod/gpu_job.py makes that input.

A job names its trees' commits, each under a ref of the public repository that gpu_job.py pushed it to, its commands
and the globs of its outputs:

    {"protocol": 3, "repository": "https://github.com/alexispurslane/tertius.git",
     "trees": {"main": {"sha": "<commit>", "ref": "refs/gpu-jobs/<date>/<agent>/<commit>"}},
     "commands": ["cd main && dotnet build src/Tertius.Demo -c Release"], "outputs": ["main/out/*.json"],
     "seconds": 600}

Each tree is a shallow, sparse checkout of its commit without docs/ and .tickets/, which no build reads. A commit
whose dev shell (flake.nix, flake.lock, native/) differs from the one that the image baked is refused, because its
commands would run in the wrong shell. The commands run one after another in the work folder, each in the dev shell,
and the first failure ends the job. The result holds each command's exit code and time, the end of the log, and the
output files up to a size limit, base64-encoded. The commands see no RUNPOD_* variable of the worker, and a process
that a command leaves in the background stops when the command ends.
"""

import base64
import glob
import math
import os
import re
import signal
import subprocess
import tempfile
import time
from dataclasses import dataclass
from pathlib import Path

# The version of the job's input. gpu_job.py reads it from this file of an image's commit, so that it sends a job
# only to an endpoint whose handler understands it.
PROTOCOL = 3
# A tree's name becomes a folder name in the work folder, so it is a plain name.
TREE_NAME = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$")
COMMIT = re.compile(r"^[0-9a-f]{40}$")
JOB_REF = re.compile(r"^refs/gpu-jobs/[A-Za-z0-9/._-]+$")
# The files of the dev shell. A commit runs only in the shell that the image built from the same files.
SHELL_PATHS = ["flake.nix", "flake.lock", "native"]
# A tree leaves these out: no build reads them, and research files there grow by megabytes an hour.
SPARSE = ["/*", "!/docs/", "!/.tickets/"]


@dataclass(frozen=True)
class Limits:
    """seconds: the ceiling of a job's own `seconds`, the time of all its commands together: the longest job_seconds of
    a kind in endpoint.json, the CPU kind's, so that an official Stryker run of one module fits. The endpoint's own
    execution timeout bounds each kind's jobs anyway. output_bytes: the base64
    size of the files sent back; Runpod keeps a job's result in its queue, so it stays small. log_bytes: the end of
    the log sent back."""

    seconds: float = 7140.0
    output_bytes: int = 8_000_000
    log_bytes: int = 200_000


class Refused(Exception):
    """A job that the handler does not start."""


def handle(job: dict, *, baked: Path, work: Path, shell: list[str], limits: Limits = Limits()) -> dict:
    """Run one job (the module's docstring has its input) and return {status, error, steps, log, files, skipped}.
    status is ok, failed, timed out or refused. files maps each output's path in the work folder to its base64."""
    result = {"status": "ok", "error": None, "steps": [], "log": "", "files": {}, "skipped": {}}
    try:
        trees = checked(job, baked)
    except Refused as refusal:
        return {**result, "status": "refused", "error": str(refusal)}
    log = bytearray()
    try:
        make_trees(trees, job["repository"], baked, work)
        seconds = min(float(job.get("seconds", limits.seconds)), limits.seconds)
        result["status"] = run_commands(job.get("commands", []), work, shell, seconds, result["steps"], log)
        if result["status"] == "failed":
            result["error"] = f"command {len(result['steps'])} failed with exit code {result['steps'][-1]['exitCode']}"
        elif result["status"] == "timed out":
            result["error"] = f"the commands passed the job's limit of {seconds:.0f} s"
    except subprocess.CalledProcessError as error:
        result["status"] = "failed"
        result["error"] = (error.stderr or b"").decode(errors="replace").strip() or str(error)
    except Refused as refusal:
        result["status"] = "refused"
        result["error"] = str(refusal)
    result["files"], result["skipped"] = collect(job.get("outputs", []), work, limits.output_bytes)
    result["log"] = bytes(log[-limits.log_bytes:]).decode(errors="replace")
    return result


def checked(job: dict, baked: Path) -> dict:
    """The job's trees, after the checks that need no work: its protocol, plain names, commits and refs, and globs."""
    if job.get("protocol") != PROTOCOL:
        raise Refused(f"the job has protocol {job.get('protocol')}, but this handler has protocol {PROTOCOL}")
    trees = job.get("trees") or {}
    for name, tree in trees.items():
        if not TREE_NAME.match(name):
            raise Refused(f"the tree name {name!r} is not a plain name")
        if not COMMIT.match(tree.get("sha", "")) or not JOB_REF.match(tree.get("ref", "")):
            raise Refused(f"the tree {name} needs a commit id and a ref under refs/gpu-jobs/")
    for pattern in job.get("outputs", []):
        if Path(pattern).is_absolute() or ".." in Path(pattern).parts:
            raise Refused(f"the output glob {pattern!r} leaves the work folder")
    return trees


def make_trees(trees: dict, repository: str, baked: Path, work: Path):
    """Fetch each tree's commit by its ref, without the blobs of the left-out folders, and check it out."""
    work.mkdir(parents=True, exist_ok=True)
    for name, tree in trees.items():
        folder = work / name
        run_git(work, "init", "-q", name)
        run_git(folder, "sparse-checkout", "set", "--no-cone", *SPARSE)
        # A named remote: the partial clone fetches the blobs of the checked-out files from it at the checkout.
        run_git(folder, "remote", "add", "origin", repository)
        run_git(folder, "fetch", "-q", "--depth", "1", "--filter=blob:none", "origin", tree["ref"])
        fetched = run_git(folder, "rev-parse", "FETCH_HEAD").strip()
        if fetched != tree["sha"]:
            raise subprocess.CalledProcessError(1, "git fetch", stderr=(
                f"{tree['ref']} names {fetched}, not the job's commit {tree['sha']}").encode())
        run_git(folder, "checkout", "-q", "--detach", tree["sha"])
        if not same_shell(folder, baked):
            raise Refused(f"the commit {tree['sha']} of the tree {name} has another dev shell than this image: "
                          f"{', '.join(SHELL_PATHS)} differ; its image must run it")


def run_git(cwd: Path, *args) -> str:
    return subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True).stdout.decode()


def same_shell(tree: Path, baked: Path) -> bool:
    """Whether the tree's dev shell files are the baked ones, byte for byte, with no file more or less."""
    def files(root: Path) -> dict:
        found = {}
        for path in SHELL_PATHS:
            target = root / path
            for file in ([target] if target.is_file() else sorted(target.rglob("*")) if target.exists() else []):
                if file.is_file():
                    found[str(file.relative_to(root))] = file.read_bytes()
        return found
    return files(tree) == files(baked)


def run_commands(commands: list, work: Path, shell: list[str], seconds: float, steps: list, log: bytearray) -> str:
    deadline = time.monotonic() + seconds
    # The worker's own variables, such as the key with which the SDK takes jobs, stay out of the commands and the log.
    env = {key: value for key, value in os.environ.items() if not key.startswith("RUNPOD_")}
    for command in commands:
        start = time.monotonic()
        log += f"$ {command}\n".encode()
        # The output goes to a file, not a pipe: a process left in the background would hold a pipe open. The command
        # gets its own process group, so the end of the command stops that process and its children too.
        with tempfile.TemporaryFile() as output:
            process = subprocess.Popen([*shell, command], cwd=work, env=env, stdout=output, stderr=subprocess.STDOUT,
                                       start_new_session=True)
            try:
                code = process.wait(timeout=max(0.0, deadline - start))
            except subprocess.TimeoutExpired:
                code = None
            stop_group(process)
            output.seek(0)
            log += output.read()
        steps.append({"command": command, "exitCode": code, "seconds": time.monotonic() - start})
        if code is None:
            return "timed out"
        if code != 0:
            return "failed"
    return "ok"


def stop_group(process: subprocess.Popen):
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    process.wait()


def collect(patterns: list, work: Path, limit: int) -> tuple[dict, dict]:
    """The files of the globs, in name order, while their base64 total stays within `limit` bytes; the rest by
    their size."""
    found = sorted({Path(path) for pattern in patterns for path in glob.glob(str(work / pattern), recursive=True)
                    if Path(path).is_file()})
    files, skipped, total = {}, {}, 0
    for path in found:
        name = str(path.relative_to(work))
        size = path.stat().st_size
        encoded = 4 * math.ceil(size / 3)
        if total + encoded <= limit:
            files[name] = base64.b64encode(path.read_bytes()).decode()
            total += encoded
        else:
            skipped[name] = size
    return files, skipped
