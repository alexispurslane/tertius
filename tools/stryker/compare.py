#!/usr/bin/env python3
"""Tool code. Compares the per-mutant results of official Stryker runs on the same spans, for example a run at a higher
concurrency against a run at concurrency 1. Run it inside the dev shell or outside it:

    python3 -I tools/stryker/compare.py <reference/results.json> <other/results.json> [<other/results.json> ...]

Each file is the results.json of tools/stryker/changed.py. The script prints every mutant whose result differs from the
reference, or that only one of the two runs has. Its exit code is 0 when every other run gives the same result for every
mutant, and 1 otherwise.

Stryker.NET 5.0.0 under its MTP runner reports killed mutants as Survived at a concurrency above 1, and the count
changes from run to run (issue #3832, tools/README.md, "Stryker.NET pitfalls"). An equal score does not show that the
fault is gone, because other mutants can change places. So the check compares each mutant.
"""

import json
import sys


def _key(row):
    # A Stryker id names the same mutant only on the same code and spans, so the place and the change are part of it.
    return row['id'], row['file'], row['line'], row['change']


def differences(reference, other):
    """One line of text for each mutant whose result differs between the rows of reference and of other, or that only
    one of them has. Empty when every mutant has the same result in both."""
    a = {_key(r): r['result'] for r in reference}
    b = {_key(r): r['result'] for r in other}
    out = []
    for key in sorted(a.keys() | b.keys(), key=lambda k: (k[1], k[2], k[0])):
        if a.get(key) != b.get(key):
            out.append(f'{key[0]} {key[1]}:{key[2]} {key[3]}: {a.get(key, "absent")} in the reference, '
                       f'{b.get(key, "absent")} in the other run')
    return out


def main():
    if len(sys.argv) < 3:
        sys.exit('usage: tools/stryker/compare.py <reference/results.json> <other/results.json> ...')
    reference = json.load(open(sys.argv[1]))['rows']
    failed = False
    for path in sys.argv[2:]:
        found = differences(reference, json.load(open(path))['rows'])
        print(f'{path}: {len(found)} mutants differ from {sys.argv[1]}')
        for line in found:
            print(f'  {line}')
        failed = failed or bool(found)
    sys.exit(1 if failed else 0)


if __name__ == '__main__':
    main()
