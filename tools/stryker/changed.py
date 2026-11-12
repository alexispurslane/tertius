#!/usr/bin/env python3
"""Tool code. The official Stryker run: Stryker.NET on the C# lines that a branch changed in Core, Simulation and
Physics, with the official settings of tools/stryker/config.py. It prints the shared table of tools/mutation_table.py over
every module. Run it inside the dev shell, from anywhere in the repository:

    nix develop --command python3 -I tools/stryker/changed.py <base> <out-dir> [--head <commit>]

For each module with changed lines, it writes <out-dir>/<module>/stryker-config.json with config.py and runs Stryker on
it with tools/stryker/run.py, one module after another, under the machine-wide lock of run.py. run.py starts Stryker at
nice 20 with the xunit threads of config.test_threads, and builds the module's test projects again after the run. A
module's test projects are its own and every other test project whose references reach it, but never
tests/Tertius.Rendering.Tests or tests/Tertius.Host.Tests: their GPU tests are slow and depend on the load.

Every other changed code file goes to the agent's tools/mutate.py list, and the script lists it with its changed lines
and the places of its removed lines. A code file is C#, a shader stage or a shader include (CODE_SUFFIXES) under src or
analyzers. So the list holds Rendering (C# and GLSL), Host, Demo, the analyzers, and any module that MODULES does not
name.

The table has one row for each Stryker mutant in the changed lines, one no-mutant row for each changed line where every
Stryker mutant failed to compile, and one removed row for each place where the branch removed lines. The last two go to
the agent's tools/mutate.py list. Under the table, the script lists the changed lines that can hold behavior but have no
Stryker mutant, because Stryker has no mutator for them. The agent judges each one for the list. Then it prints the
evidence of each module's log: the compile tries of the one schemata build, the initial test run, the Dubious tests, the
tests that each mutant ran, and the load and test hosts in run.py's samples.

The run is not valid in four cases, and then the script says so, names the fix and exits with 1. More than
MAX_TIMEOUT_SHARE of the mutants that ran ended in a timeout. A mutant timed out while the 1-minute load passed
MAX_TIMEOUT_LOAD_PER_CORE times the cores. Stryker marked a test Dubious. A build replaced Stryker's mutated assembly
during the run (run.py). The exit code is also 1 if Stryker failed.

--modules runs only the named modules, for example one module per CPU worker of tools/stryker/cloud.py, which runs
this script in the cloud and prints the same table over the modules' results.json.

--head <commit> measures a merged range, for example a ticket's commits from its base: the files must be the same in the
working tree as at <commit>. --exclude leaves files out by name, for example one that changed since. --concurrency (1 to
4) and --additional-timeout are for a calibration only, and a run with them is not official. tools/stryker/compare.py
compares the per-mutant results of two runs.
"""

import argparse
import json
import os
import re
import sys
import time
from datetime import datetime
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))
import changed_lines  # noqa: E402
import config  # noqa: E402
import mutation_table  # noqa: E402
import run  # noqa: E402
import survivors  # noqa: E402
from processes import Stopped, stop_on_signals  # noqa: E402

# The modules of the official run: their tests run on the CPU only.
MODULES = ('Core', 'Simulation', 'Physics')
GPU_TEST_PROJECTS = {'Rendering', 'Host'}
# The files that a mutant can change: C#, and the shader stages and includes of src/Tertius.Rendering/Tertius.Rendering.csproj.
CODE_SUFFIXES = ('.cs', '.vert', '.frag', '.comp', '.glsl', '.hlsl')
# The largest share of the mutants that ran that can end in a timeout, from ter-hxca's calibration. Quiet runs gave 0 of
# 3 (Core) and 3 of 20 (Simulation): the same three mutants also timed out at a 1-minute load of 173, so they make a
# test hang, and a hang is a kill. ter-u63a's trial, which the load broke, gave 65 of 76.
MAX_TIMEOUT_SHARE = 0.25
# The highest 1-minute load per core at which a timeout counts as a kill. ter-2y6i's three hangs timed out at a maximum
# load of 30.5 and of 173.4 on this 10-core machine, the same mutants in both runs. Above that, the calibration has no
# evidence that a timeout is a hang.
MAX_TIMEOUT_LOAD_PER_CORE = 17.5
RAN = ('killed', 'timeout', 'survived', 'no-coverage')


def _module_of(rel):
    """Core for src/Tertius.Core/..., and None for a file outside the modules of the official run."""
    for module in MODULES:
        if rel.startswith(f'src/Tertius.{module}/') and rel.endswith('.cs'):
            return module
    return None


def hand_files(paths):
    """The changed code files, among paths, that go to the agent's tools/mutate.py list: every one outside the C# of
    MODULES."""
    return sorted(rel for rel in paths if _module_of(rel) is None)


def removed_rows(removed, modules, exclude):
    """One removed row for each place in removed (parse_diff's second map) where the branch removed lines in a file of
    modules, except the files named in exclude."""
    out = []
    for rel in sorted(removed):
        module = _module_of(rel)
        if module in modules and os.path.basename(rel) not in exclude:
            for line, count in removed[rel]:
                out.append({'id': survivors.place_id(module, rel, 'R', line), 'file': rel, 'line': line,
                            'change': f'the branch removed {count} lines here', 'result': 'removed', 'killed_by': [],
                            'seconds': None})
    return out


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
    and its count of tests), initial_seconds, captures (the starts of a per-test coverage capture, none with
    coverage-analysis off), coverage_seconds (the capture's time), dubious (the tests whose coverage capture failed, so
    every mutant runs them) and scopes (each mutant's count of tests, or 'all')."""
    found = {'compiles': 0, 'compile_failures': 0, 'initial_runs': 0, 'tests': 0, 'dubious': 0, 'scopes': {},
             'initial_seconds': None, 'captures': 0, 'coverage_seconds': None, 'elapsed': None}
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
            found['captures'] += 1
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


def problems(rows, evidence, load_max=None, cores=None, max_timeout_share=MAX_TIMEOUT_SHARE):
    """Why a module's run is not valid, each with its fix, from its rows, its log evidence, and the highest 1-minute
    load of its samples on a machine with cores cores. Empty for a valid run."""
    out = []
    ran = [r for r in rows if r['result'] in RAN]
    timeouts = sum(r['result'] == 'timeout' for r in ran)
    cores = cores or os.cpu_count()
    if timeouts and load_max is not None and load_max > MAX_TIMEOUT_LOAD_PER_CORE * cores:
        out.append(f'{timeouts} mutants ended in a timeout while the 1-minute load reached {load_max:.0f} on {cores} '
                   f'cores, above the {MAX_TIMEOUT_LOAD_PER_CORE * cores:.0f} at which the calibration saw the same '
                   'timeouts as on a quiet machine. A timeout under such a load can hide a survivor, so the run is not '
                   'valid. Run it again on a quieter machine.')
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


def _evidence_text(module, found, result):
    scopes = list(found['scopes'].values())
    counted = [s for s in scopes if s != 'all']
    mean = f'{sum(counted) / len(counted):.1f}' if counted else '-'
    initial = f'{found["initial_seconds"]:.0f} s' if found['initial_seconds'] is not None else 'no end mark'
    if found['coverage_seconds'] is not None:
        coverage = f'per-test coverage capture {found["coverage_seconds"]:.0f} s'
    elif found['captures']:
        coverage = 'per-test coverage capture no end mark'
    else:
        coverage = 'no per-test coverage capture'
    return (f'{module}: {result["seconds"]:.0f} s wall; schemata build {found["compiles"]} compile tries '
            f'({found["compile_failures"]} failed, each removing the mutants that broke it); '
            f'{found["initial_runs"]} initial test run of {found["tests"]} tests in {initial}; '
            f'{coverage}; {found["dubious"]} Dubious tests; '
            f'{len(scopes)} mutants ran a mean of {mean} of {found["tests"]} tests, '
            f'{scopes.count("all")} ran all tests; {result["watch"]}')


def _places(numbers):
    return ','.join(f'{a}-{b}' if a != b else str(a) for a, b in changed_lines.runs(numbers))


def print_hand_files(lines, removed):
    """Print the changed code outside the official run, for the agent's tools/mutate.py list."""
    hand = hand_files(lines.keys() | removed.keys())
    if hand:
        print('Changed code outside the official run, for the tools/mutate.py list: '
              + '; '.join(rel + (f':{_places(lines[rel])}' if rel in lines else '')
                          + (f' (removed lines at {", ".join(str(n) for n, _ in removed[rel])})' if rel in removed else '')
                          for rel in hand))


def planned(root, base, head, exclude, removed, modules=MODULES):
    """module -> its mutate patterns, for each module of modules with changed lines or removed lines."""
    plans = {}
    for module in modules:
        mutate_patterns = config.mutate_patterns(root, module, base, head, exclude, None)
        if mutate_patterns or removed_rows(removed, {module}, exclude):
            plans[module] = mutate_patterns
    return plans


def settings_text(concurrency, additional_timeout):
    official = (concurrency, additional_timeout) == (config.CONCURRENCY, config.ADDITIONAL_TIMEOUT_MS)
    return official, (f'concurrency {concurrency} at nice 20 with {config.test_threads(concurrency)} xunit threads '
                      f'per test host, timeout-ratio {config.TIMEOUT_RATIO}, additional-timeout '
                      f'{additional_timeout} ms, timeout share at most {MAX_TIMEOUT_SHARE:.0%}, timeouts count up to a '
                      f'1-minute load of {MAX_TIMEOUT_LOAD_PER_CORE} per core')


def print_report(rows, unmutated, notes, problems, settings, official, load, wall_seconds, out):
    """Print the table, the lines without a mutant, the evidence and the validity of a run, and write
    <out>/results.json. The exit code: 1 if the run is not valid."""
    mutation_table.print_table(rows)
    if unmutated:
        files = {}
        for rel, line in unmutated:
            files.setdefault(rel, []).append(line)
        print('Changed code lines without any Stryker mutant, to judge for the tools/mutate.py list: '
              + ', '.join(f'{rel}:{_places(numbers)}' for rel, numbers in files.items()))
    for note in notes:
        print(note)
    load_text = f'1-minute load mean {sum(load) / len(load):.1f}, max {max(load):.1f}' if load else 'no load samples'
    print(f'wall time {wall_seconds:.0f} s; {load_text}; {settings}'
          + ('' if official else ' (calibration settings: not an official run)'))
    Path(out).mkdir(parents=True, exist_ok=True)
    (Path(out) / 'results.json').write_text(json.dumps(
        {'rows': rows, 'unmutated': unmutated, 'evidence': notes, 'problems': problems, 'settings': settings,
         'official': official, 'load': load}, indent=1) + '\n')
    for p in problems:
        print(f'NOT VALID: {p}', file=sys.stderr)
    return 1 if problems else 0


def main():
    parser = argparse.ArgumentParser(description='The official Stryker run on the changed lines outside rendering.')
    parser.add_argument('base', help='the branch or commit that the changes start from, for example main')
    parser.add_argument('out', help='the folder for the configuration, the reports, the logs and results.json')
    parser.add_argument('--head', default='HEAD', help='the last commit of the changes (default HEAD)')
    parser.add_argument('--exclude', nargs='*', default=[], help='file names to leave out, for example Program.cs')
    parser.add_argument('--concurrency', type=int, default=config.CONCURRENCY, help='calibration only, 1 to 4')
    parser.add_argument('--additional-timeout', type=int, default=config.ADDITIONAL_TIMEOUT_MS, help='calibration only')
    parser.add_argument('--modules', nargs='+', choices=MODULES, default=list(MODULES),
                        help='run only these modules (default: all three)')
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
    lines, removed = changed_lines.changed_and_removed(root, args.base, ['src', 'analyzers'], args.head, CODE_SUFFIXES)
    print_hand_files(lines, removed)
    plans = planned(root, args.base, args.head, args.exclude, removed, args.modules)
    if not plans:
        print(f'No changed C# lines in {", ".join(args.modules)}.')
        return
    official, settings = settings_text(args.concurrency, args.additional_timeout)
    stop_on_signals()
    rows, notes, all_problems, unmutated, load = [], [], [], [], []
    started = time.monotonic()
    with run.Lock() as lock:
        for module, patterns in plans.items():
            if not patterns:
                continue
            folder = out / module
            tests = test_projects(root, module)
            config.write_config(folder, patterns, tests, args.concurrency, args.additional_timeout)
            _say(f'{module}: Stryker on {len(patterns)} spans with the tests of {", ".join(tests)}; log in '
                 f'{folder}/out/logs')
            result = run.run_module(root, module, folder, lock)
            load += result['load']
            all_problems += [f'{module}: {p}' for p in result['problems']]
            report = folder / 'out/reports/mutation-report.json'
            if result['code'] != 0 or not report.is_file():
                all_problems.append(f'{module}: Stryker failed with exit code {result["code"]}, see {folder}/run.out '
                                    f'and {folder}/out/logs')
                continue
            report = json.loads(report.read_text())
            spans = survivors.spans_of(json.loads((folder / 'stryker-config.json').read_text()))
            module_rows = survivors.rows(report, spans, module, root)
            unmutated += survivors.unmutated_lines(report, spans, root)
            log = '\n'.join(p.read_text(errors='replace') for p in sorted((folder / 'out/logs').glob('log-*.txt')))
            ids = {r['id'].split('-', 1)[1] for r in module_rows if r['result'] in RAN}
            found = evidence(log, ids)
            notes.append(_evidence_text(module, found, result))
            all_problems += [f'{module}: {p}' for p in problems(module_rows, found, max(result['load'], default=None))]
            rows += module_rows
    rows += removed_rows(removed, set(plans), args.exclude)
    if print_report(rows, unmutated, notes, all_problems, settings, official, load, time.monotonic() - started, out):
        sys.exit(1)


if __name__ == '__main__':
    try:
        main()
    except (KeyboardInterrupt, Stopped):
        # The lock and the Stryker process group are already gone: the with block and run_logged ended them.
        sys.exit('stopped by a signal')
