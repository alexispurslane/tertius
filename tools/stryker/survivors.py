# Tool code. Reads a Stryker.NET JSON report. Prints the mutant count for each status, the mutation score, and every
# mutant that no test caught (Survived) or that no test ran (NoCoverage), with its source line, for triage.
#   python3 tools/stryker/survivors.py /tmp/stryker/Core/out/reports/mutation-report.json
import collections, json, sys

report = json.load(open(sys.argv[1]))
counts = collections.Counter()
rows = []
for path, f in report['files'].items():
    lines = f['source'].split('\n')
    for m in f['mutants']:
        counts[m['status']] += 1
        if m['status'] in ('Survived', 'NoCoverage'):
            n = m['location']['start']['line']
            rows.append((path.split('/src/')[-1], n, m['status'], m['mutatorName'], m.get('replacement', ''), lines[n - 1].strip()))
# A timeout counts as caught: the mutant made a test hang, and Stryker stopped it.
caught = counts['Killed'] + counts['Timeout']
valid = caught + counts['Survived'] + counts['NoCoverage']
print(dict(counts))
print(f'score {100 * caught / max(valid, 1):.2f}% ({caught}/{valid})')
for path, n, status, mutator, replacement, source in sorted(rows):
    print(f'{path}:{n} [{status}] {mutator} -> {replacement!r}\n    {source}')
