# Tool code: shared by tools/mutate.py and tools/stryker/changed.py. Both print their mutants in this one table, so that
# one ticket note can merge the two (tools/README.md, "Mutation testing").
#
# A row is a dict: id, file (relative to the repository root), line (1-based), change (the change in short), result
# (one of RESULTS), killed_by (up to three test names) and seconds (the time of the mutant's build and test run, or None).
import re

# Every result that a row can have, in the order of the summary line. killed and timeout are caught mutants. survived
# and no-coverage (Stryker: no test ran the mutant) are survivors to triage. build-failed (the mutant does not compile)
# and no-tests (the filter ran zero tests) are not valid mutants. no-mutant (a changed line where no Stryker mutant
# compiled) and removed (lines that the branch deleted) are lines for the agent's tools/mutate list.
RESULTS = ('killed', 'timeout', 'survived', 'no-coverage', 'build-failed', 'no-tests', 'no-mutant', 'removed')
CHANGE_WIDTH = 60
KILLERS = 3


def _compact(text):
    return re.sub(r'\s+', ' ', text).strip()


def short_change(original, replacement):
    """'original -> replacement' on one line, each side cut so that the whole fits in CHANGE_WIDTH characters."""
    side = (CHANGE_WIDTH - len(' -> ')) // 2

    def cut(text):
        text = _compact(text) or "''"
        return text if len(text) <= side else text[:side - 3] + '...'

    return f'{cut(original)} -> {cut(replacement)}'


def short_test_name(name):
    """The name of a test for the killed-by column: Class.Method, with the arguments of a theory case, without the
    namespace."""
    head, paren, rest = name.partition('(')
    return '.'.join(head.split('.')[-2:]) + paren + rest


def _cell(text):
    return str(text).replace('|', '\\|')


def format_rows(rows):
    """The Markdown lines of the table: a header, a separator and one line per row, in the given order."""
    lines = ['| id | place | change | result | killed by | s |', '|---|---|---|---|---|---:|']
    for r in rows:
        seconds = '-' if r['seconds'] is None else f'{r["seconds"]:.1f}'
        cells = (r['id'], f'{r["file"]}:{r["line"]}', r['change'], r['result'], ', '.join(r['killed_by'][:KILLERS]), seconds)
        lines.append('| ' + ' | '.join(_cell(c) for c in cells) + ' |')
    return lines


def summary(rows):
    """One line with the count of rows for every result in RESULTS, zeros included. An unknown result raises ValueError."""
    counts = dict.fromkeys(RESULTS, 0)
    for r in rows:
        if r['result'] not in counts:
            raise ValueError(f'unknown result {r["result"]!r} in row {r["id"]}')
        counts[r['result']] += 1
    return f'{len(rows)} rows: ' + ', '.join(f'{name} {n}' for name, n in counts.items())


def print_table(rows):
    print('\n'.join(format_rows(rows)))
    print(summary(rows))
