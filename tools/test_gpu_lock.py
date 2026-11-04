#!/usr/bin/env python3
"""Tool code. The tests of tools/gpu-lock.sh, the helper of the GPU lock: the holder's record, the release that removes
only its own lock, the clear of a stale lock, and the exclusion between the helper and its older copy of 93e46dd7f.

    nix develop --command python3 -I tools/test_gpu_lock.py

The standard library only. Each test gets a fresh folder under /private/tmp and gives the helper its lock and its queue
there, through TERTIUS_GPU_LOCK and TERTIUS_GPU_QUEUE. No test touches the real lock or the real queue, because other
runs measure on this machine.
"""

import os
import shutil
import signal
import subprocess
import tempfile
import time
import unittest
from pathlib import Path

HELPER = Path(__file__).with_name("gpu-lock.sh")
REAL_LOCK = "/tmp/tertius-gpu-measure.lock"
REAL_QUEUE = "/tmp/tertius-gpu-measure.queue"
# The helper before this ticket, whose lock is an empty directory.
OLD_HELPER_COMMIT = "93e46dd7f"
SCRATCH = "/private/tmp" if os.path.isdir("/private/tmp") else None
# The time that a test gives a run to take a lock that it must not take.
SETTLE = 1.0
# A start time that no live process has.
NEVER_STARTED = "Thu Jan 1 00:00:00 1970"


def setUpModule():
    # A helper with fixed paths would run the tests on the real lock.
    text = HELPER.read_text()
    if "${TERTIUS_GPU_LOCK:-" not in text or "${TERTIUS_GPU_QUEUE:-" not in text:
        raise RuntimeError(f"{HELPER} does not take its paths from TERTIUS_GPU_LOCK and TERTIUS_GPU_QUEUE")


def wait_for(condition, timeout: float = 10.0) -> bool:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if condition():
            return True
        time.sleep(0.02)
    return condition()


def start_time(pid: int) -> str:
    out = subprocess.run(["ps", "-o", "lstart=", "-p", str(pid)], env={**os.environ, "LC_ALL": "C"},
                         capture_output=True, text=True, check=False).stdout
    return " ".join(out.split())


class Run:
    """One run of a helper whose command holds the GPU until the test lets it go."""

    def __init__(self, bench: "Bench", name: str, helper: Path, code: int):
        self.log = bench.folder / f"{name}.log"
        self.started = bench.folder / f"{name}.started"
        self.release = bench.folder / f"{name}.release"
# The command also ends when its folder vanishes, so that no test leaves it behind.
        command = ["bash", "-c", 'touch "$1"; while [ ! -e "$2" ] && [ -e "$1" ]; do sleep 0.02; done; exit "$3"',
                   "held", str(self.started), str(self.release), str(code)]
        self.process = subprocess.Popen(["bash", str(helper), str(self.log), *command], env=bench.env,
                                        stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)

    def holds(self) -> bool:
        return self.started.exists()

    def wait_until_it_holds(self):
        if not wait_for(self.holds):
            raise AssertionError(f"the run of {self.log.name} did not take the lock")

    def finish(self, sent: int | None = None) -> int:
        """Ends the command, after the signal `sent` to the helper if one is given, and returns the exit code."""
        if sent is not None:
            self.process.send_signal(sent)
            time.sleep(0.1)
        self.release.touch()
        return self.process.wait(timeout=10)

    def log_text(self) -> str:
        return self.log.read_text() if self.log.exists() else ""


class Bench:
    """A fresh folder with the lock and the queue of the tests."""

    def __init__(self, test: unittest.TestCase):
        folder = tempfile.TemporaryDirectory(dir=SCRATCH, prefix="gpu-lock-test-")
        test.addCleanup(folder.cleanup)
        self.folder = Path(folder.name)
        self.lock = self.folder / "lock"
        self.queue = self.folder / "queue"
        self.queue.mkdir()
        self.env = {**os.environ, "TERTIUS_GPU_LOCK": str(self.lock), "TERTIUS_GPU_QUEUE": str(self.queue)}
        self.runs: list[Run] = []
        self.processes: list[subprocess.Popen] = []
        test.addCleanup(self.stop)

    def start(self, name: str, helper: Path = HELPER, code: int = 0) -> Run:
        run = Run(self, name, helper, code)
        self.runs.append(run)
        self.track(run.process)
        return run

    def old_helper(self) -> Path:
        """The helper of 93e46dd7f, with its fixed paths moved to this bench."""
        text = subprocess.run(["git", "show", f"{OLD_HELPER_COMMIT}:tools/gpu-lock.sh"], cwd=HELPER.parent,
                              capture_output=True, text=True, check=True).stdout
        assert f"lock={REAL_LOCK}\n" in text and f"queue={REAL_QUEUE}\n" in text
        text = text.replace(REAL_LOCK, str(self.lock)).replace(REAL_QUEUE, str(self.queue))
        assert REAL_LOCK not in text and REAL_QUEUE not in text
        path = self.folder / "old-gpu-lock.sh"
        path.write_text(text)
        return path

    def sleeper(self) -> subprocess.Popen:
        return self.track(subprocess.Popen(["sleep", "60"]))

    def record(self) -> dict[str, str]:
        holder = self.lock / "holder"
        if not holder.exists():
            return {}
        return dict(line.split("=", 1) for line in holder.read_text().splitlines() if "=" in line)

    def write_record(self, pid: int, start: str, token: str = "someone-else", label: str = "a test's holder"):
        """Makes the lock as a holder with this record would."""
        self.lock.mkdir()
        (self.lock / "holder").write_text(f"pid={pid}\nstart={start}\ntoken={token}\nlabel={label}\n")

    def dead_pid(self) -> int:
        process = subprocess.Popen(["true"])
        process.wait()
        return process.pid

    def leftovers(self) -> list[str]:
        """The lock and every renamed copy of it that is still in the folder."""
        return sorted(p.name for p in self.folder.iterdir() if p.name.startswith("lock"))

    def track(self, process: subprocess.Popen) -> subprocess.Popen:
        """Makes the end of the test kill the process if it still runs."""
        self.processes.append(process)
        return process

    def stop(self):
        for run in self.runs:
            run.release.touch()
        for run in self.runs:
            try:
                run.process.wait(timeout=2)
            except subprocess.TimeoutExpired:
                pass
        for process in self.processes:
            if process.poll() is None:
                process.kill()
                process.wait()


NOT_ITS_OWN = "not this run's"
DISCARD = "Discard this run's timing"


class TheIncidentsOf20261104(unittest.TestCase):
    """The two ways in which a release removed another run's lock on 2026-11-04."""

    def test_a_stray_rmdir_after_a_failed_mkdir_leaves_the_holders_lock(self):
        bench = Bench(self)
        holder = bench.start("holder")
        holder.wait_until_it_holds()
        subprocess.run(["bash", "-c", 'mkdir "$1"; rmdir "$1"', "stray", str(bench.lock)],
                       capture_output=True, check=False)
        self.assertTrue(bench.lock.is_dir())
        self.assertEqual(bench.record().get("pid"), str(holder.process.pid))
        self.assertEqual(holder.finish(), 0)
        self.assertNotIn(NOT_ITS_OWN, holder.log_text())

    def test_a_run_whose_lock_was_removed_and_taken_again_leaves_the_new_holders_lock(self):
        for how, sent, code, expected in [("normal end", None, 0, 0), ("failing command", None, 3, 3),
                                          ("INT", signal.SIGINT, 0, 130), ("TERM", signal.SIGTERM, 0, 143)]:
            with self.subTest(how):
                bench = Bench(self)
                run = bench.start("run", code=code)
                run.wait_until_it_holds()
                shutil.rmtree(bench.lock)
                bench.lock.mkdir()
                self.assertEqual(run.finish(sent), expected)
                self.assertTrue(bench.lock.is_dir(), "the run removed the lock of the next holder")


class TheRecord(unittest.TestCase):
    """The holder writes a record into the lock directory right after its mkdir."""

    def test_the_record_names_the_holders_process_its_start_time_a_token_and_the_log_and_command(self):
        bench = Bench(self)
        run = bench.start("run")
        run.wait_until_it_holds()
        record = bench.record()
        self.assertEqual(record.get("pid"), str(run.process.pid))
        self.assertEqual(record.get("start"), start_time(run.process.pid))
        self.assertRegex(record.get("token", ""), r"\S{8,}")
        self.assertIn(str(run.log), record.get("label", ""))
        self.assertIn("held", record.get("label", ""))
        run.finish()

    def test_two_runs_get_different_tokens(self):
        bench = Bench(self)
        first = bench.start("first")
        first.wait_until_it_holds()
        token = bench.record()["token"]
        first.finish()
        second = bench.start("second")
        second.wait_until_it_holds()
        self.assertNotEqual(bench.record()["token"], token)
        second.finish()

    def test_a_run_that_cannot_write_its_record_removes_the_lock_and_does_not_run_its_command(self):
        bench = Bench(self)
        # umask 777 makes the lock directory unwritable, so the write of the record fails right after the mkdir.
        result = subprocess.run(["bash", "-c", 'umask 777; exec bash "$@"', "umask", str(HELPER),
                                 str(bench.folder / "run.log"), "touch", str(bench.folder / "ran")],
                                env=bench.env, capture_output=True, text=True, timeout=10, check=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((bench.folder / "ran").exists())
        self.assertFalse(bench.lock.exists())
        self.assertIn("record", result.stderr)


class TheRelease(unittest.TestCase):
    """The release removes the lock only when the record's token is the run's own."""

    def test_the_release_removes_its_own_lock_after_every_end(self):
        for how, sent, code, expected in [("normal end", None, 0, 0), ("failing command", None, 3, 3),
                                          ("INT", signal.SIGINT, 0, 130), ("TERM", signal.SIGTERM, 0, 143)]:
            with self.subTest(how):
                bench = Bench(self)
                run = bench.start("run", code=code)
                run.wait_until_it_holds()
                self.assertEqual(run.finish(sent), expected)
                self.assertEqual(bench.leftovers(), [])
                self.assertNotIn(NOT_ITS_OWN, run.log_text())
                self.assertEqual(list(bench.queue.iterdir()), [])

    def test_a_run_whose_lock_vanished_writes_so_into_its_log(self):
        bench = Bench(self)
        run = bench.start("run")
        run.wait_until_it_holds()
        shutil.rmtree(bench.lock)
        self.assertEqual(run.finish(), 0)
        line = [line for line in run.log_text().splitlines() if NOT_ITS_OWN in line]
        self.assertEqual(len(line), 1, run.log_text())
        self.assertIn(DISCARD, line[0])

    def test_a_run_whose_lock_another_run_holds_leaves_that_lock_with_its_record_and_writes_so_into_its_log(self):
        bench = Bench(self)
        other = bench.sleeper()
        run = bench.start("run")
        run.wait_until_it_holds()
        shutil.rmtree(bench.lock)
        bench.write_record(other.pid, start_time(other.pid), label="the other run")
        self.assertEqual(run.finish(signal.SIGTERM), 143)
        self.assertEqual(bench.record().get("token"), "someone-else")
        self.assertIn(DISCARD, run.log_text())
        self.assertIn("the other run", run.log_text())

    def test_a_waiter_that_a_signal_ends_leaves_the_holders_lock(self):
        bench = Bench(self)
        holder = bench.start("holder")
        holder.wait_until_it_holds()
        waiter = bench.start("waiter")
        time.sleep(0.3)
        waiter.process.send_signal(signal.SIGTERM)
        self.assertEqual(waiter.process.wait(timeout=10), 143)
        self.assertEqual(bench.record().get("pid"), str(holder.process.pid))
        self.assertEqual(holder.finish(), 0)


class TheStaleLock(unittest.TestCase):
    """A waiter clears a lock whose record names a process that no longer exists or that started at another time."""

    def test_a_waiter_takes_the_lock_of_a_holder_that_died(self):
        bench = Bench(self)
        dead = bench.start("dead")
        dead.wait_until_it_holds()
        dead.process.kill()
        dead.process.wait()
        dead.release.touch()
        self.assertTrue(bench.lock.is_dir())
        waiter = bench.start("waiter")
        waiter.wait_until_it_holds()
        self.assertEqual(bench.record().get("pid"), str(waiter.process.pid))
        self.assertEqual(waiter.finish(), 0)
        self.assertEqual(bench.leftovers(), [])

    def test_a_reused_process_id_with_another_start_time_counts_as_stale(self):
        bench = Bench(self)
        bench.write_record(bench.sleeper().pid, NEVER_STARTED)
        waiter = bench.start("waiter")
        waiter.wait_until_it_holds()
        self.assertEqual(waiter.finish(), 0)

    def test_a_live_holder_with_its_start_time_is_not_cleared(self):
        bench = Bench(self)
        holder = bench.sleeper()
        bench.write_record(holder.pid, start_time(holder.pid))
        waiter = bench.start("waiter")
        time.sleep(SETTLE)
        self.assertFalse(waiter.holds())
        holder.kill()
        holder.wait()
        waiter.wait_until_it_holds()
        self.assertEqual(waiter.finish(), 0)

    def test_a_lock_without_a_record_is_never_cleared(self):
        bench = Bench(self)
        bench.lock.mkdir()
        waiter = bench.start("waiter")
        time.sleep(SETTLE)
        self.assertFalse(waiter.holds())
        bench.lock.rmdir()
        waiter.wait_until_it_holds()
        self.assertEqual(waiter.finish(), 0)

    def test_two_waiters_on_a_stale_lock_take_it_one_after_the_other(self):
        bench = Bench(self)
        bench.write_record(bench.dead_pid(), NEVER_STARTED)
        times = bench.folder / "times"
        logs = [bench.folder / f"waiter-{i}.log" for i in range(2)]
        command = ["bash", "-c", 'echo "start $EPOCHREALTIME" >> "$1"; sleep 0.3; echo "end $EPOCHREALTIME" >> "$1"',
                   "timed", str(times)]
        waiters = [bench.track(subprocess.Popen(["bash", str(HELPER), str(log), *command], env=bench.env))
                   for log in logs]
        for waiter in waiters:
            self.assertEqual(waiter.wait(timeout=20), 0)
        kinds = [line.split()[0] for line in times.read_text().splitlines()]
        self.assertEqual(kinds, ["start", "end", "start", "end"])
        for log in logs:
            self.assertNotIn(NOT_ITS_OWN, log.read_text())
        self.assertEqual(bench.leftovers(), [])


class TheOldHelper(unittest.TestCase):
    """The helper of 93e46dd7f, which some worktrees still run, and this helper exclude each other."""

    def test_the_helper_waits_for_a_lock_that_the_old_helper_holds(self):
        bench = Bench(self)
        old = bench.start("old", helper=bench.old_helper())
        old.wait_until_it_holds()
        new = bench.start("new")
        time.sleep(SETTLE)
        self.assertFalse(new.holds())
        self.assertEqual(old.finish(), 0)
        new.wait_until_it_holds()
        self.assertEqual(new.finish(), 0)
        self.assertEqual(bench.leftovers(), [])

    def test_the_old_helper_waits_for_a_lock_that_the_helper_holds(self):
        bench = Bench(self)
        new = bench.start("new")
        new.wait_until_it_holds()
        old = bench.start("old", helper=bench.old_helper())
        time.sleep(SETTLE)
        self.assertFalse(old.holds())
        self.assertEqual(new.finish(), 0)
        old.wait_until_it_holds()
        self.assertEqual(old.finish(), 0)
        self.assertEqual(bench.leftovers(), [])


if __name__ == "__main__":
    unittest.main()
