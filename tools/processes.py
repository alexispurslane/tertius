# Tool code: shared by tools/mutate.py and the Stryker scripts in tools/stryker. Runs a command in its own process group
# with its output in a log, and turns SIGTERM and SIGHUP into an exception, so that every `finally` and context manager
# of a tool restores what it changed: a mutated file, or the Stryker lock.
import contextlib
import os
import signal
import subprocess
import time
from pathlib import Path


class Stopped(BaseException):
    """A SIGTERM or SIGHUP arrived. A BaseException, so that no `except Exception` swallows it on its way out."""


def stop_on_signals():
    """Turns SIGTERM and SIGHUP into Stopped, so that every `finally` and context manager runs."""
    def handler(signum, frame):
        raise Stopped(signum)
    signal.signal(signal.SIGTERM, handler)
    signal.signal(signal.SIGHUP, handler)


def run_logged(command, root, log_path, timeout=None, started=None):
    """Runs command in its own process group with its output in log_path. Returns the exit code, whether the timeout
    stopped it, its output and its seconds. On a timeout, an exception or a signal, it kills the whole group, so that
    no test host keeps running with the mutant. started, if given, gets the process id, which is also the id of its
    group, as soon as the process runs; it returns a function that run_logged calls when the process has ended."""
    start = time.monotonic()
    with open(log_path, 'w') as log:
        process = subprocess.Popen(command, cwd=root, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        ended = started(process.pid) if started else None
        timed_out = False
        try:
            process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
        finally:
            if process.returncode is None:
                with contextlib.suppress(ProcessLookupError):
                    os.killpg(process.pid, signal.SIGKILL)
                process.wait()
            if ended:
                ended()
    return process.returncode, timed_out, Path(log_path).read_text(errors='replace'), time.monotonic() - start
