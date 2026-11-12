"""Tool code (tools/README.md, "GPU jobs on Runpod"): the job handler of the serverless worker, without the Runpod SDK.
tools/runpod/worker.py hands each job's input to `handle`, and tools/runpod/gpu_job.py makes that input.

A job names the commit that the image baked (`base`), its trees, its commands and the globs of its outputs:

    {"base": "<sha>", "trees": {"main": "", "branch": "<base64 of git diff --binary>"},
     "commands": ["cd branch && dotnet build src/Tertius.Demo -c Release"], "outputs": ["branch/out/*.json"],
     "seconds": 600}

Each tree is a copy of the baked source plus its own patch. A build in the tree compiles every project again, because
MSBuild's restore files hold the image's absolute project paths: 15 to 21 s on a worker (ter-94uo, 2026-11-12). The
commands run one after another in the work folder, each in the dev
shell, and the first failure ends the job. The result holds each command's exit code and time, the end of the log, and
the output files up to a size limit, base64-encoded. The commands see no RUNPOD_* variable of the worker, and a
process that a command leaves in the background stops when the command ends.
"""

import base64
import glob
import math
import os
import re
import shutil
import signal
import subprocess
import tempfile
import time
from dataclasses import dataclass
from pathlib import Path

# A tree's name becomes a folder name in the work folder, so it is a plain name.
TREE_NAME = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$")


@dataclass(frozen=True)
class Limits:
    """seconds: the ceiling of a job's own `seconds`, the time of all its commands together. output_bytes: the base64
    size of the files sent back; Runpod keeps a job's result in its queue, so it stays small. log_bytes: the end of
    the log sent back."""

    seconds: float = 840.0
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
        make_trees(trees, baked, work)
        seconds = min(float(job.get("seconds", limits.seconds)), limits.seconds)
        result["status"] = run_commands(job.get("commands", []), work, shell, seconds, result["steps"], log)
        if result["status"] == "failed":
            result["error"] = f"command {len(result['steps'])} failed with exit code {result['steps'][-1]['exitCode']}"
        elif result["status"] == "timed out":
            result["error"] = f"the commands passed the job's limit of {seconds:.0f} s"
    except subprocess.CalledProcessError as error:
        result["status"] = "failed"
        result["error"] = (error.stderr or b"").decode(errors="replace").strip() or str(error)
    result["files"], result["skipped"] = collect(job.get("outputs", []), work, limits.output_bytes)
    result["log"] = bytes(log[-limits.log_bytes:]).decode(errors="replace")
    return result


def checked(job: dict, baked: Path) -> dict:
    """The job's trees, after the checks that need no work: its base is the image's, its names and globs are plain."""
    image = (baked / "BAKED_COMMIT").read_text().strip()
    if job.get("base") != image:
        raise Refused(f"the job's base is {job.get('base')}, but this image baked {image}; make the patches from it")
    trees = job.get("trees") or {}
    for name in trees:
        if not TREE_NAME.match(name):
            raise Refused(f"the tree name {name!r} is not a plain name")
    for pattern in job.get("outputs", []):
        if Path(pattern).is_absolute() or ".." in Path(pattern).parts:
            raise Refused(f"the output glob {pattern!r} leaves the work folder")
    return trees


def make_trees(trees: dict, baked: Path, work: Path):
    work.mkdir(parents=True, exist_ok=True)
    for name, patch in trees.items():
        tree = work / name
        # copy2 keeps the times, so the build after the patch sees only the patched files as changed.
        shutil.copytree(baked, tree, symlinks=True)
        if patch:
            subprocess.run(["git", "apply", "--binary", "--whitespace=nowarn", "-"], cwd=tree,
                           input=base64.b64decode(patch), check=True, capture_output=True)


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
