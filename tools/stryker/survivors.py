# Tool code. Reads a Stryker.NET JSON report. Prints the mutant count for each status, the mutation score, and the
# shared table of tools/mutation_table.py: one row for each mutant inside the configuration's mutate spans, and one
# no-mutant row for each line in the spans where every mutant failed to compile. Stryker leaves those out of the score,
# so no test ran them: break their lines by hand with tools/mutate.py instead. tools/stryker/changed.py prints the same
# rows for the official run.
#   python3 tools/stryker/survivors.py /tmp/stryker/Core/out/reports/mutation-report.json --config /tmp/stryker/Core/stryker-config.json
import argparse, collections, json, os, re, subprocess, sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import mutation_table  # noqa: E402

# Stryker's statuses of a mutant that ran, as results of the shared table. A RuntimeError (the mutant crashed the test
# host) counts as caught, as Stryker counts it.
RESULTS = {'Killed': 'killed', 'Timeout': 'timeout', 'Survived': 'survived', 'NoCoverage': 'no-coverage',
           'RuntimeError': 'killed'}


def spans_of(config):
    """Maps each mutate pattern's path (relative to the project folder) to its spans of characters, from a
    stryker-config.json that tools/stryker/config.py wrote. A pattern without a span covers the whole file."""
    spans = collections.defaultdict(list)
    for pattern in config['stryker-config']['mutate']:
        m = re.match(r'\*\*/(.*?)(?:\{(\d+)\.\.(\d+)\})?$', pattern)
        spans[m.group(1)].append((int(m.group(2)), int(m.group(3))) if m.group(2) else (0, float('inf')))
    return spans


def _offsets(source):
    starts = [0]
    for line in source.splitlines(keepends=True):
        starts.append(starts[-1] + len(line))
    return starts


def _span_of(starts, mutant):
    s, e = mutant['location']['start'], mutant['location']['end']
    return starts[s['line'] - 1] + s['column'] - 1, starts[e['line'] - 1] + e['column'] - 1


def in_spans(spans, path, source, mutant):
    """True if the mutant lies inside one of the spans of characters for its file."""
    a, b = _span_of(_offsets(source), mutant)
    return any(path.endswith('/' + rel) and x <= a and b <= y for rel, ranges in spans.items() for x, y in ranges)


def _short_test_name(name):
    head, paren, rest = name.partition('(')
    return '.'.join(head.split('.')[-2:]) + paren + rest


def rows(report, spans, module, root):
    """The rows of the shared table for the mutants of report inside spans (every mutant if spans is None). The ids are
    <module>-<Stryker's id>, and <module>-L<line> for a no-mutant line. root is the repository root, which the report's
    absolute paths start with. Ignored mutants give no row: a Stryker ignore comment holds their triage."""
    tests = {t['id']: _short_test_name(t['name']) for f in report.get('testFiles', {}).values() for t in f['tests']}
    out, unbuilt = [], []
    for path, f in sorted(report['files'].items()):
        rel = os.path.relpath(path, root)
        starts = _offsets(f['source'])
        compiled_lines = set()
        for m in f['mutants']:
            if spans is not None and not in_spans(spans, path, f['source'], m):
                continue
            line = m['location']['start']['line']
            if m['status'] == 'CompileError':
                unbuilt.append((rel, line))
                continue
            if m['status'] not in RESULTS:
                continue
            compiled_lines.add(line)
            a, b = _span_of(starts, m)
            # A theory's cases share one name, so the names repeat.
            killers = list(dict.fromkeys(tests.get(t, t) for t in m.get('killedBy') or []))
            if m['status'] == 'RuntimeError':
                killers = ['(runtime error)']
            out.append({'id': f'{module}-{m["id"]}', 'file': rel, 'line': line,
                        'change': mutation_table.short_change(f['source'][a:b], m.get('replacement', '')),
                        'result': RESULTS[m['status']], 'killed_by': killers[:mutation_table.KILLERS],
                        'seconds': None, 'mutator': m['mutatorName'], 'covered_by': len(m.get('coveredBy') or [])})
        for line in sorted({n for r, n in unbuilt if r == rel} - compiled_lines):
            out.append({'id': f'{module}-L{line}', 'file': rel, 'line': line,
                        'change': 'every Stryker mutant failed to compile', 'result': 'no-mutant', 'killed_by': [],
                        'seconds': None})
    return out


def status_counts(report):
    return collections.Counter(m['status'] for f in report['files'].values() for m in f['mutants'])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('report')
    parser.add_argument('--config', help='the stryker-config.json of the run, to keep only the mutants in its spans')
    parser.add_argument('--module', default='M', help='the prefix of the ids, for example Core')
    args = parser.parse_args()
    report = json.load(open(args.report))
    spans = spans_of(json.load(open(args.config))) if args.config else None
    counts = status_counts(report)
    # A timeout counts as caught: the mutant made a test hang, and Stryker stopped it.
    caught = counts['Killed'] + counts['Timeout'] + counts['RuntimeError']
    valid = caught + counts['Survived'] + counts['NoCoverage']
    print(dict(counts))
    print(f'score {100 * caught / max(valid, 1):.2f}% ({caught}/{valid}), NoCoverage counted as survived')
    root = subprocess.check_output(['git', 'rev-parse', '--show-toplevel'], text=True).strip()
    mutation_table.print_table(rows(report, spans, args.module, root))


if __name__ == '__main__':
    main()
