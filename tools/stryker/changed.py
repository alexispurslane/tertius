#!/usr/bin/env python3
"""Tool code. The official Stryker run: Stryker.NET on the C# lines that a branch changed in Core, Simulation and
Physics, with the official settings of tools/stryker/config.py, under a machine-wide lock. It prints the shared table of
tools/mutation_table.py over every module. Run it inside the dev shell, from anywhere in the repository:

    nix develop --command python3 -I tools/stryker/changed.py <base> <out-dir> [--head <commit>]

For each module with changed lines, it writes <out-dir>/<module>/stryker-config.json with config.py and runs
tools/stryker/run.sh on it, one module after another, at nice 20 and with the xunit threads of config.test_threads
(stryker_command). After each module's run, it builds the module's test projects again, because Stryker leaves its
mutated assembly in their output. A module's test projects are its own and every other test project whose references
reach it, but never tests/Tertius.Rendering.Tests or tests/Tertius.Host.Tests: their GPU tests are slow and depend on
the load, so their lines go to tools/mutate.py. So do the changed lines of Rendering, Host and Demo, which the script
lists.

The table has one row for each Stryker mutant in the changed lines, one no-mutant row for each changed line where every
Stryker mutant failed to compile, and one removed row for each place where the branch removed lines. The last two go to
the agent's tools/mutate.py list. Under the table, the script lists the changed lines that can hold behavior but have no
Stryker mutant at all, because Stryker has no mutator for them; the agent judges each one for the list. Then it prints
the evidence of each module's log: the compile tries of the one schemata build, the initial test run, the Dubious
tests, and the tests that each mutant ran.

The run is not valid if more than MAX_TIMEOUT_SHARE of the mutants that ran ended in a timeout, or if Stryker marked a
test Dubious. Then the script says so and names the fix, and its exit code is 1. It is also 1 if Stryker failed.

The lock is the folder /tmp/tertius-stryker.lock. The script makes it before the first Stryker run, waits while another
run holds it, and removes it after the last run, also on a failure, an exception, Ctrl-C, SIGTERM or SIGHUP. It never
removes a lock that another process holds. If the process that holds it is gone, the script stops and prints the command
that removes the lock.

--head <commit> measures a merged range, for example a ticket's commits from its base: the files must be the same in the
working tree as at <commit>. --exclude leaves files out by name, for example one that changed since. --concurrency (1 to
4) and --additional-timeout are for a calibration only; a run with them is not official. tools/stryker/compare.py
compares the per-mutant results of two runs.
"""

import argparse
import json
import os
import re
import subprocess
import sys
import threading
import time
from datetime import datetime
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))
import changed_lines  # noqa: E402
import config  # noqa: E402
import mutate  # noqa: E402
import mutation_table  # noqa: E402
import survivors  # noqa: E402

# The modules of the official run: their tests run on the CPU only.
MODULES = ('Core', 'Simulation', 'Physics')
# The modules whose changed lines go to the agent's tools/mutate.py list.
HAND_LIST_MODULES = ('Rendering', 'Host', 'Demo')
GPU_TEST_PROJECTS = {'Rendering', 'Host'}
# The largest share of the mutants that ran that can end in a timeout, from ter-hxca's calibration. Quiet runs gave 0 of
# 3 (Core) and 3 of 20 (Simulation): the same three mutants also timed out at a 1-minute load of 173, so they make a
# test hang, and a hang is a kill. ter-u63a's trial, which the load broke, gave 65 of 76.
MAX_TIMEOUT_SHARE = 0.25
LOCK_PATH = Path('/tmp/tertius-stryker.lock')
RAN = ('killed', 'timeout', 'survived', 'no-coverage')


def _say(text):
    print(text, file=sys.stderr, flush=True)


def test_projects(root, module):
    """The names of the test projects, such as Core for tests/Tertius.Core.Tests, whose project references reach
    src/Tertius.<module> directly or through other projects, without the GPU test projects."""
    root = Path(root)
    target = (root / f'src/Tertius.{module}/Tertius.{module}.csproj').resolve()
    references = {}

    def refs(csproj):
        if csproj not in references:
            text = csproj.read_text() if csproj.is_file() else ''
            references[csproj] = [(csproj.parent / r.replace('\\', '/')).resolve()
                                  for r in re.findall(r'<ProjectReference\s+Include="([^"]+)"', text)]
        return references[csproj]

    def reaches(csproj, seen):
        if csproj == target:
            return True
        seen.add(csproj)
        return any(reaches(r, seen) for r in refs(csproj) if r not in seen)

    names = []
    for csproj in sorted(root.glob('tests/Tertius.*.Tests/Tertius.*.Tests.csproj')):
        name = csproj.stem[len('Tertius.'):-len('.Tests')]
        if name not in GPU_TEST_PROJECTS and reaches(csproj.resolve(), set()):
            names.append(name)
    return names


_LINE = re.compile(r'^(\S+)\s+\[(\w+)\] (.*)$', re.M)


def _time(stamp):
    # Stryker writes seven digits of a second, and datetime reads at most six.
    return datetime.fromisoformat(re.sub(r'(\.\d{6})\d+', r'\1', stamp))


def evidence(log, ids):
    """What a Stryker log shows about the speed of its tricks, for the mutants whose Stryker ids are in ids:
    compiles and compile_failures (the tries of the one schemata build), initial_runs and tests (the initial test run
    and its count of tests), initial_seconds, coverage_seconds (the per-test coverage capture), dubious (the tests whose
    coverage capture failed, so every mutant runs them) and scopes (each mutant's count of tests, or 'all')."""
    found = {'compiles': 0, 'compile_failures': 0, 'initial_runs': 0, 'tests': 0, 'dubious': 0, 'scopes': {},
             'initial_seconds': None, 'coverage_seconds': None, 'elapsed': None}
    marks = {}
    for stamp, level, text in _LINE.findall(log):
        if text.startswith('Trying compilation for the'):
            found['compiles'] += 1
        elif text.startswith('Compilation failed'):
            found['compile_failures'] += 1
        elif 'Initial test run started' in text:
            found['initial_runs'] += 1
            found['tests'] += int(re.search(r'Number of tests found: (\d+)', text).group(1))
            marks.setdefault('initial', _time(stamp))
        elif text.startswith('Initial test run output'):
            marks.setdefault('initial_end', _time(stamp))
        elif text.startswith('Starting per-test coverage capture'):
            marks.setdefault('coverage', _time(stamp))
        elif text.startswith('Per-test coverage capture complete'):
            marks['coverage_end'] = _time(stamp)
        elif 'marking as Dubious' in text:
            found['dubious'] += 1
        elif m := re.match(r'Mutant (\d+) will be tested against \((\w+)\) tests', text):
            if m.group(1) in ids:
                found['scopes'][m.group(1)] = 'all' if m.group(2) == 'all' else int(m.group(2))
        elif m := re.match(r'Time Elapsed (\S+)', text):
            found['elapsed'] = m.group(1)
    for key, start, end in (('initial_seconds', 'initial', 'initial_end'), ('coverage_seconds', 'coverage', 'coverage_end')):
        if start in marks and end in marks:
            found[key] = (marks[end] - marks[start]).total_seconds()
    return found


def stryker_command(module, folder, concurrency):
    """The command that runs tools/stryker/run.sh on module with the configuration in folder. It starts at nice 20, so
    that the test hosts inherit the lowest priority, and with DOTNET_PROCESSOR_COUNT at config.test_threads, so that
    the xunit threads of all hosts together stay within config.MAX_TEST_THREADS. Stryker starts each host as
    `dotnet <tests>.dll --server`, with no place for an xunit option such as --max-threads, so the bound goes through
    the environment that every host inherits."""
    return ['nice', '-n', '20', 'env', f'DOTNET_PROCESSOR_COUNT={config.test_threads(concurrency)}',
            'bash', str(HERE / 'run.sh'), module, str(folder)]


def problems(rows, evidence, max_timeout_share=MAX_TIMEOUT_SHARE):
    """Why a module's run is not valid, each with its fix, from its rows and its log evidence. Empty for a valid run."""
    out = []
    ran = [r for r in rows if r['result'] in RAN]
    timeouts = sum(r['result'] == 'timeout' for r in ran)
    if ran and timeouts / len(ran) > max_timeout_share:
        out.append(f'{timeouts} of {len(ran)} mutants ended in a timeout, above the official share of '
                   f'{max_timeout_share:.0%}, so the run is not valid: a timeout under load hides a survivor. Run it '
                   'again on a quieter machine. If tools/stryker/compare.py then shows the same timeouts, the mutants '
                   'make a test hang: calibrate a larger share in tools/stryker/changed.py.')
    if evidence['dubious']:
        out.append(f'Stryker marked {evidence["dubious"]} tests Dubious: their per-test coverage capture timed out, so '
                   'every mutant also ran them and the run is not scoped to the covering tests. Run it again on a '
                   'quieter machine.')
    return out


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

    def acquire(self):
        said = None
        while True:
            try:
                os.mkdir(self.path)
                break
            except FileExistsError:
                pass
            # An empty owner is a run between its mkdir and its write of the owner file.
            owner = self._owner()
            pid = int(owner.split()[0]) if owner else None
            if pid is not None and not _alive(pid):
                raise SystemExit(f'The Stryker lock {self.path} belongs to process {pid}, which is gone ({owner}). '
                                 f'If no Stryker runs on this machine, remove the lock: rm -r {self.path}')
            if said is None or time.monotonic() - said > 300:
                self.say(f'waiting for the Stryker lock {self.path}, held by {owner or "a run that is starting"}')
                said = time.monotonic()
            time.sleep(self.poll)
        (self.path / 'owner').write_text(f'{os.getpid()} {os.getcwd()} {time.strftime("%Y-%m-%dT%H:%M:%S")}\n')

    def release(self):
        """Removes the lock if this process holds it, and leaves it otherwise."""
        owner = self._owner()
        if not owner or int(owner.split()[0]) != os.getpid():
            return
        (self.path / 'owner').unlink()
        os.rmdir(self.path)

    def __enter__(self):
        self.acquire()
        return self

    def __exit__(self, *exception):
        self.release()


class LoadSampler:
    """Samples the 1-minute load average every interval seconds while a run goes on."""

    def __init__(self, interval=15):
        self.interval, self.samples, self.stop = interval, [], threading.Event()
        self.thread = threading.Thread(target=self._sample, daemon=True)

    def _sample(self):
        while not self.stop.is_set():
            self.samples.append(os.getloadavg()[0])
            self.stop.wait(self.interval)

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *exception):
        self.stop.set()
        self.thread.join()

    def text(self):
        if not self.samples:
            return 'no load samples'
        return f'1-minute load mean {sum(self.samples) / len(self.samples):.1f}, max {max(self.samples):.1f}'


def _rebuild(root, tests, folder):
    """Builds each test project in Checked after a Stryker run. Stryker leaves its mutated assembly of the module in
    their output, and a later `dotnet test --no-build` would run it. A plain incremental build copies the real one
    back."""
    for name in tests:
        csproj = f'tests/Tertius.{name}.Tests/Tertius.{name}.Tests.csproj'
        if mutate.run_logged(['dotnet', 'build', csproj, '-c', config.CONFIGURATION], root,
                             folder / f'rebuild-{name}.build.log')[0] != 0:
            _say(f'WARNING: the rebuild of {csproj} after the run failed, see {folder}/rebuild-{name}.build.log')


def _evidence_text(module, found, rows, seconds):
    scopes = list(found['scopes'].values())
    counted = [s for s in scopes if s != 'all']
    mean = f'{sum(counted) / len(counted):.1f}' if counted else '-'
    initial = f'{found["initial_seconds"]:.0f} s' if found['initial_seconds'] is not None else 'no end mark'
    coverage = f'{found["coverage_seconds"]:.0f} s' if found['coverage_seconds'] is not None else 'no end mark'
    return (f'{module}: {seconds:.0f} s wall; schemata build {found["compiles"]} compile tries '
            f'({found["compile_failures"]} failed, each removing the mutants that broke it); '
            f'{found["initial_runs"]} initial test run of {found["tests"]} tests in {initial}; '
            f'per-test coverage capture {coverage}; {found["dubious"]} Dubious tests; '
            f'{len(scopes)} mutants ran a mean of {mean} of {found["tests"]} tests, '
            f'{scopes.count("all")} ran all tests')


def main():
    parser = argparse.ArgumentParser(description='The official Stryker run on the changed lines outside rendering.')
    parser.add_argument('base', help='the branch or commit that the changes start from, for example main')
    parser.add_argument('out', help='the folder for the configuration, the reports, the logs and results.json')
    parser.add_argument('--head', default='HEAD', help='the last commit of the changes (default HEAD)')
    parser.add_argument('--exclude', nargs='*', default=[], help='file names to leave out, for example Program.cs')
    parser.add_argument('--concurrency', type=int, default=config.CONCURRENCY, help='calibration only, 1 to 4')
    parser.add_argument('--additional-timeout', type=int, default=config.ADDITIONAL_TIMEOUT_MS, help='calibration only')
    args = parser.parse_args()
    try:
        config.test_threads(args.concurrency)
    except ValueError as e:
        parser.error(str(e))
    if not os.environ.get('TERTIUS_VULKAN_DIR'):
        sys.exit('TERTIUS_VULKAN_DIR is empty. Run the script inside the dev shell: nix develop --command python3 -I '
                 'tools/stryker/changed.py ...')
    root = changed_lines.repo_root()
    out = Path(args.out).resolve()
    lines, removed = changed_lines.changed_and_removed(root, args.base, ['src'], args.head)

    def module_of(rel):
        return rel.split('/')[1][len('Tertius.'):]

    hand = sorted(rel for rel in lines if module_of(rel) in HAND_LIST_MODULES)
    if hand:
        print(f'Changed lines for the tools/mutate.py list ({", ".join(HAND_LIST_MODULES)} are outside the run): '
              + ', '.join(f'{rel}:{",".join(f"{a}-{b}" if a != b else str(a) for a, b in changed_lines.runs(lines[rel]))}'
                          for rel in hand))
    plans = {}
    for module in MODULES:
        mutate_patterns = config.mutate_patterns(root, module, args.base, args.head, args.exclude, None)
        if mutate_patterns or any(module_of(rel) == module for rel in removed if os.path.basename(rel) not in args.exclude):
            plans[module] = mutate_patterns
    if not plans:
        print(f'No changed C# lines in {", ".join(MODULES)}.')
        return
    official = (args.concurrency, args.additional_timeout) == (config.CONCURRENCY, config.ADDITIONAL_TIMEOUT_MS)
    mutate.stop_on_signals()
    rows, notes, all_problems, unmutated = [], [], [], []
    started = time.monotonic()
    with Lock(), LoadSampler() as load:
        for module, patterns in plans.items():
            if not patterns:
                continue
            folder = out / module
            tests = test_projects(root, module)
            config.write_config(folder, patterns, tests, args.concurrency, args.additional_timeout)
            _say(f'{module}: Stryker on {len(patterns)} spans with the tests of {", ".join(tests)}; log in '
                 f'{folder}/out/logs')
            folder.mkdir(parents=True, exist_ok=True)
            try:
                code, _, _, seconds = mutate.run_logged(stryker_command(module, folder, args.concurrency), root,
                                                        folder / 'run.out')
            finally:
                _rebuild(root, tests, folder)
            report = folder / 'out/reports/mutation-report.json'
            if code != 0 or not report.is_file():
                all_problems.append(f'{module}: Stryker failed with exit code {code}, see {folder}/run.out and '
                                    f'{folder}/out/logs')
                continue
            report = json.loads(report.read_text())
            spans = survivors.spans_of(json.loads((folder / 'stryker-config.json').read_text()))
            module_rows = survivors.rows(report, spans, module, root)
            unmutated += survivors.unmutated_lines(report, spans, root)
            log = '\n'.join(p.read_text(errors='replace') for p in sorted((folder / 'out/logs').glob('log-*.txt')))
            ids = {r['id'].split('-', 1)[1] for r in module_rows if r['result'] in RAN}
            found = evidence(log, ids)
            notes.append(_evidence_text(module, found, module_rows, seconds))
            all_problems += [f'{module}: {p}' for p in problems(module_rows, found)]
            rows += module_rows
    for rel in sorted(removed):
        module = module_of(rel)
        if module in plans and os.path.basename(rel) not in args.exclude:
            for n, (line, count) in enumerate(removed[rel], 1):
                rows.append({'id': f'{module}-R{line}', 'file': rel, 'line': line,
                             'change': f'the branch removed {count} lines here', 'result': 'removed',
                             'killed_by': [], 'seconds': None})
    mutation_table.print_table(rows)
    if unmutated:
        files = {}
        for rel, line in unmutated:
            files.setdefault(rel, []).append(line)
        print('Changed code lines without any Stryker mutant, to judge for the tools/mutate.py list: '
              + ', '.join(f'{rel}:{",".join(f"{a}-{b}" if a != b else str(a) for a, b in changed_lines.runs(lines))}'
                          for rel, lines in files.items()))
    for note in notes:
        print(note)
    settings = (f'concurrency {args.concurrency} at nice 20 with {config.test_threads(args.concurrency)} xunit threads '
                f'per test host, timeout-ratio {config.TIMEOUT_RATIO}, additional-timeout '
                f'{args.additional_timeout} ms, timeout share at most {MAX_TIMEOUT_SHARE:.0%}')
    print(f'wall time {time.monotonic() - started:.0f} s; {load.text()}; {settings}'
          + ('' if official else ' (calibration settings: not an official run)'))
    (out / 'results.json').write_text(json.dumps(
        {'rows': rows, 'unmutated': unmutated, 'evidence': notes, 'problems': all_problems, 'settings': settings,
         'official': official, 'load': load.samples}, indent=1) + '\n')
    for p in all_problems:
        print(f'NOT VALID: {p}', file=sys.stderr)
    if all_problems:
        sys.exit(1)


if __name__ == '__main__':
    try:
        main()
    except (KeyboardInterrupt, mutate.Stopped):
        # The lock and the Stryker process group are already gone: the with block and run_logged ended them.
        sys.exit('stopped by a signal')
