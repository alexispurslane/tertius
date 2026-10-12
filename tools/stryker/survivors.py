# Tool code. Reads a Stryker.NET JSON report. Prints the mutant count for each status, the mutation score, and every
# mutant that no test caught (Survived) or that no test ran (NoCoverage), with its source line, for triage.
# With --config, it also lists the mutants inside the configuration's mutate spans that did not compile. The score
# leaves those out, so no test ran them: break their lines by hand instead.
#   python3 tools/stryker/survivors.py /tmp/stryker/Core/out/reports/mutation-report.json --config /tmp/stryker/Core/stryker-config.json
import argparse, collections, json, re

parser = argparse.ArgumentParser()
parser.add_argument('report')
parser.add_argument('--config', help='the stryker-config.json of the run, to list the mutants in its spans that did not compile')
args = parser.parse_args()

report = json.load(open(args.report))
spans = collections.defaultdict(list)
if args.config:
    for pattern in json.load(open(args.config))['stryker-config']['mutate']:
        m = re.match(r'\*\*/(.*?)(?:\{(\d+)\.\.(\d+)\})?$', pattern)
        spans[m.group(1)].append((int(m.group(2)), int(m.group(3))) if m.group(2) else (0, float('inf')))


def in_spans(path, source, mutant):
    """True if the mutant lies inside one of the configuration's spans of characters for its file."""
    starts = [0]
    for line in source.splitlines(keepends=True):
        starts.append(starts[-1] + len(line))
    s, e = mutant['location']['start'], mutant['location']['end']
    a, b = starts[s['line'] - 1] + s['column'] - 1, starts[e['line'] - 1] + e['column'] - 1
    return any(path.endswith('/' + rel) and x <= a and b <= y for rel, ranges in spans.items() for x, y in ranges)


counts = collections.Counter()
rows, unbuilt = [], []
for path, f in report['files'].items():
    lines = f['source'].split('\n')
    for m in f['mutants']:
        counts[m['status']] += 1
        n = m['location']['start']['line']
        row = (path.split('/src/')[-1], n, m['status'], m['mutatorName'], m.get('replacement', ''), lines[n - 1].strip())
        if m['status'] in ('Survived', 'NoCoverage'):
            rows.append(row)
        elif m['status'] == 'CompileError' and args.config and in_spans(path, f['source'], m):
            unbuilt.append(row)
# A timeout counts as caught: the mutant made a test hang, and Stryker stopped it.
caught = counts['Killed'] + counts['Timeout']
valid = caught + counts['Survived'] + counts['NoCoverage']
print(dict(counts))
print(f'score {100 * caught / max(valid, 1):.2f}% ({caught}/{valid})')
for path, n, status, mutator, replacement, source in sorted(rows):
    print(f'{path}:{n} [{status}] {mutator} -> {replacement!r}\n    {source}')
if args.config:
    print(f'{len(unbuilt)} mutants in the spans did not compile, so no test ran them. Break their lines by hand:')
    for path, n, status, mutator, replacement, source in sorted(unbuilt):
        print(f'{path}:{n} [{status}] {mutator}\n    {source}')
