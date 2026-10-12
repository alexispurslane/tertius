# Tool code. Merges Cobertura reports and prints the coverage of the lines that this branch changed in src/.
# A line counts as covered if any report covers it, so passing the Debug and Release folders gives their union:
# Release covers the code behind `#if !DEBUG`, and Debug covers the contracts.
#   python3 tools/coverage/changed-lines.py [--base main] /tmp/coverage/Debug /tmp/coverage/Release
import argparse, glob, os, re, sys, xml.etree.ElementTree as ET
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from changed_lines import repo_root, changed_lines, runs

parser = argparse.ArgumentParser()
parser.add_argument('--base', default='main', help='the branch that this branch starts from')
parser.add_argument('reports', nargs='+', help='folders that hold *.cobertura.xml files, searched recursively')
args = parser.parse_args()

root = repo_root()
hits = defaultdict(dict)      # absolute file -> line -> highest hit count in any report
branches = defaultdict(dict)  # absolute file -> line -> (covered, total) from the report that covers the most
for path in [p for folder in args.reports for p in glob.glob(os.path.join(folder, '**', '*.cobertura.xml'), recursive=True)]:
    for cls in ET.parse(path).getroot().iter('class'):
        name = cls.get('filename')
        if not name:
            continue
        name = os.path.realpath(name)
        for line in cls.iter('line'):
            n, h = int(line.get('number')), int(line.get('hits'))
            hits[name][n] = max(hits[name].get(n, 0), h)
            condition = line.get('condition-coverage')
            if condition:
                m = re.search(r'\((\d+)/(\d+)\)', condition)
                covered, total = int(m.group(1)), int(m.group(2))
                old = branches[name].get(n)
                if not old or covered > old[0]:
                    branches[name][n] = (covered, total)

def ranges(numbers):
    return ','.join(f'{a}-{b}' if a != b else str(a) for a, b in runs(numbers))

rows = []
total_lines = covered_lines = total_branches = covered_branches = 0
for rel, lines in changed_lines(root, args.base, ['src']).items():
    name = os.path.realpath(os.path.join(root, rel))
    h, b = hits.get(name, {}), branches.get(name, {})
    instrumented = [n for n in lines if n in h]
    uncovered = [n for n in instrumented if h[n] == 0]
    with_branches = [n for n in lines if n in b]
    bc, bt = sum(b[n][0] for n in with_branches), sum(b[n][1] for n in with_branches)
    partial = [n for n in with_branches if b[n][0] < b[n][1]]
    total_lines += len(instrumented); covered_lines += len(instrumented) - len(uncovered)
    total_branches += bt; covered_branches += bc
    if instrumented:
        rows.append((len(uncovered), rel, len(instrumented) - len(uncovered), len(instrumented), bc, bt, uncovered, partial))

print(f'changed lines covered: {covered_lines}/{total_lines} = {100 * covered_lines / max(total_lines, 1):.1f}%; '
      f'changed branches covered: {covered_branches}/{total_branches} = {100 * covered_branches / max(total_branches, 1):.1f}%')
for _, rel, c, t, bc, bt, uncovered, partial in sorted(rows, key=lambda r: (-r[0], r[1])):
    print(f'{rel}: lines {c}/{t} ({100 * c / t:.0f}%), branches {bc}/{bt}; uncovered {ranges(uncovered)}; partial branches {ranges(partial)}')
