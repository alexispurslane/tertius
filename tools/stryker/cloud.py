#!/usr/bin/env python3
"""Tool code. The official Stryker run of tools/stryker/changed.py on the cloud's CPU workers: one job per module with
changed lines, all at once, each on its own worker with the official settings. It prints the same table, evidence and
validity as changed.py over all modules. Run it from the repository on the Mac, without the dev shell:

    python3 -I tools/stryker/cloud.py <base> <out-dir> --agent <name> [--head <commit>] [--exclude <file>...]

The script finds the modules with changed lines here, as changed.py does, and prints the changed code outside the
official run for the agent's tools/mutate.py list. Then it sends one job per module through tools/runpod/gpu_job.py
with --kind cpu, which publishes the head commit and keeps the caps and the audit log. Each job fetches the history of
its tree, runs `changed.py <merge base> --modules <module>` in the dev shell, and sends back the module's results.json,
its Stryker configuration, Stryker's console output and run.py's process samples. They land in <out-dir>/<module>/,
which must be new or empty, so that a result of an earlier run cannot count for this one. A SIGTERM, SIGHUP or SIGINT
reaches every job's gpu_job.py, which cancels its job.

A worker runs one module at concurrency 1, so the rule of tools/README.md, "Stryker.NET pitfalls", holds on each
machine: one test host. Workers are separate machines, so the Mac's lock /tmp/tertius-stryker.lock does not bind them.
The exit code is 1 if a module's run is not valid or its job did not end with a result, as with changed.py.
"""

import argparse
import json
import shlex
import signal
import subprocess
import sys
import threading
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))
import changed  # noqa: E402
import changed_lines  # noqa: E402

GPU_JOB = HERE.parent / "runpod/gpu_job.py"
# The files that come back from a module's worker, under the job's out/: changed.py's results.json, which holds the
# rows, the evidence and the validity, and the module's Stryker configuration, console output and run.py's samples. A
# job sends back at most 8 MB in base64. Stryker's log (8 to 21 MB) and its mutation report (6 to 85 MB) passed that on
# 2026-11-12, so they stay on the worker.
OUTPUTS = ["results.json", "{module}/stryker-config.json", "{module}/run.out", "{module}/processes.log"]


def job_command(base: str, module: str, exclude: list) -> str:
    """The job's command: the tree's whole history, without blobs until a diff reads them, the Stryker tool of the
    tool manifest, then changed.py on one module. The job's tree is a shallow clone of one commit, and changed.py diffs from the merge base."""
    excluded = f" --exclude {' '.join(shlex.quote(name) for name in exclude)}" if exclude else ""
    return (f"cd t && git fetch -q --filter=blob:none --unshallow origin && dotnet tool restore > /dev/null && "
            f"python3 -I tools/stryker/changed.py {base} ../out --modules {module}{excluded}")


def merged(results: dict, failures: dict) -> dict:
    """The run over all modules from each module's results.json (module -> its parsed file) and the modules whose job
    gave none (module -> why). The rows keep the order of changed.MODULES."""
    order = [module for module in changed.MODULES if module in results or module in failures]
    rows, unmutated, notes, problems, load, settings, official = [], [], [], [], [], None, True
    for module in order:
        if module in failures:
            problems.append(f"{module}: the cloud job gave no results.json: {failures[module]}")
            continue
        result = results[module]
        rows += result["rows"]
        unmutated += [tuple(item) for item in result["unmutated"]]
        notes += result["evidence"]
        problems += result["problems"]
        load += result["load"]
        settings = settings or result["settings"]
        official = official and result["official"]
    return {"rows": rows, "unmutated": unmutated, "notes": notes, "problems": problems, "load": load,
            "settings": settings or "", "official": official}


def unusable(out: Path):
    """Why the output folder cannot take this run, or None. A folder with files could hold a module's results.json
    from an earlier run, which would count as this run's result if this run's job gave none."""
    if out.exists() and (not out.is_dir() or any(out.iterdir())):
        return f"the output folder {out} is not empty: give a new folder, so that no earlier result counts for this run"
    return None


class Jobs:
    """The gpu_job.py processes of this run, and the signal that stopped the run, if any. Each process starts in a
    session of its own, so a Ctrl-C or a signal to the run's process group reaches it once, through `stop`, and not a
    second time while it cancels its job. One lock spans each start and the stop, so no job starts after a stop."""

    def __init__(self):
        self.lock = threading.Lock()
        self.children = []
        self.stopped = None

    def start(self, command: list, log):
        """The started gpu_job.py process, or None after a stop."""
        with self.lock:
            if self.stopped is not None:
                return None
            child = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            self.children.append(child)
            return child

    def stop(self, signum: int):
        """Sends the signal to each job's gpu_job.py, which cancels its job at Runpod and exits."""
        with self.lock:
            self.stopped = signum
            for child in self.children:
                child.send_signal(signum)


def run_job(module: str, args, head: str, base: str, out: Path, ended: dict, jobs: Jobs):
    folder = out / module
    command = [sys.executable, "-I", str(GPU_JOB), "submit", "--agent", args.agent, "--kind", "cpu",
               "--tree", f"t={head}", "--run", job_command(base, module, args.exclude), "--out", str(out / "jobs" / module)]
    for name in OUTPUTS:
        command += ["--output", "out/" + name.format(module=module)]
    with (out / f"{module}.job.log").open("w") as log:
        child = jobs.start(command, log)
        if child is None:
            print("not started: the run was stopped", file=log)
            return
        ended[module] = child.wait()
    landed = out / "jobs" / module / "out"
    folder.mkdir(parents=True, exist_ok=True)
    if (landed / module).exists():
        for path in (landed / module).rglob("*"):
            if path.is_file():
                target = folder / path.relative_to(landed / module)
                target.parent.mkdir(parents=True, exist_ok=True)
                path.replace(target)
    if (landed / "results.json").is_file():
        (landed / "results.json").replace(folder / "results.json")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("base", help="the branch or commit that the changes start from, for example main")
    parser.add_argument("out", type=Path, help="the folder for each module's results and the merged results.json")
    parser.add_argument("--agent", required=True, help="who sends the jobs, for the audit log")
    parser.add_argument("--head", default="HEAD", help="the last commit of the changes (default HEAD)")
    parser.add_argument("--exclude", nargs="*", default=[], help="file names to leave out, for example Program.cs")
    args = parser.parse_args()
    root = changed_lines.repo_root()
    git = lambda *words: subprocess.check_output(["git", "-C", root, *words], text=True).strip()  # noqa: E731
    head = git("rev-parse", "--verify", f"{args.head}^{{commit}}")
    base = git("merge-base", args.base, head)
    lines, removed = changed_lines.changed_and_removed(root, base, ["src", "analyzers"], head, changed.CODE_SUFFIXES)
    changed.print_hand_files(lines, removed)
    plans = changed.planned(root, base, head, args.exclude, removed)
    modules = [module for module, patterns in plans.items() if patterns]
    if not modules:
        print(f"No changed C# lines in {', '.join(changed.MODULES)}.")
        return 0
    out = args.out.resolve()
    if problem := unusable(out):
        print(problem, file=sys.stderr)
        return 2
    out.mkdir(parents=True, exist_ok=True)
    print(f"Stryker on {', '.join(modules)}, one CPU worker each; the jobs' logs are {out}/<module>.job.log",
          file=sys.stderr, flush=True)
    started = time.monotonic()
    ended, jobs = {}, Jobs()
    for signum in (signal.SIGTERM, signal.SIGHUP, signal.SIGINT):
        signal.signal(signum, lambda number, _frame: jobs.stop(number))
    threads = [threading.Thread(target=run_job, args=(module, args, head, base, out, ended, jobs))
               for module in modules]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()
    results, failures = {}, {}
    for module in modules:
        path = out / module / "results.json"
        if path.is_file():
            results[module] = json.loads(path.read_text())
        else:
            failures[module] = f"exit code {ended.get(module)}, see {out}/{module}.job.log"
    run = merged(results, failures)
    run["rows"] += changed.removed_rows(removed, set(plans) - set(modules), args.exclude)
    return changed.print_report(run["rows"], run["unmutated"], run["notes"], run["problems"], run["settings"],
                          run["official"], run["load"], time.monotonic() - started, out)


if __name__ == "__main__":
    sys.exit(main())
