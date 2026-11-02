#!/usr/bin/env python3
"""Tool code. Runs a list of hand-chosen mutants, C# and GLSL alike, one at a time, and prints the shared table of
tools/mutation_table.py. Run it inside the dev shell, from anywhere in the repository:

    nix develop --command python3 -I tools/mutate.py <list.json> <out-dir> [--only C01,S03] [--keep-going]

The list is a JSON array. Each mutant has these fields:
    id           a unique name, for example C01 or S03
    file         the mutated file, relative to the repository root
    line         the 1-based line where the original text starts
    original     the exact text to replace; it must start once on that line, and it can span lines
    replacement  the text that takes its place; an empty text deletes the original
    project      the test project folder, for example tests/Tertius.Rendering.Tests
    class        the test class that covers the mutant, or a list of them: a bare name matches that class in any
                 namespace, and a qualified name or a pattern with * goes to --filter-class unchanged
    note         optional: the change in a few words, for the table

Before the first change, the script checks the whole list and refuses to start on any error: every file exists, git
tracks it and it has no uncommitted change, the original starts on its line, and the ids are unique. Then it builds and
runs each distinct pair of project and classes once without a mutant. A baseline that fails or runs zero tests stops the
run, because a failing baseline makes every mutant look killed.

For each mutant: apply it, build only its test project in Checked (a plain incremental build), check that the build
wrote a new assembly of the mutated file's project into the test project's output (so a GLSL change reached the
embedded SPIR-V), run only its classes in Checked with --no-build under a timeout of 120 s, then restore the exact
original bytes and touch the file. The restore also runs on a failure, an exception, Ctrl-C, SIGTERM and SIGHUP. After
the last mutant, the script builds each test project once more, so that its output matches the source again, and
checks that git shows every mutated file unchanged.

Results: killed (a test failed, or the test host crashed), timeout (counts as killed, listed apart), survived,
build-failed (the mutant does not compile, so it is not a valid mutant) and no-tests (the filter ran zero tests: an
error in the list, so the run stops unless --keep-going). The output folder gets results.json and, for each mutant,
<id>.build.log and <id>.test.log. The exit code is 0 when every mutant ran, 1 when the run stopped early or a file is
not restored, and 2 for an error in the list.
"""

import argparse
import contextlib
import json
import os
import re
import signal
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mutation_table  # noqa: E402

CONFIGURATION = 'Checked'
TEST_TIMEOUT = 120  # seconds; CLAUDE.md, Testing
REQUIRED = {'id': str, 'file': str, 'line': int, 'original': str, 'replacement': str, 'project': str}
OPTIONAL = {'note': str}


class Stopped(BaseException):
    """A SIGTERM or SIGHUP arrived. A BaseException, so that no `except Exception` swallows it on its way out."""


def stop_on_signals():
    """Turns SIGTERM and SIGHUP into Stopped, so that every `finally` and context manager restores its file."""
    def handler(signum, frame):
        raise Stopped(signum)
    signal.signal(signal.SIGTERM, handler)
    signal.signal(signal.SIGHUP, handler)


# Reading and checking the list.

def parse_list(text):
    """The mutants of the JSON text of a list, and a list of errors. Each mutant has the fields of the list, with class
    as `classes`, always a list. On any error, the caller must not start."""
    try:
        entries = json.loads(text)
    except json.JSONDecodeError as e:
        return [], [f'the list is not valid JSON: {e}']
    if not isinstance(entries, list):
        return [], ['the list must be a JSON array of mutants']
    mutants, errors, seen = [], [], set()
    for n, entry in enumerate(entries, 1):
        name = entry.get('id', f'entry {n}') if isinstance(entry, dict) else f'entry {n}'
        problems = _entry_problems(entry)
        if problems:
            errors.extend(f'{name}: {p}' for p in problems)
            continue
        if entry['id'] in seen:
            errors.append(f'{name}: the id occurs twice')
        seen.add(entry['id'])
        classes = entry['class'] if isinstance(entry['class'], list) else [entry['class']]
        mutant = {k: entry[k] for k in REQUIRED}
        mutant.update(classes=classes, note=entry.get('note', ''))
        mutants.append(mutant)
    return mutants, errors


def _entry_problems(entry):
    if not isinstance(entry, dict):
        return ['a mutant must be a JSON object']
    problems = [f'unknown field "{k}"' for k in entry if k not in REQUIRED and k not in OPTIONAL and k != 'class']
    for field, kind in REQUIRED.items():
        value = entry.get(field)
        # bool is an int in Python, so a line of true would pass the isinstance check.
        if value is None or not isinstance(value, kind) or isinstance(value, bool):
            problems.append(f'the field "{field}" is missing or not a {kind.__name__}')
    for field, kind in OPTIONAL.items():
        if field in entry and not isinstance(entry[field], kind):
            problems.append(f'the field "{field}" is not a {kind.__name__}')
    classes = entry.get('class')
    if isinstance(classes, str):
        classes = [classes]
    if not isinstance(classes, list) or not classes or not all(isinstance(c, str) and c for c in classes):
        problems.append('the field "class" must be a test class name or a non-empty list of them')
    if problems:
        return problems
    if entry['line'] < 1:
        problems.append('the line must be 1 or more')
    if not entry['original']:
        problems.append('the original text is empty')
    if entry['original'] == entry['replacement']:
        problems.append('the replacement equals the original, so the mutant changes nothing')
    return problems


def _starts_on_line(data, line, original):
    """The byte offsets in data at which the UTF-8 bytes of original start on the 1-based line."""
    start = 0
    for _ in range(line - 1):
        start = data.find(b'\n', start) + 1
        if start == 0:
            return []
    end = data.find(b'\n', start)
    end = len(data) if end < 0 else end + 1
    needle, offsets = original.encode('utf-8'), []
    at = data.find(needle, start)
    while 0 <= at < end:
        offsets.append(at)
        at = data.find(needle, at + 1)
    return offsets


def locate(data, line, original):
    """The byte offset in data where original starts on its line, or None if it does not start there exactly once."""
    offsets = _starts_on_line(data, line, original)
    return offsets[0] if len(offsets) == 1 else None


def validate(mutants, root, tracked, dirty):
    """The errors of the mutants against the repository at root, in which git tracks the relative paths in tracked and
    shows uncommitted changes, also untracked files, for the paths in dirty. An empty list means that the run can
    start."""
    root = Path(root).resolve()
    errors = []
    for m in mutants:
        path = (root / m['file']).resolve()
        if not path.is_relative_to(root):
            errors.append(f'{m["id"]}: {m["file"]} is outside the repository')
            continue
        rel = path.relative_to(root).as_posix()
        project = root / m['project']
        if not path.is_file():
            errors.append(f'{m["id"]}: {m["file"]} does not exist')
        elif rel not in tracked:
            errors.append(f'{m["id"]}: git does not track {rel}, so git cannot show it unchanged at the end')
        elif rel in dirty:
            errors.append(f'{m["id"]}: {rel} has an uncommitted change; commit it or restore it first '
                          '(a run that was killed with SIGKILL leaves its mutant in the file)')
        else:
            found = len(_starts_on_line(path.read_bytes(), m['line'], m['original']))
            if found == 0:
                errors.append(f'{m["id"]}: the original text does not start on line {m["line"]} of {rel}')
            elif found > 1:
                errors.append(f'{m["id"]}: the original text starts twice on line {m["line"]} of {rel}; '
                              'make it longer so that its place is clear')
        if not (project / f'{project.name}.csproj').is_file():
            errors.append(f'{m["id"]}: {m["project"]} has no {project.name}.csproj')
    return errors


# Applying and restoring.

@contextlib.contextmanager
def _signals_blocked():
    held = {signal.SIGINT, signal.SIGTERM, signal.SIGHUP}
    signal.pthread_sigmask(signal.SIG_BLOCK, held)
    try:
        yield
    finally:
        signal.pthread_sigmask(signal.SIG_UNBLOCK, held)


@contextlib.contextmanager
def applied(path, line, original, replacement):
    """Inside the block, the file at path holds the mutant: replacement in place of original where it starts on line.
    On the way out, also on an exception or a signal that stop_on_signals turned into one, the file gets its exact
    original bytes back and a new modification time, so that the next incremental build compiles it again."""
    path = Path(path)
    data = path.read_bytes()
    offset = locate(data, line, original)
    if offset is None:
        raise ValueError(f'the original text does not start once on line {line} of {path}')
    mutated = data[:offset] + replacement.encode('utf-8') + data[offset + len(original.encode('utf-8')):]
    try:
        path.write_bytes(mutated)
        yield
    finally:
        # A second Ctrl-C during the restore must not leave a half-written file.
        with _signals_blocked():
            path.write_bytes(data)
            os.utime(path)


# Classifying a test run.

_SUMMARY = re.compile(r'^  (total|failed): (\d+)$', re.M)
_FAILED = re.compile(r'^failed (.*) \([^()]*\)\s*$', re.M)


def _short_test_name(name):
    """Class.Method, with the arguments of a theory case, without the namespace."""
    head, paren, rest = name.partition('(')
    return '.'.join(head.split('.')[-2:]) + paren + rest


def classify(code, timed_out, output):
    """The result of one dotnet test run of a mutant and up to three failed test names, from its exit code, whether
    the timeout stopped it, and its output. Exit code 8 of Microsoft.Testing.Platform means that zero tests ran."""
    if timed_out:
        return 'timeout', []
    counts = {}
    for key, value in _SUMMARY.findall(output):
        counts[key] = int(value)
    if code == 8 or counts.get('total') == 0:
        return 'no-tests', []
    if counts.get('failed'):
        names = [_short_test_name(n) for n in _FAILED.findall(output)]
        return 'killed', names[:mutation_table.KILLERS] or [f'(exit code {code})']
    if 'total' not in counts:
        # A crash before the summary is a kill, for example a validation layer that aborts the host. Exit code 0
        # without a summary shows no test that ran.
        return ('killed', [f'(test host exit code {code})']) if code else ('no-tests', [])
    if code:
        # Every test passed, but the session failed, for example on a leak check after the last test.
        return 'killed', [f'(test run exit code {code})']
    return 'survived', []


def filter_arguments(classes):
    """The --filter-class options of dotnet test for the classes: a bare name becomes *.Name."""
    out = []
    for c in classes:
        out += ['--filter-class', c if '.' in c or '*' in c else f'*.{c}']
    return out


# Running the commands.

def _run(command, root, log_path, timeout=None):
    """Runs command in its own process group with its output in log_path. Returns the exit code, whether the timeout
    stopped it, its output and its seconds. On a timeout, an exception or a signal, it kills the whole group, so that
    no test host keeps running with the mutant."""
    start = time.monotonic()
    with open(log_path, 'w') as log:
        process = subprocess.Popen(command, cwd=root, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
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
    return process.returncode, timed_out, Path(log_path).read_text(errors='replace'), time.monotonic() - start


def _build(root, project, log_path):
    csproj = Path(project) / f'{Path(project).name}.csproj'
    return _run(['dotnet', 'build', str(csproj), '-c', CONFIGURATION], root, log_path)[0] == 0


def _test(root, project, classes, log_path):
    command = ['dotnet', 'test', '--project', project, '-c', CONFIGURATION, '--no-build', *filter_arguments(classes)]
    code, timed_out, output, seconds = _run(command, root, log_path, TEST_TIMEOUT)
    return classify(code, timed_out, output)


def _assembly(root, file, project):
    """The paths of the assembly that holds file, in the Checked output of the test project: the assembly of the
    nearest folder above file with a .csproj."""
    folder = (Path(root) / file).parent
    while not list(folder.glob('*.csproj')):
        folder = folder.parent
    name = list(folder.glob('*.csproj'))[0].stem
    return list((Path(root) / project / 'bin' / CONFIGURATION).glob(f'*/{name}.dll'))


def _git_lines(root, *args):
    out = subprocess.run(['git', '-C', str(root), *args], check=True, capture_output=True, text=True).stdout
    return [line for line in out.split('\0') if line]


def _dirty(root, files=()):
    """The relative paths that git shows as changed or untracked, among files if given."""
    entries = iter(_git_lines(root, 'status', '--porcelain=v1', '-z', '--untracked-files=all', '--', *files))
    paths = set()
    for e in entries:
        # Each entry is 'XY path'. A rename or a copy is followed by an entry that holds only the old path.
        paths.add(e[3:])
        if e[0] in 'RC':
            paths.add(next(entries))
    return paths


def main():
    parser = argparse.ArgumentParser(description='Runs a list of mutants and prints the shared table.')
    parser.add_argument('list', help='the JSON list of mutants')
    parser.add_argument('out', help='the folder for results.json and the logs of each mutant')
    parser.add_argument('--only', help='run only these ids, separated by commas')
    parser.add_argument('--keep-going', action='store_true', help='go on after a mutant whose filter ran zero tests')
    args = parser.parse_args()
    if not os.environ.get('TERTIUS_VULKAN_DIR'):
        sys.exit('TERTIUS_VULKAN_DIR is empty. Run the script inside the dev shell: nix develop --command python3 -I '
                 'tools/mutate.py ...')
    root = Path(subprocess.check_output(['git', 'rev-parse', '--show-toplevel'], text=True).strip())
    mutants, errors = parse_list(Path(args.list).read_text())
    if args.only and not errors:
        wanted = args.only.split(',')
        errors += [f'--only names {i}, which the list does not have' for i in wanted if i not in {m['id'] for m in mutants}]
        mutants = [m for m in mutants if m['id'] in wanted]
    if not errors:
        errors = validate(mutants, root, set(_git_lines(root, 'ls-files', '-z')), _dirty(root))
    if errors:
        print('The list has errors, so no mutant ran:', *errors, sep='\n  ', file=sys.stderr)
        sys.exit(2)
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    stop_on_signals()
    started = time.monotonic()
    rows, stopped = [], None
    try:
        stopped = _baselines(root, mutants, out) or _check_assemblies(root, mutants)
        if not stopped:
            stopped = _run_mutants(root, mutants, out, rows, args.keep_going)
    except (KeyboardInterrupt, Stopped):
        stopped = 'the run was interrupted'
    finally:
        stopped = _finish(root, mutants, out, rows, stopped)
    mutation_table.print_table(rows)
    print(f'wall time {time.monotonic() - started:.0f} s; logs and results.json in {out}')
    if stopped:
        print(f'STOPPED: {stopped}', file=sys.stderr)
        sys.exit(1)


def _baselines(root, mutants, out):
    """Builds and runs each distinct pair of project and classes once without a mutant. Returns why the run must stop,
    or None."""
    pairs = list(dict.fromkeys((m['project'], tuple(m['classes'])) for m in mutants))
    for n, (project, classes) in enumerate(pairs, 1):
        print(f'baseline {n}/{len(pairs)}: {project} {" ".join(classes)}', file=sys.stderr, flush=True)
        if not _build(root, project, out / f'baseline-{n}.build.log'):
            return f'the baseline build of {project} failed, see {out}/baseline-{n}.build.log'
        result, names = _test(root, project, classes, out / f'baseline-{n}.test.log')
        if result != 'survived':
            return (f'the baseline of {project} {" ".join(classes)} without a mutant gave {result} {", ".join(names)}, '
                    f'see {out}/baseline-{n}.test.log; with it every mutant would look {result}')
    return None


def _check_assemblies(root, mutants):
    for m in mutants:
        if len(_assembly(root, m['file'], m['project'])) != 1:
            return (f'{m["id"]}: the Checked output of {m["project"]} has no single assembly of the project of '
                    f'{m["file"]}, so its tests cannot run the mutant')
    return None


def _run_mutants(root, mutants, out, rows, keep_going):
    """Runs every mutant and appends its row. Returns why the run must stop, or None."""
    for n, m in enumerate(mutants, 1):
        start = time.monotonic()
        path = root / m['file']
        with applied(path, m['line'], m['original'], m['replacement']):
            mutated_at = path.stat().st_mtime_ns
            if not _build(root, m['project'], out / f'{m["id"]}.build.log'):
                result, names = 'build-failed', []
            else:
                assembly = _assembly(root, m['file'], m['project'])[0]
                if assembly.stat().st_mtime_ns <= mutated_at:
                    return (f'{m["id"]}: the build did not write a new {assembly.name} into {m["project"]}, so the '
                            'tests would run the old code')
                result, names = _test(root, m['project'], m['classes'], out / f'{m["id"]}.test.log')
        rows.append({'id': m['id'], 'file': m['file'], 'line': m['line'],
                     'change': m['note'] or mutation_table.short_change(m['original'], m['replacement']),
                     'result': result, 'killed_by': names, 'seconds': time.monotonic() - start,
                     'original': m['original'], 'replacement': m['replacement'], 'classes': m['classes']})
        print(f'{n}/{len(mutants)} {m["id"]}: {result} {", ".join(names)}', file=sys.stderr, flush=True)
        if result == 'no-tests' and not keep_going:
            return f'{m["id"]}: its filter ran zero tests, an error in the list (--keep-going goes on)'
    return None


def _finish(root, mutants, out, rows, stopped):
    """Rebuilds each test project once, so that its output holds no mutant, writes results.json, and checks that git
    shows every mutated file unchanged. Returns why the run stopped, or None."""
    for n, project in enumerate(dict.fromkeys(m['project'] for m in mutants), 1):
        if not _build(root, project, out / f'restore-{n}.build.log'):
            print(f'WARNING: the rebuild of {project} after the run failed, see {out}/restore-{n}.build.log',
                  file=sys.stderr)
    changed = sorted(_dirty(root, sorted({m['file'] for m in mutants})))
    if changed:
        stopped = f'git shows these mutated files changed: {", ".join(changed)}. Restore them with git restore.'
    (out / 'results.json').write_text(json.dumps({'stopped': stopped, 'rows': rows}, indent=1) + '\n')
    return stopped


if __name__ == '__main__':
    main()
