# Tool code. Writes a Stryker.NET configuration file for one source project.
# By default it mutates only the lines that this branch changed. With --files it mutates whole files instead.
#   python3 tools/stryker/config.py --project Core --tests Core Simulation Physics --out /tmp/stryker/Core
#   python3 tools/stryker/config.py --project Core --tests Core --files Frames/FrameTree.cs --out /tmp/stryker/FrameTree
# Then run tools/stryker/run.sh with the same project and folder.
import argparse, json, os, sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from changed_lines import repo_root, changed_lines, runs

parser = argparse.ArgumentParser()
parser.add_argument('--project', required=True, help='the module name, for example Core for src/Tertius.Core')
parser.add_argument('--tests', nargs='+', required=True, help='the test modules that run, for example Core Simulation')
parser.add_argument('--out', required=True, help='the folder for the configuration file and the reports')
parser.add_argument('--base', default='main', help='the branch that this branch starts from')
parser.add_argument('--exclude', nargs='*', default=[], help='file names to leave out, for example Program.cs')
parser.add_argument('--files', nargs='*', help='paths relative to the project folder to mutate whole; skips the diff')
args = parser.parse_args()

root = repo_root()
project = f'src/Tertius.{args.project}'
project_dir = os.path.join(root, project)

# Stryker.NET matches a mutate pattern against the full path, so every pattern starts with **/.
if args.files is not None:
    mutate = [f'**/{f}' for f in args.files]
else:
    mutate = []
    for rel, lines in sorted(changed_lines(root, args.base, [project]).items()):
        if os.path.basename(rel) in args.exclude:
            continue
        path = os.path.join(root, rel)
        # Stryker.NET reads {a..b} as character offsets in the file, not as line numbers.
        text = open(path, encoding='utf-8-sig').read()
        starts = [0]
        for line in text.splitlines(keepends=True):
            starts.append(starts[-1] + len(line))
        in_project = os.path.relpath(path, project_dir)
        for a, b in runs(lines):
            mutate.append(f'**/{in_project}{{{starts[a - 1]}..{starts[min(b, len(starts) - 1)]}}}')
if not mutate:
    sys.exit(f'nothing to mutate in {project}')

config = {'stryker-config': {
    'test-runner': 'mtp',
    # Stryker.NET 5.0.0 takes coverage-analysis only from a configuration file, not from the command line.
    'coverage-analysis': 'perTest',
    'mutate': mutate,
    'test-projects': [f'../../tests/Tertius.{t}.Tests/Tertius.{t}.Tests.csproj' for t in args.tests],
    'reporters': ['json', 'html', 'progress'],
    'thresholds': {'high': 80, 'low': 70, 'break': 0},
}}
os.makedirs(args.out, exist_ok=True)
with open(os.path.join(args.out, 'stryker-config.json'), 'w') as f:
    json.dump(config, f, indent=1)
print(f'{len(mutate)} mutate patterns written to {os.path.join(args.out, "stryker-config.json")}')
