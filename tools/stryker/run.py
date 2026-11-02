#!/usr/bin/env python3
"""Tool code. Runs Stryker.NET on one source project with the configuration that tools/stryker/config.py wrote. It is
the only script that starts Stryker, so that every run takes the machine-wide lock, runs at the lowest priority and
bounds its xunit threads. tools/stryker/changed.py, the official run, calls run_module for each module. Run it by hand
inside the dev shell:

    nix develop --command python3 -I tools/stryker/run.py <module> <out-dir>

<out-dir> holds the stryker-config.json of config.py. Stryker writes its log to <out-dir>/out/logs (the -L flag, because
the progress bar shows only on a terminal) and its JSON report to <out-dir>/out/reports/mutation-report.json. Read the
report with tools/stryker/survivors.py. run.out holds Stryker's output, and processes.log holds the process samples.

The run works in this order:
1. It takes the lock /tmp/tertius-stryker.lock, a folder that os.mkdir makes for one process only, because two Stryker
   runs compete for the CPU and their tests time out. It waits while another run holds the lock.
2. It starts `dotnet tool run dotnet-stryker` in src/Tertius.<module> at nice 20, so that the test hosts inherit the
   lowest priority, with DOTNET_PROCESSOR_COUNT at config.test_threads of the configuration's concurrency. Stryker
   starts each test host as `dotnet <tests>.dll --server`, with no place for an xunit option such as --max-threads, so
   the bound goes through the environment that every host inherits.
3. Every 15 s, it samples the 1-minute load and the processes of Stryker's process group, with their priority and CPU.
4. With each sample, it makes sure that each test project's copy of the module assembly holds Stryker's helper class
   MutantControl while Stryker's mutants run. Stryker copies its mutated assembly over each copy before the coverage
   capture ("Injected the mutated assembly file into" in its log), and it restores the plain copy about 1 s before its
   "Time Elapsed" line. A copy without the class in two samples in a row of that time shows that a build of this
   worktree replaced the mutated assembly, so the later mutants ran the plain code and can show Survived. The run is
   then not valid.
5. It builds the test projects again in Checked. A Stryker run that ends early, for example on a signal, leaves its
   mutated assembly in their output, and a later `dotnet test --no-build` would run it.
6. It removes the lock, also on a failure, an exception, Ctrl-C, SIGTERM or SIGHUP. It never removes a lock that
   another process holds. If the process that holds the lock is gone, or the lock folder has had no owner file for
   STALE_SECONDS, it stops and prints the command that removes the lock.
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import threading
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))
import changed_lines  # noqa: E402
import config  # noqa: E402
from processes import Stopped, run_logged, stop_on_signals  # noqa: E402

LOCK_PATH = Path('/tmp/tertius-stryker.lock')
# A run writes its owner file right after its mkdir. A lock folder without one for longer is the rest of a run that died
# at its start, or of a mkdir by hand.
STALE_SECONDS = 60
# The helper class that Stryker compiles into every mutated assembly (Stryker.Core.InjectedHelpers.MutantControl.cs).
STRYKER_MARK = b'MutantControl'
# The log lines of Stryker.NET 5.0.0 that start and end the time in which the mutated assembly is in place.
INJECTED = 'Injected the mutated assembly file into'
ENDED = 'Time Elapsed'


def _say(text):
    print(text, file=sys.stderr, flush=True)


def _alive(pid):
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        pass
    return True


class Lock:
    """The machine-wide lock of Stryker runs: the folder at path, which os.mkdir makes for one process only. The folder
    holds the file owner: the pid, the folder and the start time of the run that holds it."""

    def __init__(self, path=LOCK_PATH, poll=30, say=_say):
        self.path, self.poll, self.say = Path(path), poll, say

    def _owner(self):
        try:
            return (self.path / 'owner').read_text().strip()
        except FileNotFoundError:
            return ''

    def _age(self):
        try:
            return time.time() - self.path.stat().st_mtime
        except FileNotFoundError:
            return 0

    def held(self):
        """True if this process holds the lock."""
        owner = self._owner()
        return bool(owner) and int(owner.split()[0]) == os.getpid()

    def acquire(self):
        said = None
        while True:
            try:
                os.mkdir(self.path)
                break
            except FileExistsError:
                pass
            owner = self._owner()
            if owner:
                pid = int(owner.split()[0])
                if not _alive(pid):
                    raise SystemExit(f'The Stryker lock {self.path} belongs to process {pid}, which is gone ({owner}). '
                                     f'If no Stryker runs on this machine, remove the lock: rm -r {self.path}')
            elif self._age() > STALE_SECONDS:
                raise SystemExit(f'The Stryker lock {self.path} has had no owner file for {self._age():.0f} s, so no '
                                 'run holds it: a run died at its start, or someone made the folder by hand. If no '
                                 f'Stryker runs on this machine, remove the lock: rm -r {self.path}')
            if said is None or time.monotonic() - said > 300:
                self.say(f'waiting for the Stryker lock {self.path}, held by {owner or "a run that is starting"}')
                said = time.monotonic()
            time.sleep(self.poll)
        try:
            (self.path / 'owner').write_text(f'{os.getpid()} {os.getcwd()} {time.strftime("%Y-%m-%dT%H:%M:%S")}\n')
        except BaseException:
            os.rmdir(self.path)
            raise

    def release(self):
        """Removes the lock if this process holds it, and leaves it otherwise."""
        if not self.held():
            return
        (self.path / 'owner').unlink()
        os.rmdir(self.path)

    def __enter__(self):
        self.acquire()
        return self

    def __exit__(self, *exception):
        self.release()


def stryker_command(folder):
    """The command that runs Stryker with the configuration in folder: at nice 20, and with DOTNET_PROCESSOR_COUNT at
    config.test_threads of its concurrency, so that the xunit threads of all test hosts together stay within
    config.MAX_TEST_THREADS."""
    concurrency = json.loads((folder / 'stryker-config.json').read_text())['stryker-config']['concurrency']
    return ['nice', '-n', '20', 'env', f'DOTNET_PROCESSOR_COUNT={config.test_threads(concurrency)}',
            'dotnet', 'tool', 'run', 'dotnet-stryker', '--',
            '-f', str(folder / 'stryker-config.json'), '-O', str(folder / 'out'), '-L']


def group_processes(ps_text, group):
    """The processes of the process group in the output of `ps -A -o pid=,pgid=,ni=,pcpu=,command=`, each with its pid,
    nice value, CPU share in percent of one core, and command."""
    out = []
    for line in ps_text.splitlines():
        fields = line.split(None, 4)
        if len(fields) == 5 and fields[1] == str(group):
            out.append({'pid': int(fields[0]), 'nice': int(fields[2]), 'cpu': float(fields[3]), 'command': fields[4]})
    return out


def mutated_phase(log_text):
    """True if the Stryker log text shows that its mutated assembly is in place: after the injection, before the end."""
    return INJECTED in log_text and ENDED not in log_text


def plain_assemblies(paths):
    """The assemblies among paths without Stryker's helper class."""
    return [p for p in paths if STRYKER_MARK not in Path(p).read_bytes()]


def replaced_copies(samples):
    """The copies that a build replaced during a run, from samples: one (in the mutated phase, the set of plain copies)
    per sample. A copy counts when it is plain in two samples in a row of the mutated phase. One plain sample can be
    Stryker's own restore, about 1 s before its end."""
    out, before = set(), set()
    for in_phase, plain in samples:
        now = set(plain) if in_phase else set()
        out |= before & now
        before = now
    return sorted(out)


class Watch:
    """Samples the 1-minute load and the processes of one process group every interval seconds, from start(group) until
    the function that start returns runs. Appends each process sample to log_path. With each sample, it also reads the
    new lines of the Stryker logs in logs and, in the mutated phase, finds the plain ones among the paths that copies()
    returns."""

    def __init__(self, log_path, logs, copies, interval=15):
        self.log_path, self.logs, self.copies, self.interval = log_path, logs, copies, interval
        self.loads, self.hosts, self.copy_samples = [], [], []
        self.marks, self.read = '', {}
        self.stopping = threading.Event()

    def start(self, group):
        self.group = group
        thread = threading.Thread(target=self._sample, daemon=True)
        thread.start()

        def stop():
            self.stopping.set()
            thread.join()
        return stop

    def _sample(self):
        with open(self.log_path, 'a') as log:
            while True:
                self.loads.append(os.getloadavg()[0])
                ps = subprocess.run(['ps', '-A', '-ww', '-o', 'pid=,pgid=,ni=,pcpu=,command='],
                                    capture_output=True, text=True).stdout
                found = group_processes(ps, self.group)
                stamp = time.strftime('%H:%M:%S')
                for p in found:
                    log.write(f'{stamp} load {self.loads[-1]:.1f} pid {p["pid"]} nice {p["nice"]} cpu {p["cpu"]:.0f}% '
                              f'{p["command"][:160]}\n')
                log.flush()
                # The MTP test hosts of Stryker run as `dotnet <tests>.dll --server ...`.
                self.hosts.append([p for p in found if ' --server' in p['command']])
                in_phase = mutated_phase(self._new_marks())
                try:
                    plain = {str(p) for p in plain_assemblies(self.copies())} if in_phase else set()
                except OSError:
                    # A build or Stryker's restore can remove a copy for a moment. The next sample reads it again.
                    plain = set()
                self.copy_samples.append((in_phase, plain))
                if self.stopping.wait(self.interval):
                    return

    def _new_marks(self):
        """The INJECTED and ENDED lines of the Stryker logs so far. Reads only the bytes that are new since the last
        sample."""
        for path in sorted(self.logs.glob('log-*.txt')):
            with open(path, 'rb') as f:
                f.seek(self.read.get(path, 0))
                data = f.read()
            # Read only up to the last complete line, so that a half-written line comes again whole.
            end = data.rfind(b'\n') + 1
            self.read[path] = self.read.get(path, 0) + end
            self.marks += ''.join(line + '\n' for line in data[:end].decode(errors='replace').splitlines()
                                  if INJECTED in line or ENDED in line)
        return self.marks

    def text(self):
        """The load and the test hosts of the samples in one line."""
        load = (f'1-minute load mean {sum(self.loads) / len(self.loads):.1f}, max {max(self.loads):.1f}'
                if self.loads else 'no load samples')
        return f'{load}; {hosts_text(self.hosts)}'


def hosts_text(samples):
    """The test hosts of samples (a list of samples, each a list of group_processes entries) in one line. Stryker starts
    one host per test project at once but runs one at a time, so the CPU of all hosts of one sample together shows the
    thread bound, not their count."""
    hosts = [h for sample in samples for h in sample]
    if not hosts:
        return 'no test host in the samples'
    nice = ', '.join(str(n) for n in sorted({h['nice'] for h in hosts}))
    totals = [sum(h['cpu'] for h in sample) for sample in samples if sample]
    return (f'{max(len(sample) for sample in samples)} test host processes at most, at nice {nice}, together at most '
            f'{max(totals):.0f}% and on mean {sum(totals) / len(totals):.0f}% of one core')


def _test_projects(root, module, folder):
    """The test projects of the configuration in folder, as paths relative to root."""
    project = root / 'src' / f'Tertius.{module}'
    tests = json.loads((folder / 'stryker-config.json').read_text())['stryker-config']['test-projects']
    return [(project / t).resolve().relative_to(root.resolve()) for t in tests]


def _rebuild(root, tests, folder, say):
    """Builds each test project in Checked after a Stryker run. A plain incremental build copies the module's real
    assembly back over Stryker's mutated one."""
    for csproj in tests:
        log = folder / f'rebuild-{csproj.parent.name}.build.log'
        if run_logged(['dotnet', 'build', str(csproj), '-c', config.CONFIGURATION], root, log)[0] != 0:
            say(f'WARNING: the rebuild of {csproj} after the run failed, see {log}')


def run_module(root, module, folder, lock, say=_say):
    """Runs Stryker on src/Tertius.<module> with the configuration in folder, under lock, which this process must hold.
    Returns a dict: code (Stryker's exit code), seconds, load (the 1-minute load samples), watch (Watch.text) and
    problems (why the run is not valid, each with its fix)."""
    if not lock.held():
        raise RuntimeError(f'Stryker runs only under the lock {lock.path}, held by this process')
    if not os.environ.get('TERTIUS_VULKAN_DIR'):
        sys.exit('TERTIUS_VULKAN_DIR is empty. Run the script inside the dev shell: nix develop --command python3 -I '
                 f'{sys.argv[0]} ...')
    root, folder = Path(root), Path(folder)
    tests = _test_projects(root, module, folder)
    shutil.rmtree(folder / 'out', ignore_errors=True)

    def copies():
        return [p for t in tests for p in (root / t.parent / 'bin' / config.CONFIGURATION).glob(f'*/Tertius.{module}.dll')]

    watch = Watch(folder / 'processes.log', folder / 'out' / 'logs', copies)
    try:
        code, _, _, seconds = run_logged(stryker_command(folder), root / 'src' / f'Tertius.{module}', folder / 'run.out',
                                         started=watch.start)
    finally:
        _rebuild(root, tests, folder, say)
    problems = [f'{Path(p).relative_to(root)} lost Stryker\'s mutated assembly while the mutants ran: a build of this '
                'worktree replaced it, so the mutants after it ran the plain code and can show Survived. Build or test '
                'no Checked test project of this worktree during an official run, and run it again.'
                for p in replaced_copies(watch.copy_samples)]
    return {'code': code, 'seconds': seconds, 'load': watch.loads, 'watch': watch.text(), 'problems': problems}


def main():
    parser = argparse.ArgumentParser(description='Runs Stryker.NET on one module under the machine-wide lock.')
    parser.add_argument('module', help='the module name, for example Core for src/Tertius.Core')
    parser.add_argument('out', help='the folder with the stryker-config.json of tools/stryker/config.py')
    args = parser.parse_args()
    stop_on_signals()
    root = Path(changed_lines.repo_root())
    try:
        with Lock() as lock:
            result = run_module(root, args.module, Path(args.out).resolve(), lock)
    except (KeyboardInterrupt, Stopped):
        # The lock and the Stryker process group are already gone: the with block and run_logged ended them.
        sys.exit('stopped by a signal')
    print(f'Stryker exit code {result["code"]} after {result["seconds"]:.0f} s; {result["watch"]}')
    for p in result['problems']:
        print(f'NOT VALID: {p}', file=sys.stderr)
    sys.exit(result['code'] or (1 if result['problems'] else 0))


if __name__ == '__main__':
    main()
