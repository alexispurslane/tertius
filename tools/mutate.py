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
tracks it, by default it has no uncommitted change and it belongs to a project (a folder above it has a .csproj), the original
starts on its line, and the ids are unique. Then it builds and
runs each distinct pair of project and classes once without a mutant. A baseline that fails or runs zero tests stops the
run, because a failing baseline makes every mutant look killed.

For each mutant: apply it, build only its test project in Checked (a plain incremental build), check that the build
wrote a new assembly of the mutated file's project into the test project's output (so a GLSL change reached the
embedded SPIR-V), run only its classes in Checked with --no-build under a timeout of 120 s, then restore the exact
original bytes and touch the file. The restore also runs on a failure, an exception, Ctrl-C, SIGTERM and SIGHUP. After
the last mutant, the script builds each test project once more, so that its output matches the source again, and
checks every mutated file against its exact starting bytes and mode. A file of analyzers/ works the same way, with its test project
tests/Tertius.Unions.Tests or tests/Tertius.Style.Tests.

--allow-staged explicitly admits tracked regular stage-0 inputs whose bytes and executable mode equal the index.
Unstaged differences, unmerged entries and unsupported file modes remain errors. Every run snapshots all semantic
index entries, HEAD and pending merge metadata, and verifies them without rewriting Git state. inputs.json records
the source hashes and Git baseline. Unexpected edits fail the run and are never overwritten by restoration.

Results: killed (a test failed, or the test host crashed), timeout (counts as killed, listed apart), survived,
build-failed (the mutant does not compile, so it is not a valid mutant) and no-tests (the filter ran zero tests: an
error in the list, so the run stops unless --keep-going). The output folder gets results.json and, for each mutant,
<id>.build.log and <id>.test.log. The exit code is 0 when every mutant ran, 1 when the run stopped early or a file is
not restored, and 2 for an error in the list.
"""

import argparse
import contextlib
import hashlib
import json
import os
import re
import signal
import stat
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mutation_table  # noqa: E402
from processes import Stopped, run_logged, stop_on_signals  # noqa: E402

CONFIGURATION = 'Checked'
TEST_TIMEOUT = 120  # seconds; CLAUDE.md, Testing
REQUIRED = {'id': str, 'file': str, 'line': int, 'original': str, 'replacement': str, 'project': str}
OPTIONAL = {'note': str}


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


def validate(mutants, root, tracked, dirty, allow_staged=False):
    """The errors of the mutants against the repository at root, in which git tracks the relative paths in tracked and
    shows uncommitted changes, also untracked files, for the paths in dirty. With allow_staged, each input must match
    its single regular stage-0 entry exactly. An empty list means that the run can start."""
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
        elif rel in dirty and not allow_staged:
            errors.append(f'{m["id"]}: {rel} has an uncommitted change; commit it or restore it first '
                          '(a run that was killed with SIGKILL leaves its mutant in the file)')
        elif project_of(root, rel) is None:
            errors.append(f'{m["id"]}: {rel} belongs to no project, because no folder above it has a .csproj, so no '
                          'test build holds its mutant')
        else:
            if allow_staged:
                try:
                    if _file_input(path) != _staged_input(root, rel):
                        errors.append(f'{m["id"]}: {rel} differs from its staged bytes or mode')
                except (InputChanged, subprocess.CalledProcessError) as error:
                    errors.append(f'{m["id"]}: {error}')
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

class InputChanged(RuntimeError):
    """The run cannot safely use or restore its original input."""


def _file_input(path):
    mode = path.lstat().st_mode
    if not stat.S_ISREG(mode):
        raise InputChanged(f'{path} is not a regular file')
    return path.read_bytes(), bool(mode & 0o111)


def _staged_input(root, rel):
    entries = _git_lines(root, 'ls-files', '--stage', '-z', '--', rel)
    if len(entries) != 1:
        raise InputChanged(f'{rel} needs one tracked stage-0 entry')
    mode, object_id, stage = entries[0].split('\t', 1)[0].split()
    if stage != '0' or mode not in ('100644', '100755'):
        raise InputChanged(f'{rel} has an unsupported mode or unresolved stage')
    data = subprocess.check_output(['git', '-C', str(root), 'cat-file', 'blob', object_id])
    return data, mode == '100755'


class Inputs:
    """Exact mutated-file inputs, all semantic index entries and the pending merge identity; never rewrites Git state."""

    def __init__(self, root, mutants):
        self.root = root
        self.index = self._index()
        if any(entry.split('\t', 1)[0].split()[2] != '0' for entry in self.index):
            raise InputChanged('the index has unresolved stages')
        self.identity = self._identity()
        self.files = {}
        for rel in sorted({mutant['file'] for mutant in mutants}):
            value = _file_input(root / rel)
            if value != _staged_input(root, rel):
                raise InputChanged(f'{rel} differs from its staged bytes or mode')
            self.files[rel] = value
        if self.check():
            raise InputChanged('inputs changed while taking their baseline')

    def _index(self):
        return _git_lines(self.root, 'ls-files', '--stage', '-z')

    def _identity(self):
        identity = {'HEAD': subprocess.check_output(['git', '-C', str(self.root), 'rev-parse', '--verify', 'HEAD'])}
        for name in ('MERGE_HEAD', 'MERGE_MSG', 'MERGE_MODE'):
            value = subprocess.check_output(['git', '-C', str(self.root), 'rev-parse', '--git-path', name])
            path = self.root / os.fsdecode(value).strip()
            identity[name] = path.read_bytes() if path.exists() else None
        return identity

    def check(self):
        problems = []
        if self._index() != self.index:
            problems.append('semantic staged entries changed')
        if self._identity() != self.identity:
            problems.append('HEAD or pending merge metadata changed')
        for rel, expected in self.files.items():
            try:
                if _file_input(self.root / rel) != expected:
                    problems.append(f'{rel} bytes or mode changed')
            except (OSError, InputChanged):
                problems.append(f'{rel} is missing or no longer a regular input')
        return problems

    def evidence(self):
        return {'index': self.index,
                'identity': {key: os.fsdecode(value) if value is not None else None
                             for key, value in self.identity.items()},
                'files': {rel: {'sha256': hashlib.sha256(data).hexdigest(), 'executable': executable}
                          for rel, (data, executable) in self.files.items()}}

@contextlib.contextmanager
def _signals_blocked():
    held = {signal.SIGINT, signal.SIGTERM, signal.SIGHUP}
    signal.pthread_sigmask(signal.SIG_BLOCK, held)
    try:
        yield
    finally:
        signal.pthread_sigmask(signal.SIG_UNBLOCK, held)


@contextlib.contextmanager
def applied(path, line, original, replacement, baseline=None):
    """Inside the block, the file at path holds the mutant: replacement in place of original where it starts on line.
    On the way out, also on an exception or a signal that stop_on_signals turned into one, the file gets its exact
    original bytes back and a new modification time, unless an unexpected writer changed it, which fails without
    overwriting the edit. A supplied baseline must match before the mutation starts."""
    path = Path(path)
    starting = _file_input(path)
    if baseline is not None and starting != baseline:
        raise InputChanged(f'{path} changed before mutation; nothing was overwritten')
    data, executable = starting
    offset = locate(data, line, original)
    if offset is None:
        raise ValueError(f'the original text does not start once on line {line} of {path}')
    mutated = data[:offset] + replacement.encode('utf-8') + data[offset + len(original.encode('utf-8')):]
    try:
        path.write_bytes(mutated)
        yield
    finally:
        _restore(path, data, mutated, executable)


def _restore(path, data, mutated, executable):
    """Writes data to path and touches it, whatever signal arrives. A signal blocked during the write runs its handler
    when the block ends, and a signal can also arrive before the block starts. Either way the restore runs again, and
    the first signal goes on after the file holds data."""
    caught = None
    while True:
        try:
            with _signals_blocked():
                current, mode = _file_input(path)
                if current not in (data, mutated) or mode != executable:
                    raise InputChanged(f'{path} changed unexpectedly; restoration did not overwrite it')
                path.write_bytes(data)
                os.utime(path)
            break
        except (KeyboardInterrupt, Stopped) as e:
            caught = caught or e
    if caught:
        raise caught


# Classifying a test run.

_SUMMARY = re.compile(r'^  (total|failed): (\d+)$', re.M)
_FAILED = re.compile(r'^failed (.*) \([^()]*\)\s*$', re.M)


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
        names = [mutation_table.short_test_name(n) for n in _FAILED.findall(output)]
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

def _build(root, project, log_path):
    csproj = Path(project) / f'{Path(project).name}.csproj'
    return run_logged(['dotnet', 'build', str(csproj), '-c', CONFIGURATION], root, log_path)[0] == 0


def _test(root, project, classes, log_path):
    command = ['dotnet', 'test', '--project', project, '-c', CONFIGURATION, '--no-build', *filter_arguments(classes)]
    code, timed_out, output, seconds = run_logged(command, root, log_path, TEST_TIMEOUT)
    return classify(code, timed_out, output)


def project_of(root, file):
    """The name of the project whose assembly holds file (relative to root): the .csproj of the nearest folder above
    file, up to root. None if no folder up to root has one."""
    root = Path(root).resolve()
    folder = (root / file).resolve().parent
    while folder.is_relative_to(root):
        projects = sorted(folder.glob('*.csproj'))
        if projects:
            return projects[0].stem
        if folder == root:
            break
        folder = folder.parent
    return None


def _assembly(root, file, project):
    """The paths of the assembly of file's project in the Checked output of the test project."""
    return list((Path(root) / project / 'bin' / CONFIGURATION).glob(f'*/{project_of(root, file)}.dll'))


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
    parser.add_argument('--allow-staged', action='store_true',
                        help='admit tracked stage-0 inputs only when their bytes and mode exactly match the index')
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
        errors = validate(mutants, root, set(_git_lines(root, 'ls-files', '-z')), _dirty(root), args.allow_staged)
    baseline = None
    if not errors:
        try:
            baseline = Inputs(root, mutants)
        except (InputChanged, OSError, subprocess.CalledProcessError) as error:
            errors.append(str(error))
    if errors:
        print('The list has errors, so no mutant ran:', *errors, sep='\n  ', file=sys.stderr)
        sys.exit(2)
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    (out / 'inputs.json').write_text(json.dumps(baseline.evidence(), indent=1) + '\n')
    stop_on_signals()
    started = time.monotonic()
    rows, stopped = [], None
    try:
        stopped = _baselines(root, mutants, out) or _check_assemblies(root, mutants)
        if not stopped:
            stopped = _run_mutants(root, mutants, out, rows, args.keep_going, baseline)
    except (KeyboardInterrupt, Stopped):
        stopped = 'the run was interrupted'
    except InputChanged as error:
        stopped = str(error)
    finally:
        stopped = _finish(root, mutants, out, rows, stopped, baseline)
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


def _run_mutants(root, mutants, out, rows, keep_going, baseline):
    """Runs every mutant and appends its row. Returns why the run must stop, or None."""
    for n, m in enumerate(mutants, 1):
        problems = baseline.check()
        if problems:
            raise InputChanged('; '.join(problems))
        start = time.monotonic()
        path = root / m['file']
        with applied(path, m['line'], m['original'], m['replacement'], baseline.files[m['file']]):
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


def _finish(root, mutants, out, rows, stopped, baseline):
    """Rebuilds each test project once, so that its output holds no mutant, writes results.json, and checks that git
    matches the starting source/index/merge baseline. Unexpected changes skip the rebuild and remain untouched.
    Returns why the run stopped, or None."""
    problems = baseline.check()
    if not problems:
        for n, project in enumerate(dict.fromkeys(m['project'] for m in mutants), 1):
            if not _build(root, project, out / f'restore-{n}.build.log'):
                stopped = f'the rebuild of {project} after the run failed, see {out}/restore-{n}.build.log'
        problems = baseline.check()
    if problems:
        stopped = '; '.join(problems) + '; unexpected changes were preserved, not overwritten'
    (out / 'results.json').write_text(json.dumps({'stopped': stopped, 'rows': rows}, indent=1) + '\n')
    return stopped


if __name__ == '__main__':
    main()
