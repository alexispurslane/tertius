# Tool code. Writes a Stryker.NET configuration file for one source project, with the official settings below.
# By default it mutates only the lines that this branch changed. With --files it mutates whole files instead.
#   python3 tools/stryker/config.py --project Core --tests Core Simulation Physics --out /tmp/stryker/Core
#   python3 tools/stryker/config.py --project Core --tests Core --files Frames/FrameTree.cs --out /tmp/stryker/FrameTree
# Then run tools/stryker/run.py with the same project and folder. tools/stryker/changed.py does both for every module
# that a branch changed: that is the official run (tools/README.md, "Mutation testing").
import argparse, json, os, subprocess, sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from changed_lines import repo_root, changed_lines, runs  # noqa: E402

# The official settings, from the calibration of ter-hxca (.tickets/ter-hxca.md, the calibration note).
# Concurrency: the test hosts that run at once. Stryker.NET 5.0.0 under its MTP runner reports killed mutants as
# Survived at a concurrency above 1, and the count changes from run to run (issue #3832, tools/README.md, "Stryker.NET
# pitfalls"). Raise it only when tools/stryker/compare.py shows the same result for every mutant against concurrency 1.
CONCURRENCY = 1
# The xunit threads of all test hosts together. Each host runs its test classes on one thread per processor, and with
# three hosts of ten threads each the 1-minute load of this 10-core machine reached 173 while three agents built.
MAX_TEST_THREADS = 4


def test_threads(concurrency):
    """The xunit threads of each test host at the concurrency, so that all hosts together use at most MAX_TEST_THREADS.
    tools/stryker/changed.py passes it as DOTNET_PROCESSOR_COUNT, the processor count that xunit's default reads.
    Raises ValueError above MAX_TEST_THREADS hosts, because each host needs one thread."""
    if not 1 <= concurrency <= MAX_TEST_THREADS:
        raise ValueError(f'the concurrency must be 1 to {MAX_TEST_THREADS}, so that the test hosts together use at '
                         f'most {MAX_TEST_THREADS} xunit threads')
    return MAX_TEST_THREADS // concurrency


# Stryker stops a mutant's test run after TIMEOUT_RATIO x the covering tests' initial time + ADDITIONAL_TIMEOUT_MS.
# The MTP runner also stops one test of the per-test coverage capture after 1.5 x its initial time +
# ADDITIONAL_TIMEOUT_MS, at least 30 s, and marks it Dubious. A test whose class fixture does the work, such as
# DeterminismTests, has an initial time near 0 but takes 80 to 90 s alone under the capture's instrumentation, so the
# Stryker default of 10 s and a trial of 90 s both left Dubious tests. After such a timeout, Stryker 5.0.0 also waits
# 10 s for the killed host's coverage relay on every later test of that runner and marks each one Dubious.
TIMEOUT_RATIO = 2
ADDITIONAL_TIMEOUT_MS = 300000
# Checked: Debug's contracts with Release's optimized code, as tools/mutate.py and the pre-commit hook test.
CONFIGURATION = 'Checked'


def mutate_patterns(root, project, base, head, exclude, files):
    """The mutate patterns of the project src/Tertius.<project>: whole files for files, or else the lines that the
    branch from base to head changed, as spans of characters in the working tree's files. With a head other than
    HEAD, every such file must be the same in the working tree as at head, or the spans would be wrong."""
    project_dir = os.path.join(root, f'src/Tertius.{project}')
    # Stryker.NET matches a mutate pattern against the full path, so every pattern starts with **/.
    if files is not None:
        return [f'**/{f}' for f in files]
    mutate = []
    for rel, lines in sorted(changed_lines(root, base, [f'src/Tertius.{project}'], head).items()):
        if os.path.basename(rel) in exclude:
            continue
        if subprocess.run(['git', '-C', root, 'diff', '--quiet', head, '--', rel]).returncode != 0:
            sys.exit(f'{rel} differs between {head} and the working tree, so its changed lines are not where the '
                     f'diff to {head} says. Use a range whose files are unchanged since, or run on that checkout.')
        path = os.path.join(root, rel)
        # Stryker.NET reads {a..b} as character offsets in the file, not as line numbers.
        text = open(path, encoding='utf-8-sig').read()
        starts = [0]
        for line in text.splitlines(keepends=True):
            starts.append(starts[-1] + len(line))
        in_project = os.path.relpath(path, project_dir)
        for a, b in runs(lines):
            mutate.append(f'**/{in_project}{{{starts[a - 1]}..{starts[min(b, len(starts) - 1)]}}}')
    return mutate


def write_config(out, mutate, tests, concurrency=CONCURRENCY, additional_timeout=ADDITIONAL_TIMEOUT_MS):
    """Writes <out>/stryker-config.json for the mutate patterns and the test modules, and returns its path."""
    config = {'stryker-config': {
        'test-runner': 'mtp',
        # Stryker.NET 5.0.0 takes coverage-analysis only from a configuration file, not from the command line.
        'coverage-analysis': 'perTest',
        'configuration': CONFIGURATION,
        'concurrency': concurrency,
        'timeout-ratio': TIMEOUT_RATIO,
        'additional-timeout': additional_timeout,
        # A test that fails without a mutant would make every mutant that it covers look killed, so the run stops.
        'break-on-initial-test-failure': True,
        'mutate': mutate,
        'test-projects': [f'../../tests/Tertius.{t}.Tests/Tertius.{t}.Tests.csproj' for t in tests],
        'reporters': ['json', 'html', 'progress'],
        'thresholds': {'high': 80, 'low': 70, 'break': 0},
    }}
    os.makedirs(out, exist_ok=True)
    path = os.path.join(out, 'stryker-config.json')
    with open(path, 'w') as f:
        json.dump(config, f, indent=1)
    return path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--project', required=True, help='the module name, for example Core for src/Tertius.Core')
    parser.add_argument('--tests', nargs='+', required=True, help='the test modules that run, for example Core Simulation')
    parser.add_argument('--out', required=True, help='the folder for the configuration file and the reports')
    parser.add_argument('--base', default='main', help='the branch that this branch starts from')
    parser.add_argument('--head', default='HEAD', help='the last commit of the changes; its files must equal the working tree')
    parser.add_argument('--exclude', nargs='*', default=[], help='file names to leave out, for example Program.cs')
    parser.add_argument('--files', nargs='*', help='paths relative to the project folder to mutate whole; skips the diff')
    args = parser.parse_args()
    mutate = mutate_patterns(repo_root(), args.project, args.base, args.head, args.exclude, args.files)
    if not mutate:
        sys.exit(f'nothing to mutate in src/Tertius.{args.project}')
    path = write_config(args.out, mutate, args.tests)
    print(f'{len(mutate)} mutate patterns written to {path}')


if __name__ == '__main__':
    main()
