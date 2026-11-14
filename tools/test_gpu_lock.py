#!/usr/bin/env python3
"""Tool code. The tests of tools/gpu-lock.sh, the helper of the GPU lock: the holder's record, the release that removes
only its own lock, the clear of a stale lock, the queue's tickets, the exclusion between the helper and its older copy
of 93e46dd7f, and the record and liveness rule that the helper shares with the Stryker lock of tools/stryker/run.py.

    nix develop --command python3 -I tools/test_gpu_lock.py

The standard library only. Each test gets a fresh folder under /private/tmp and gives the helper its lock and its queue
there, through TERTIUS_GPU_LOCK and TERTIUS_GPU_QUEUE. No test touches the real lock or the real queue, because other
runs measure on this machine. The tests of the shared record give the Stryker lock its folder there too, never
/tmp/tertius-stryker.lock.
"""

import os
import shlex
import shutil
import signal
import sys
import subprocess
import tempfile
import time
import unittest
from pathlib import Path

HELPER = Path(__file__).with_name("gpu-lock.sh")
STRYKER_RUN = Path(__file__).with_name("stryker") / "run.py"
REAL_LOCK = "/tmp/tertius-gpu-measure.lock"
REAL_QUEUE = "/tmp/tertius-gpu-measure.queue"
# The helper before this ticket, whose lock is an empty directory.
OLD_HELPER_COMMIT = "93e46dd7f"
# The helper before the shared mode (te-vnjc), which has the record and the queue's records but no mode.
MODELESS_HELPER_COMMIT = "8fe7ac7cf"
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
    """The start time of the process `pid` as the helper records it: in UTC, with its spaces squeezed."""
    out = subprocess.run(["ps", "-o", "lstart=", "-p", str(pid)], env={**os.environ, "TZ": "UTC0", "LC_ALL": "C"},
                         capture_output=True, text=True, check=False).stdout
    return " ".join(out.split())


class Run:
    """One run of a helper whose command holds the GPU until the test lets it go."""

    def __init__(self, bench: "Bench", name: str, helper: Path, code: int, env: dict[str, str], shared: bool = False):
        self.log = bench.folder / f"{name}.log"
        self.started = bench.folder / f"{name}.started"
        self.release = bench.folder / f"{name}.release"
        # The command also ends when its started marker vanishes with the test's folder, so that no test leaves it
        # behind.
        command = ["bash", "-c", 'touch "$1"; while [ ! -e "$2" ] && [ -e "$1" ]; do sleep 0.02; done; exit "$3"',
                   "held", str(self.started), str(self.release), str(code)]
        mode = ["--shared"] if shared else []
        self.process = subprocess.Popen(["bash", str(helper), *mode, str(self.log), *command], env={**bench.env, **env},
                                        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

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

    def start(self, name: str, helper: Path = HELPER, code: int = 0, env: dict[str, str] | None = None,
              shared: bool = False) -> Run:
        """Starts a run whose environment is the bench's, with `env` over it. A `shared` run asks for the shared
        mode."""
        run = Run(self, name, helper, code, env or {}, shared)
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

    def modeless_helper(self) -> Path:
        """The helper of 8fe7ac7cf, before the shared mode. It takes its paths from the bench's environment."""
        path = self.folder / "modeless-gpu-lock.sh"
        source = f"{MODELESS_HELPER_COMMIT}:tools/gpu-lock.sh"
        path.write_text(subprocess.run(["git", "show", source], cwd=HELPER.parent, capture_output=True, text=True,
                                       check=True).stdout)
        return path

    def shim(self, name: str, body: str) -> dict[str, str]:
        """An environment whose PATH finds the bash `body` as the command `name` first. The body finds the real command
        in $real."""
        folder = self.folder / "bin"
        folder.mkdir(exist_ok=True)
        path = folder / name
        path.write_text(f"#!/bin/bash\nreal={shlex.quote(shutil.which(name))}\n{body}\n")
        path.chmod(0o755)
        return {"PATH": f"{folder}{os.pathsep}{os.environ['PATH']}"}

    def counting_ps(self) -> dict[str, str]:
        """An environment whose ps writes its arguments into a file of the bench before it runs."""
        return self.shim("ps", f'echo "$*" >> {shlex.quote(str(self.folder / "ps-calls"))}; exec "$real" "$@"')

    def ps_calls_about(self, pid: int | str) -> int:
        calls = self.folder / "ps-calls"
        return sum(1 for line in calls.read_text().splitlines() if line.endswith(f"-p {pid}")) if calls.exists() else 0

    def write_ticket(self, pid: int, start: str, name: str = "0000000000000000") -> Path:
        """Writes the ticket of a waiter that came before every run of the test, with the record `pid` and `start`."""
        ticket = self.queue / f"{name}-{pid}"
        ticket.write_text(f"pid={pid}\nstart={start}\ntoken=older-waiter\nlabel=an older waiter\n")
        return ticket

    def sleeper(self) -> subprocess.Popen:
        return self.track(subprocess.Popen(["sleep", "60"]))

    def record(self) -> dict[str, str]:
        holder = self.lock / "holder"
        if not holder.exists():
            return {}
        return dict(line.split("=", 1) for line in holder.read_text().splitlines() if "=" in line)

    def write_record(self, pid: int | str, start: str, token: str = "someone-else", label: str = "a test's holder"):
        """Makes the lock as a holder with this record would."""
        self.lock.mkdir()
        (self.lock / "holder").write_text(f"pid={pid}\nstart={start}\ntoken={token}\nlabel={label}\n")

    def dead_pid(self) -> int:
        process = subprocess.Popen(["true"])
        process.wait()
        return process.pid

    def shared_records(self) -> dict[str, dict[str, str]]:
        """The records of the shared holders, by file name."""
        folder = self.lock / "shared"
        if not folder.is_dir():
            return {}
        return {p.name: dict(line.split("=", 1) for line in p.read_text().splitlines() if "=" in line)
                for p in folder.iterdir()}

    def write_shared_record(self, pid: int | str, start: str, name: str = "0000000000000000-1",
                            token: str = "someone-else"):
        """Makes the lock as a shared holder with this record would, or joins the shared holders in it."""
        (self.lock / "shared").mkdir(parents=True, exist_ok=True)
        (self.lock / "shared" / name).write_text(f"pid={pid}\nstart={start}\ntoken={token}\nlabel=a shared holder\n")

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


class TheCommandKnowsThatItHoldsTheLock(unittest.TestCase):
    """The command runs with TERTIUS_GPU_LOCK_HELD set to the mode that it holds, so that a GPU test run inside it does
    not wait for the lock that its own parent holds (ticket ter-zoof)."""

    def held_as(self, shared: bool) -> str:
        bench = Bench(self)
        seen = bench.folder / "seen"
        mode = ["--shared"] if shared else []
        command = ["bash", "-c", 'printf %s "${TERTIUS_GPU_LOCK_HELD-unset}" > "$1"', "seen", str(seen)]
        subprocess.run(["bash", str(HELPER), *mode, str(bench.folder / "run.log"), *command], env=bench.env,
                       check=True, timeout=30)
        return seen.read_text()

    def test_an_exclusive_run_tells_its_command_exclusive(self):
        self.assertEqual(self.held_as(shared=False), "exclusive")

    def test_a_shared_run_tells_its_command_shared(self):
        self.assertEqual(self.held_as(shared=True), "shared")


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
                                          ("INT", signal.SIGINT, 0, 130), ("TERM", signal.SIGTERM, 0, 143),
                                          ("HUP", signal.SIGHUP, 0, 129)]:
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

    def test_a_newline_in_the_log_path_cannot_split_the_holders_record(self):
        bench = Bench(self)
        run = bench.start("run\ntoken=foreign")
        run.wait_until_it_holds()
        self.assertIn(str(run.log).replace("\n", " "), bench.record().get("label", ""))
        self.assertEqual(len((bench.lock / "holder").read_text().splitlines()), 4)
        self.assertEqual(run.finish(), 0)
        self.assertEqual(bench.leftovers(), [])
        self.assertNotIn(NOT_ITS_OWN, run.log_text())

    def test_a_failed_record_rename_removes_the_lock_without_running_the_command(self):
        bench = Bench(self)
        env = {**bench.env, **bench.shim("mv", 'case "$2" in */holder) exit 1;; esac; exec "$real" "$@"')}
        result = subprocess.run(["bash", str(HELPER), str(bench.folder / "run.log"), "touch", str(bench.folder / "ran")],
                                env=env, capture_output=True, text=True, timeout=10, check=False)
        self.assertEqual(result.returncode, 1)
        self.assertFalse((bench.folder / "ran").exists())
        self.assertEqual(bench.leftovers(), [])
        self.assertEqual(list(bench.queue.iterdir()), [])
        self.assertIn("record", result.stderr)

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

    def test_a_run_whose_ps_gives_no_start_time_removes_the_lock_and_does_not_run_its_command(self):
        bench = Bench(self)
        env = {**bench.env, **bench.shim("ps", "exit 0")}
        result = subprocess.run(["bash", str(HELPER), str(bench.folder / "run.log"), "touch", str(bench.folder / "ran")],
                                env=env, capture_output=True, text=True, timeout=10, check=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((bench.folder / "ran").exists())
        self.assertFalse(bench.lock.exists())
        self.assertIn("record", result.stderr)


class TheRelease(unittest.TestCase):
    """The release removes the lock only when the record's token is the run's own."""

    def test_the_release_removes_its_own_lock_after_every_end(self):
        for how, sent, code, expected in [("normal end", None, 0, 0), ("failing command", None, 3, 3),
                                          ("INT", signal.SIGINT, 0, 130), ("TERM", signal.SIGTERM, 0, 143),
                                          ("HUP", signal.SIGHUP, 0, 129)]:
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

    def test_a_signal_between_the_mkdir_and_the_record_ends_the_run_without_a_lock(self):
        for how, sent, expected in [("INT", signal.SIGINT, 130), ("TERM", signal.SIGTERM, 143),
                                    ("HUP", signal.SIGHUP, 129)]:
            with self.subTest(how):
                bench = Bench(self)
                # A slow rename of the record holds the run between its mkdir and its record.
                run = bench.start("run", env=bench.shim("mv", 'case "$2" in */holder) sleep 1;; esac; exec "$real" "$@"'))
                self.assertTrue(wait_for(bench.lock.exists))
                self.assertEqual(bench.record(), {})
                run.process.send_signal(sent)
                self.assertEqual(run.process.wait(timeout=10), expected)
                self.assertFalse(run.holds())
                self.assertEqual(bench.leftovers(), [])
                self.assertEqual(list(bench.queue.iterdir()), [])

    def test_a_signal_during_the_release_does_not_stop_it_halfway(self):
        for sent in [signal.SIGINT, signal.SIGTERM, signal.SIGHUP]:
            with self.subTest(sent):
                bench = Bench(self)
                renaming = bench.folder / "renaming"
                run = bench.start("run", env=bench.shim("mv", f'case "$2" in *.released.*) touch {shlex.quote(str(renaming))}; '
                                                              'sleep 1;; esac; exec "$real" "$@"'))
                run.wait_until_it_holds()
                run.release.touch()
                self.assertTrue(wait_for(renaming.exists))
                run.process.send_signal(sent)
                self.assertEqual(run.process.wait(timeout=10), 0)
                self.assertEqual(bench.leftovers(), [])
                self.assertNotIn(NOT_ITS_OWN, run.log_text())

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

    def test_only_the_oldest_live_waiter_can_clear_and_take_a_stale_lock(self):
        bench = Bench(self)
        older = bench.sleeper()
        oldest = bench.queue / f"0000000000000000-{older.pid}"
        oldest.touch()
        bench.write_record(bench.dead_pid(), NEVER_STARTED)
        waiter = bench.start("waiter")
        self.assertTrue(wait_for(lambda: len(list(bench.queue.iterdir())) == 2))
        time.sleep(SETTLE)
        self.assertFalse(waiter.holds())
        self.assertEqual(bench.record().get("token"), "someone-else")
        older.kill()
        older.wait()
        waiter.wait_until_it_holds()
        self.assertEqual(waiter.finish(), 0)
        self.assertEqual(list(bench.queue.iterdir()), [])

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

    def test_a_holder_that_kill_cannot_signal_is_not_cleared(self):
        # kill -0 of process 1, which another user runs, fails with "Operation not permitted", not "No such process".
        bench = Bench(self)
        bench.write_record(1, start_time(1))
        waiter = bench.start("waiter")
        time.sleep(SETTLE)
        self.assertFalse(waiter.holds())
        self.assertEqual(bench.record().get("token"), "someone-else")

    def test_a_live_holder_is_not_cleared_when_ps_gives_the_waiter_nothing(self):
        bench = Bench(self)
        holder = bench.sleeper()
        bench.write_record(holder.pid, start_time(holder.pid))
        waiter = bench.start("waiter", env=bench.shim("ps", "exit 0"))
        time.sleep(SETTLE)
        self.assertFalse(waiter.holds())
        self.assertEqual(bench.record().get("token"), "someone-else")

    def test_a_live_holder_in_another_time_zone_is_not_cleared(self):
        bench = Bench(self)
        holder = bench.start("holder", env={"TZ": "UTC"})
        holder.wait_until_it_holds()
        waiter = bench.start("waiter", env={"TZ": "Asia/Tokyo"})
        time.sleep(SETTLE)
        self.assertFalse(waiter.holds())
        self.assertEqual(bench.record().get("pid"), str(holder.process.pid))
        self.assertEqual(holder.finish(), 0)
        waiter.wait_until_it_holds()
        self.assertEqual(waiter.finish(), 0)

    def test_a_record_that_lacks_a_field_is_never_cleared(self):
        for lacks in ["pid", "start", "token"]:
            with self.subTest(lacks):
                bench = Bench(self)
                # Each record would be stale with the field: a dead process, or a live one with another start time.
                record = {"pid": bench.dead_pid(), "start": NEVER_STARTED, "token": "someone-else"}
                if lacks == "start":
                    record["pid"] = bench.sleeper().pid
                record[lacks] = ""
                bench.write_record(**record)
                waiter = bench.start("waiter")
                time.sleep(SETTLE)
                self.assertFalse(waiter.holds())
                self.assertEqual(bench.record().get(lacks), "")

    def test_an_incomplete_live_record_cannot_be_cleared_when_it_becomes_complete_during_the_rename(self):
        bench = Bench(self)
        holder = bench.sleeper()
        bench.write_record(holder.pid, "")
        complete = f"pid={holder.pid}\nstart={start_time(holder.pid)}\ntoken=someone-else\nlabel=the live holder\n"
        finish_record = (f'case "$2" in *.stale.*) printf %s {shlex.quote(complete)} > "$1/holder";; esac; '
                         'exec "$real" "$@"')
        waiter = bench.start("waiter", env=bench.shim("mv", finish_record))
        time.sleep(SETTLE)
        self.assertFalse(waiter.holds())
        (bench.lock / "holder").write_text(complete)
        time.sleep(SETTLE)
        self.assertFalse(waiter.holds())
        self.assertEqual(bench.record().get("pid"), str(holder.pid))
        holder.kill()
        holder.wait()
        waiter.wait_until_it_holds()
        self.assertEqual(waiter.finish(), 0)
        self.assertEqual(bench.leftovers(), [])

    def test_a_record_with_a_non_ascii_pid_is_not_probed_as_a_process(self):
        bench = Bench(self)
        bench.write_record("\u00b2", NEVER_STARTED)
        env = bench.counting_ps()
        attempts = bench.folder / "take-attempts"
        bench.shim("mkdir", f'if [ "$1" = {shlex.quote(str(bench.lock))} ]; then '
                   f'echo take >> {shlex.quote(str(attempts))}; fi; exec "$real" "$@"')
        waiter = bench.start("waiter", env=env)
        self.assertTrue(wait_for(lambda: attempts.exists() and len(attempts.read_text().splitlines()) >= 2))
        self.assertFalse(waiter.holds())
        self.assertEqual(bench.ps_calls_about("\u00b2"), 0)

    def test_a_waiter_asks_ps_about_a_live_holder_at_most_once_a_second(self):
        bench = Bench(self)
        holder = bench.sleeper()
        bench.write_record(holder.pid, start_time(holder.pid))
        waiter = bench.start("waiter", env=bench.counting_ps())
        time.sleep(2.0)
        self.assertFalse(waiter.holds())
        self.assertLessEqual(bench.ps_calls_about(holder.pid), 3)

    def test_a_lock_that_another_run_takes_during_the_clear_goes_back_with_its_record(self):
        bench = Bench(self)
        bench.write_record(bench.dead_pid(), NEVER_STARTED)
        live = bench.sleeper()
        swapped = bench.folder / "swapped"
        # Once, right before the waiter renames the stale lock, another run's record replaces the stale one, as if
        # that run had cleared the lock and taken it since the waiter read the record.
        swap = (f'if [[ "$2" == *.stale.* && ! -e {shlex.quote(str(swapped))} ]]; then '
                f'touch {shlex.quote(str(swapped))}; '
                f'printf "pid={live.pid}\\nstart={start_time(live.pid)}\\ntoken=live\\nlabel=x\\n" > "$1/holder"; fi; '
                'exec "$real" "$@"')
        waiter = bench.start("waiter", env=bench.shim("mv", swap))
        self.assertTrue(wait_for(swapped.exists))
        time.sleep(SETTLE)
        self.assertFalse(waiter.holds())
        self.assertEqual(bench.record().get("token"), "live")
        self.assertEqual(bench.leftovers(), ["lock"])
        live.kill()
        live.wait()
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


class TheQueue(unittest.TestCase):
    """Each waiter's ticket holds the waiter's record, so that the queue judges a waiter as the lock judges a holder."""

    def test_a_waiters_ticket_holds_the_record_that_it_writes_into_the_lock(self):
        bench = Bench(self)
        holder = bench.start("holder")
        holder.wait_until_it_holds()
        waiter = bench.start("waiter")
        self.assertTrue(wait_for(lambda: any(p.name.endswith(f"-{waiter.process.pid}") and p.read_text()
                                             for p in bench.queue.iterdir())))
        ticket = next(p for p in bench.queue.iterdir() if p.name.endswith(f"-{waiter.process.pid}"))
        record = dict(line.split("=", 1) for line in ticket.read_text().splitlines() if "=" in line)
        self.assertEqual(record.get("pid"), str(waiter.process.pid))
        self.assertEqual(record.get("start"), start_time(waiter.process.pid))
        self.assertEqual(holder.finish(), 0)
        waiter.wait_until_it_holds()
        self.assertEqual(bench.record(), record)
        self.assertEqual(waiter.finish(), 0)

    def test_an_older_ticket_whose_process_id_a_later_process_reuses_does_not_block_the_queue(self):
        bench = Bench(self)
        older = bench.write_ticket(bench.sleeper().pid, NEVER_STARTED)
        waiter = bench.start("waiter")
        waiter.wait_until_it_holds()
        self.assertFalse(older.exists())
        self.assertEqual(waiter.finish(), 0)

    def test_an_older_ticket_of_a_live_waiter_keeps_its_turn_until_that_waiter_ends(self):
        bench = Bench(self)
        older = bench.sleeper()
        bench.write_ticket(older.pid, start_time(older.pid))
        waiter = bench.start("waiter")
        time.sleep(SETTLE)
        self.assertFalse(waiter.holds())
        older.kill()
        older.wait()
        waiter.wait_until_it_holds()
        self.assertEqual(waiter.finish(), 0)

    def test_a_waiter_asks_ps_about_an_older_live_waiter_at_most_once_a_second(self):
        bench = Bench(self)
        older = bench.sleeper()
        bench.write_ticket(older.pid, start_time(older.pid))
        waiter = bench.start("waiter", env=bench.counting_ps())
        time.sleep(2.0)
        self.assertFalse(waiter.holds())
        self.assertLessEqual(bench.ps_calls_about(older.pid), 3)


class TheSharedMode(unittest.TestCase):
    """Runs that ask for the shared mode hold the GPU side by side, up to a cap, and never beside an exclusive run."""

    def test_three_shared_runs_hold_the_gpu_at_once(self):
        bench = Bench(self)
        runs = [bench.start(f"shared-{i}", shared=True) for i in range(3)]
        for run in runs:
            run.wait_until_it_holds()
        self.assertEqual(sorted(r["pid"] for r in bench.shared_records().values()),
                         sorted(str(run.process.pid) for run in runs))
        for run in runs:
            self.assertEqual(run.finish(), 0)
        self.assertEqual(bench.leftovers(), [])
        self.assertEqual(list(bench.queue.iterdir()), [])

    def test_a_fourth_shared_run_waits_until_one_of_three_ends(self):
        bench = Bench(self)
        runs = [bench.start(f"shared-{i}", shared=True) for i in range(3)]
        for run in runs:
            run.wait_until_it_holds()
        fourth = bench.start("fourth", shared=True)
        time.sleep(SETTLE)
        self.assertFalse(fourth.holds())
        self.assertEqual(runs[1].finish(), 0)
        fourth.wait_until_it_holds()
        self.assertEqual(len(bench.shared_records()), 3)

    def test_the_cap_comes_from_the_environment(self):
        bench = Bench(self)
        cap = {"TERTIUS_GPU_SHARED_CAP": "2"}
        runs = [bench.start(f"shared-{i}", shared=True, env=cap) for i in range(2)]
        for run in runs:
            run.wait_until_it_holds()
        third = bench.start("third", shared=True, env=cap)
        time.sleep(SETTLE)
        self.assertFalse(third.holds())
        self.assertEqual(runs[0].finish(), 0)
        third.wait_until_it_holds()

    def test_a_cap_that_is_not_a_positive_number_ends_the_run_before_it_waits(self):
        for cap in ["0", "two", "-1", ""]:
            with self.subTest(cap=cap):
                bench = Bench(self)
                env = {**bench.env, "TERTIUS_GPU_SHARED_CAP": cap}
                result = subprocess.run(["bash", str(HELPER), "--shared", str(bench.folder / "run.log"), "touch",
                                         str(bench.folder / "ran")], env=env, capture_output=True, text=True,
                                        timeout=10, check=False)
                self.assertEqual(result.returncode, 2)
                self.assertIn("TERTIUS_GPU_SHARED_CAP", result.stderr)
                self.assertFalse((bench.folder / "ran").exists())
                self.assertEqual(list(bench.queue.iterdir()), [])
                self.assertEqual(bench.leftovers(), [])

    def test_a_shared_run_waits_while_an_exclusive_run_holds_the_gpu(self):
        bench = Bench(self)
        exclusive = bench.start("exclusive")
        exclusive.wait_until_it_holds()
        shared = bench.start("shared", shared=True)
        time.sleep(SETTLE)
        self.assertFalse(shared.holds())
        self.assertEqual(exclusive.finish(), 0)
        shared.wait_until_it_holds()
        self.assertEqual(shared.finish(), 0)
        self.assertEqual(bench.leftovers(), [])

    def test_an_exclusive_run_waits_until_every_shared_run_ends(self):
        bench = Bench(self)
        shared = [bench.start(f"shared-{i}", shared=True) for i in range(2)]
        for run in shared:
            run.wait_until_it_holds()
        exclusive = bench.start("exclusive")
        time.sleep(SETTLE)
        self.assertFalse(exclusive.holds())
        self.assertEqual(shared[0].finish(), 0)
        time.sleep(SETTLE)
        self.assertFalse(exclusive.holds())
        self.assertEqual(shared[1].finish(), 0)
        exclusive.wait_until_it_holds()
        self.assertEqual(bench.record().get("pid"), str(exclusive.process.pid))
        self.assertEqual(exclusive.finish(), 0)
        self.assertEqual(bench.leftovers(), [])

    def test_shared_and_exclusive_runs_never_overlap_and_the_shared_ones_overlap_each_other(self):
        bench = Bench(self)
        times = bench.folder / "times"
        command = ["bash", "-c",
                   'echo "start $2 $EPOCHREALTIME" >> "$1"; sleep 0.4; echo "end $2 $EPOCHREALTIME" >> "$1"',
                   "timed", str(times)]
        modes = ["shared", "shared", "exclusive", "shared", "shared", "exclusive", "shared"]
        processes = []
        for i, mode in enumerate(modes):
            flag = ["--shared"] if mode == "shared" else []
            processes.append(bench.track(subprocess.Popen(["bash", str(HELPER), *flag, str(bench.folder / f"{i}.log"),
                                                           *command, f"{mode}-{i}"], env=bench.env)))
            time.sleep(0.05)
        for process in processes:
            self.assertEqual(process.wait(timeout=30), 0)
        holding: set[str] = set()
        most_shared = 0
        for line in times.read_text().splitlines():
            kind, name, _ = line.split()
            if kind == "start":
                self.assertFalse(name.startswith("exclusive") and holding, f"{name} started beside {holding}")
                self.assertFalse(any(h.startswith("exclusive") for h in holding), f"{name} started beside {holding}")
                holding.add(name)
                most_shared = max(most_shared, len(holding))
            else:
                holding.remove(name)
        self.assertEqual(most_shared, 2)
        self.assertEqual(bench.leftovers(), [])


class TheOrderAcrossModes(unittest.TestCase):
    """The queue stays first in, first out across both modes, so that shared runs cannot starve an exclusive run."""

    def test_a_shared_run_that_comes_after_a_waiting_exclusive_run_waits_behind_it(self):
        bench = Bench(self)
        first = bench.start("first", shared=True)
        first.wait_until_it_holds()
        exclusive = bench.start("exclusive")
        self.assertTrue(wait_for(lambda: len(list(bench.queue.iterdir())) == 1))
        later = bench.start("later", shared=True)
        time.sleep(SETTLE)
        self.assertFalse(later.holds(), "a shared run went past a waiting exclusive run")
        self.assertEqual(first.finish(), 0)
        exclusive.wait_until_it_holds()
        time.sleep(SETTLE)
        self.assertFalse(later.holds())
        self.assertEqual(exclusive.finish(), 0)
        later.wait_until_it_holds()
        self.assertEqual(later.finish(), 0)

    def test_shared_runs_at_the_head_of_the_queue_start_together_up_to_the_cap(self):
        bench = Bench(self)
        exclusive = bench.start("exclusive")
        exclusive.wait_until_it_holds()
        waiters = []
        for i in range(4):
            waiters.append(bench.start(f"shared-{i}", shared=True))
            self.assertTrue(wait_for(lambda: len(list(bench.queue.iterdir())) == i + 1))
        self.assertEqual(exclusive.finish(), 0)
        for run in waiters[:3]:
            run.wait_until_it_holds()
        time.sleep(SETTLE)
        self.assertFalse(waiters[3].holds())
        self.assertEqual(waiters[0].finish(), 0)
        waiters[3].wait_until_it_holds()

    def test_a_ticket_without_a_mode_counts_as_an_exclusive_waiter(self):
        # The ticket of an older helper names no mode. A shared run that comes after it must not go past it, though
        # a shared run holds the GPU and the cap leaves room.
        bench = Bench(self)
        first = bench.start("first", shared=True)
        first.wait_until_it_holds()
        older = bench.sleeper()
        bench.write_ticket(older.pid, start_time(older.pid))
        later = bench.start("later", shared=True)
        time.sleep(SETTLE)
        self.assertFalse(later.holds())
        older.kill()
        older.wait()
        later.wait_until_it_holds()
        self.assertEqual(first.finish(), 0)
        self.assertEqual(later.finish(), 0)


class TheSharedRelease(unittest.TestCase):
    """A shared holder removes only its own record, and the last one removes the lock."""

    def test_a_shared_holders_record_is_the_record_of_a_machine_lock_in_a_file_of_its_own(self):
        bench = Bench(self)
        run = bench.start("run", shared=True)
        run.wait_until_it_holds()
        self.assertEqual(bench.record(), {}, "a shared holder writes no exclusive record")
        records = list(bench.shared_records().values())
        self.assertEqual(len(records), 1)
        self.assertEqual(records[0].get("pid"), str(run.process.pid))
        self.assertEqual(records[0].get("start"), start_time(run.process.pid))
        self.assertRegex(records[0].get("token", ""), r"\S{8,}")
        self.assertIn(str(run.log), records[0].get("label", ""))
        self.assertEqual(run.finish(), 0)

    def test_the_shared_release_removes_its_own_record_after_every_end(self):
        for how, sent, code, expected in [("normal end", None, 0, 0), ("failing command", None, 3, 3),
                                          ("INT", signal.SIGINT, 0, 130), ("TERM", signal.SIGTERM, 0, 143),
                                          ("HUP", signal.SIGHUP, 0, 129)]:
            with self.subTest(how):
                bench = Bench(self)
                other = bench.start("other", shared=True)
                other.wait_until_it_holds()
                run = bench.start("run", shared=True, code=code)
                run.wait_until_it_holds()
                self.assertEqual(run.finish(sent), expected)
                self.assertEqual([r["pid"] for r in bench.shared_records().values()], [str(other.process.pid)])
                self.assertNotIn(NOT_ITS_OWN, run.log_text())
                self.assertEqual(other.finish(), 0)
                self.assertEqual(bench.leftovers(), [])
                self.assertEqual(list(bench.queue.iterdir()), [])

    def test_shared_runs_that_end_together_leave_no_lock(self):
        for _ in range(5):
            bench = Bench(self)
            runs = [bench.start(f"shared-{i}", shared=True) for i in range(3)]
            for run in runs:
                run.wait_until_it_holds()
            for run in runs:
                run.release.touch()
            for run in runs:
                self.assertEqual(run.process.wait(timeout=10), 0)
            self.assertEqual(bench.leftovers(), [])

    def test_a_shared_run_whose_record_vanished_writes_so_into_its_log(self):
        bench = Bench(self)
        other = bench.start("other", shared=True)
        other.wait_until_it_holds()
        run = bench.start("run", shared=True)
        run.wait_until_it_holds()
        for name, record in bench.shared_records().items():
            if record["pid"] == str(run.process.pid):
                (bench.lock / "shared" / name).unlink()
        self.assertEqual(run.finish(), 0)
        line = [line for line in run.log_text().splitlines() if NOT_ITS_OWN in line]
        self.assertEqual(len(line), 1, run.log_text())
        self.assertEqual([r["pid"] for r in bench.shared_records().values()], [str(other.process.pid)])
        self.assertEqual(other.finish(), 0)

    def test_a_shared_run_whose_lock_another_run_took_leaves_that_lock(self):
        bench = Bench(self)
        live = bench.sleeper()
        run = bench.start("run", shared=True)
        run.wait_until_it_holds()
        shutil.rmtree(bench.lock)
        bench.write_record(live.pid, start_time(live.pid), label="the other run")
        self.assertEqual(run.finish(), 0)
        self.assertEqual(bench.record().get("token"), "someone-else")
        self.assertIn(DISCARD, run.log_text())

    def test_a_shared_run_says_so_in_its_log(self):
        bench = Bench(self)
        run = bench.start("run", shared=True)
        run.wait_until_it_holds()
        self.assertIn("mode: shared", run.log_text())
        self.assertEqual(run.finish(), 0)
        exclusive = bench.start("exclusive")
        exclusive.wait_until_it_holds()
        self.assertNotIn("mode: shared", exclusive.log_text())
        self.assertEqual(exclusive.finish(), 0)


class TheStaleSharedHolder(unittest.TestCase):
    """A shared holder that is gone does not block the GPU: the oldest waiter removes its record."""

    def test_an_exclusive_waiter_takes_the_gpu_after_a_shared_holder_died(self):
        bench = Bench(self)
        dead = bench.start("dead", shared=True)
        dead.wait_until_it_holds()
        dead.process.kill()
        dead.process.wait()
        dead.release.touch()
        waiter = bench.start("waiter")
        waiter.wait_until_it_holds()
        self.assertEqual(bench.record().get("pid"), str(waiter.process.pid))
        self.assertEqual(waiter.finish(), 0)
        self.assertEqual(bench.leftovers(), [])

    def test_an_exclusive_waiter_removes_a_dead_shared_holder_and_waits_for_a_live_one(self):
        bench = Bench(self)
        live = bench.sleeper()
        bench.write_shared_record(bench.dead_pid(), NEVER_STARTED, name="0000000000000000-1")
        bench.write_shared_record(live.pid, start_time(live.pid), name="0000000000000001-2")
        waiter = bench.start("waiter")
        self.assertTrue(wait_for(lambda: len(bench.shared_records()) == 1))
        time.sleep(SETTLE)
        self.assertFalse(waiter.holds())
        self.assertEqual([r["pid"] for r in bench.shared_records().values()], [str(live.pid)])
        live.kill()
        live.wait()
        waiter.wait_until_it_holds()
        self.assertEqual(waiter.finish(), 0)
        self.assertEqual(bench.leftovers(), [])

    def test_a_shared_waiter_at_the_cap_takes_the_place_of_a_dead_holder(self):
        bench = Bench(self)
        cap = {"TERTIUS_GPU_SHARED_CAP": "1"}
        bench.write_shared_record(bench.sleeper().pid, NEVER_STARTED)
        waiter = bench.start("waiter", shared=True, env=cap)
        waiter.wait_until_it_holds()
        self.assertEqual([r["pid"] for r in bench.shared_records().values()], [str(waiter.process.pid)])
        self.assertEqual(waiter.finish(), 0)
        self.assertEqual(bench.leftovers(), [])

    def test_a_shared_record_that_lacks_a_field_is_never_removed(self):
        for lacks in ["pid", "start", "token"]:
            with self.subTest(lacks):
                bench = Bench(self)
                record = {"pid": bench.dead_pid(), "start": NEVER_STARTED, "token": "someone-else"}
                record[lacks] = ""
                bench.write_shared_record(**record)
                waiter = bench.start("waiter")
                time.sleep(SETTLE)
                self.assertFalse(waiter.holds())
                self.assertEqual(len(bench.shared_records()), 1)

    def test_a_waiter_asks_ps_about_each_live_shared_holder_at_most_once_a_second(self):
        bench = Bench(self)
        holders = [bench.sleeper() for _ in range(2)]
        for i, holder in enumerate(holders):
            bench.write_shared_record(holder.pid, start_time(holder.pid), name=f"000000000000000{i}-{holder.pid}")
        waiter = bench.start("waiter", env=bench.counting_ps())
        time.sleep(2.0)
        self.assertFalse(waiter.holds())
        for holder in holders:
            self.assertLessEqual(bench.ps_calls_about(holder.pid), 3)

    def test_each_shared_holder_with_a_reused_process_id_is_found_gone(self):
        # Both holders' pids live, with other start times: only ps finds them gone, and ps runs once a second.
        bench = Bench(self)
        for i in range(2):
            bench.write_shared_record(bench.sleeper().pid, NEVER_STARTED, name=f"000000000000000{i}-{i}")
        waiter = bench.start("waiter")
        waiter.wait_until_it_holds()
        self.assertEqual(waiter.finish(), 0)
        self.assertEqual(bench.leftovers(), [])


# A Stryker lock in a process of its own: it takes the lock at argv[2] and releases it. With argv[3] and argv[4], it
# makes the file argv[3] once it holds the lock, and it holds the lock until the file argv[4] exists or argv[3]
# vanishes. A run that stops on a stale lock prints its message and exits with 1.
PYTHON_LOCK = """
import importlib.util, os, sys, time
spec = importlib.util.spec_from_file_location("run", sys.argv[1])
run = importlib.util.module_from_spec(spec)
spec.loader.exec_module(run)
lock = run.Lock(sys.argv[2], poll=0.01, say=lambda _: None)
lock.acquire()
if len(sys.argv) > 3:
    open(sys.argv[3], "w").close()
    while not os.path.exists(sys.argv[4]) and os.path.exists(sys.argv[3]):
        time.sleep(0.02)
lock.release()
print("took")
"""

HELD, GONE = "held", "gone"


class TheSharedRecord(unittest.TestCase):
    """The GPU lock and the Stryker lock write one record and judge it with one liveness rule (tools/README.md, "The
    record of a machine lock"). A record that one lock writes, the other judges as the writer itself does."""

    @classmethod
    def setUpClass(cls):
        # A live process that started at least a second before each holder of these tests. A record with its pid and a
        # holder's start time names a reused process id.
        cls.reused = subprocess.Popen(["sleep", "300"])
        time.sleep(1.1)

    @classmethod
    def tearDownClass(cls):
        cls.reused.kill()
        cls.reused.wait()

    def written_by(self, writer: str, case: str) -> str | None:
        """The text of the record that a holder of the lock `writer` ("bash" or "python") writes, brought into the
        state `case`. None is no record."""
        if case == "no record":
            return None
        bench = Bench(self)
        if writer == "bash":
            holder = bench.start("writer")
            holder.wait_until_it_holds()
            process = holder.process
        else:
            held, release = bench.folder / "writer.held", bench.folder / "writer.release"
            process = bench.track(subprocess.Popen([sys.executable, "-I", "-c", PYTHON_LOCK, str(STRYKER_RUN),
                                                    str(bench.lock), str(held), str(release)]))
            self.assertTrue(wait_for(held.exists))
        text = (bench.lock / "holder").read_text()
        if case == "alive":
            return text
        process.kill()
        process.wait()
        if case == "gone by start":
            return "".join(f"pid={self.reused.pid}\n" if line.startswith("pid=") else line
                           for line in text.splitlines(keepends=True))
        if case == "incomplete":
            return "".join(line for line in text.splitlines(keepends=True) if not line.startswith("start="))
        return text

    def lock_with(self, bench: Bench, text: str | bytes | None):
        bench.lock.mkdir()
        if text is not None:
            (bench.lock / "holder").write_bytes(text if isinstance(text, bytes) else text.encode())

    def verdicts(self, text: str | bytes | None, expected: str) -> dict[str, str]:
        """How the GPU helper and the Stryker lock judge a lock that holds the record `text`, each on a copy of its
        own: GONE when the GPU helper takes it or the Stryker lock stops with the command that removes it, HELD when
        each waits. For an `expected` GONE, both get up to 10 s, so that a slow start on a loaded machine still counts.
        For an expected HELD, both get SETTLE, the time in which a wrong GONE shows."""
        gpu, stryker = Bench(self), Bench(self)
        self.lock_with(gpu, text)
        self.lock_with(stryker, text)
        python = stryker.track(subprocess.Popen([sys.executable, "-I", "-c", PYTHON_LOCK, str(STRYKER_RUN),
                                                 str(stryker.lock)], stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                                text=True))
        waiter = gpu.start("judge")
        if expected == GONE:
            wait_for(lambda: waiter.holds() and python.poll() is not None)
        else:
            time.sleep(SETTLE)
        bash = GONE if waiter.holds() else HELD
        if python.poll() is None:
            python.kill()
            python.wait()
            python_verdict = HELD
        else:
            stderr = python.stderr.read()
            python_verdict = GONE if python.returncode == 1 and f"rm -r {stryker.lock}" in stderr else stderr
        python.stdout.close()
        python.stderr.close()
        if waiter.holds():
            waiter.finish()
        else:
            waiter.process.terminate()
            waiter.process.wait(timeout=10)
        return {"bash": bash, "python": python_verdict}

    def test_each_lock_judges_the_record_of_the_other_as_the_other_does(self):
        cases = {"alive": HELD, "gone by pid": GONE, "gone by start": GONE, "incomplete": HELD, "no record": HELD}
        for writer in ["bash", "python"]:
            for case, expected in cases.items():
                with self.subTest(writer=writer, case=case):
                    self.assertEqual(self.verdicts(self.written_by(writer, case), expected),
                                     {"bash": expected, "python": expected})

    def test_both_locks_judge_a_holder_that_kill_cannot_signal_by_its_start_time(self):
        # kill -0 of process 1, which another user runs, fails with "Operation not permitted".
        for start, expected in [(start_time(1), HELD), (NEVER_STARTED, GONE)]:
            with self.subTest(start=start):
                text = f"pid=1\nstart={start}\ntoken=someone-else\nlabel=another user's process\n"
                self.assertEqual(self.verdicts(text, expected), {"bash": expected, "python": expected})

    def test_both_locks_read_each_edge_of_the_record_format_alike(self):
        # Each record is of a live holder or of a dead one, and a reader that splits or drops its lines unlike the
        # written rule (tools/README.md, "The record of a machine lock") judges it the other way.
        dead = subprocess.Popen(["true"])
        dead.wait()
        live, start = self.reused.pid, start_time(self.reused.pid)
        cases = {
            # Only a newline ends a line. Python's splitlines also splits at these.
            "a carriage return and other separators in the label":
                (f"pid={live}\nstart={start}\ntoken=T\nlabel=/a\rtoken=X\x1cpid={dead.pid}\x0bstart=S\n", HELD),
            # A last line without its newline is not part of the record, so the token is missing.
            "a last line without its newline": (f"pid={dead.pid}\nstart={NEVER_STARTED}\ntoken=T", HELD),
            # A line is split at its first =, and the value keeps a = at its end.
            "a value that ends with =": (f"pid={live}\nstart={start}=\ntoken=T\n", GONE),
            # A line without = is no line of the record.
            "a line without =": (f"pid={dead.pid}\nstart={NEVER_STARTED}\ntoken=T\npid\n", GONE),
            # Only the ASCII digits make a pid.
            "a pid of other digits": (f"pid=\u00b2\nstart={NEVER_STARTED}\ntoken=T\n", HELD),
            # bash cannot hold a NUL byte, so both readers drop it.
            "a NUL byte in the start": (f"pid={live}\nstart={start}\x00\ntoken=T\n", HELD),
            # A pid that no process can have: kill -0 refuses it, and ps gives nothing.
            "a pid too large for kill": (f"pid={'9' * 30}\nstart={NEVER_STARTED}\ntoken=T\n", HELD),
        }
        for case, (text, expected) in cases.items():
            with self.subTest(case=case):
                self.assertEqual(self.verdicts(text, expected), {"bash": expected, "python": expected})

    def test_both_locks_use_the_last_pid_start_and_token_in_the_record(self):
        dead = subprocess.Popen(["true"])
        dead.wait()
        live, start = self.reused.pid, start_time(self.reused.pid)
        cases = {
            "a live pid after a dead pid": (f"pid={dead.pid}\npid={live}\nstart={start}\ntoken=T\n", HELD),
            "a dead pid after a live pid": (f"pid={live}\npid={dead.pid}\nstart={start}\ntoken=T\n", GONE),
            "the real start after another start":
                (f"pid={live}\nstart={NEVER_STARTED}\nstart={start}\ntoken=T\n", HELD),
            "another start after the real start":
                (f"pid={live}\nstart={start}\nstart={NEVER_STARTED}\ntoken=T\n", GONE),
            "a token after an empty token": (f"pid={dead.pid}\nstart={start}\ntoken=\ntoken=T\n", GONE),
            "an empty token after a token": (f"pid={dead.pid}\nstart={start}\ntoken=T\ntoken=\n", HELD),
        }
        for case, (text, expected) in cases.items():
            with self.subTest(case=case):
                self.assertEqual(self.verdicts(text, expected), {"bash": expected, "python": expected})

    def test_both_locks_read_a_label_that_is_not_utf_8(self):
        # A command argument of the GPU lock can hold any bytes.
        text = f"pid={self.reused.pid}\nstart={start_time(self.reused.pid)}\ntoken=T\nlabel=\xff\xfe\n"
        self.assertEqual(self.verdicts(text.encode("latin-1"), HELD), {"bash": HELD, "python": HELD})


class TheOldHelper(unittest.TestCase):
    """The helper of 93e46dd7f, which some worktrees still run, and this helper exclude each other."""

    def test_older_helpers_wait_while_shared_runs_hold_the_gpu(self):
        for which in ["93e46dd7f", "8fe7ac7cf"]:
            with self.subTest(which):
                bench = Bench(self)
                helper = bench.old_helper() if which == OLD_HELPER_COMMIT else bench.modeless_helper()
                shared = [bench.start(f"shared-{i}", shared=True) for i in range(2)]
                for run in shared:
                    run.wait_until_it_holds()
                old = bench.start("old", helper=helper)
                time.sleep(SETTLE)
                self.assertFalse(old.holds())
                self.assertEqual(shared[0].finish(), 0)
                self.assertEqual(shared[1].finish(), 0)
                old.wait_until_it_holds()
                self.assertEqual(old.finish(), 0)
                self.assertEqual(bench.leftovers(), [])

    def test_a_shared_run_waits_while_an_older_helper_holds_the_gpu(self):
        for which in ["93e46dd7f", "8fe7ac7cf"]:
            with self.subTest(which):
                bench = Bench(self)
                helper = bench.old_helper() if which == OLD_HELPER_COMMIT else bench.modeless_helper()
                old = bench.start("old", helper=helper)
                old.wait_until_it_holds()
                shared = bench.start("shared", shared=True)
                time.sleep(SETTLE)
                self.assertFalse(shared.holds())
                self.assertEqual(old.finish(), 0)
                shared.wait_until_it_holds()
                self.assertEqual(shared.finish(), 0)
                self.assertEqual(bench.leftovers(), [])

    def test_a_shared_run_does_not_go_past_an_older_helpers_waiting_ticket(self):
        bench = Bench(self)
        first = bench.start("first", shared=True)
        first.wait_until_it_holds()
        old = bench.start("old", helper=bench.modeless_helper())
        self.assertTrue(wait_for(lambda: len(list(bench.queue.iterdir())) == 1))
        later = bench.start("later", shared=True)
        time.sleep(SETTLE)
        self.assertFalse(later.holds())
        self.assertEqual(first.finish(), 0)
        old.wait_until_it_holds()
        self.assertFalse(later.holds())
        self.assertEqual(old.finish(), 0)
        later.wait_until_it_holds()
        self.assertEqual(later.finish(), 0)

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
