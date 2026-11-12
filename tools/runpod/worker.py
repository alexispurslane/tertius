"""Tool code (tools/runpod/README.md): the entry of the serverless worker in the image. The Runpod SDK
takes each job from the endpoint's queue and calls `run`, which hands the job's input to handler.handle in a fresh work
folder and removes the folder after the job, so a warm worker's disk does not fill.

The image runs it as `/opt/tertius-worker/bin/python -u /opt/tertius/tools/runpod/worker.py` (tools/runpod/Dockerfile).
"""

import shutil
import tempfile
from pathlib import Path

import runpod

import handler

BAKED = Path("/opt/tertius")
# Each command runs in the dev shell that the image baked. Nix stays offline; git fetches the trees' commits.
SHELL = ["nix", "develop", "/nix/var/nix/profiles/tertius-dev", "--offline", "--command", "bash", "-c"]


def run(job: dict) -> dict:
    work = Path(tempfile.mkdtemp(prefix="tertius-job-"))
    try:
        return handler.handle(job["input"], baked=BAKED, work=work, shell=SHELL)
    finally:
        shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    runpod.serverless.start({"handler": run})
