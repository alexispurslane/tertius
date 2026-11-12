"""Tool code (tools/README.md, "GPU jobs on Runpod"): the job handler of the serverless worker, without the Runpod SDK.
tools/runpod/worker.py hands each job's input to `handle`, and tools/runpod/gpu_job.py makes that input.

A job names the commit that the image baked (`base`), its trees, its commands and the globs of its outputs:

    {"base": "<sha>", "trees": {"main": "", "branch": "<base64 of git diff --binary>"},
     "commands": ["cd branch && dotnet build src/Tertius.Demo -c Release"], "outputs": ["branch/out/*.json"]}

Each tree is a copy of the baked source, with its built bin and obj folders, plus its own patch, so a build after the
patch compiles only what the patch changed. The commands run one after another in the work folder, each in the dev
shell, and the first failure ends the job. The result holds each command's exit code and time, the end of the log, and
the output files up to a size limit, base64-encoded.
"""

import base64
import glob
import os
import re
import shutil
import signal
import subprocess
import time
from dataclasses import dataclass
from pathlib import Path

# A tree's name becomes a folder name in the work folder, so it is a plain name.
TREE_NAME = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$")


@dataclass(frozen=True)
class Limits:
    """seconds: the time of all commands together, below the endpoint's execution timeout. output_bytes: the files
    sent back; Runpod keeps a job's result in its queue, so it stays small. log_bytes: the end of the log sent back."""

    seconds: float = 840.0
    output_bytes: int = 8_000_000
    log_bytes: int = 200_000


class Refused(Exception):
    """A job that the handler does not start."""


def handle(job: dict, *, baked: Path, work: Path, shell: list[str], limits: Limits = Limits()) -> dict:
    result = {"status": "ok", "error": None, "steps": [], "log": "", "files": {}, "skipped": {}}
    try:
        trees = checked(job, baked)
    except Refused as refusal:
        return {**result, "status": "refused", "error": str(refusal)}
    log = bytearray()
    try:
        make_trees(trees, baked, work)
        result["status"] = run_commands(job.get("commands", []), work, shell, limits.seconds, result["steps"], log)
        if result["status"] == "failed":
            result["error"] = f"command {len(result['steps'])} failed with exit code {result['steps'][-1]['exitCode']}"
        elif result["status"] == "timed out":
            result["error"] = f"the commands passed the job's limit of {limits.seconds:.0f} s"
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
    for command in commands:
        start = time.monotonic()
        log += f"$ {command}\n".encode()
        # Its own process group, so a timeout stops the command's children too, such as the demo under dotnet.
        process = subprocess.Popen([*shell, command], cwd=work, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                   start_new_session=True)
        try:
            output, _ = process.communicate(timeout=max(0.0, deadline - start))
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            output, _ = process.communicate()
            log += output
            steps.append({"command": command, "exitCode": None, "seconds": time.monotonic() - start})
            return "timed out"
        log += output
        steps.append({"command": command, "exitCode": process.returncode, "seconds": time.monotonic() - start})
        if process.returncode != 0:
            return "failed"
    return "ok"


def collect(patterns: list, work: Path, limit: int) -> tuple[dict, dict]:
    """The files of the globs, in name order, while their total stays within `limit` bytes; the rest by size."""
    found = sorted({Path(path) for pattern in patterns for path in glob.glob(str(work / pattern), recursive=True)
                    if Path(path).is_file()})
    files, skipped, total = {}, {}, 0
    for path in found:
        name = str(path.relative_to(work))
        size = path.stat().st_size
        if total + size <= limit:
            files[name] = base64.b64encode(path.read_bytes()).decode()
            total += size
        else:
            skipped[name] = size
    return files, skipped
