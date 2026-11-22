# Tool code of t0-5tyj. stilltable.py [run group...]: the survey's still part, M beside X, per case, from the runs
# s<g>-M and s<g>-X in /private/tmp/t0-5tyj/runs (default groups 1 to 7; group 7 holds only X's retakes, which pair with group 6's M by case name). Per case: specks (bright single-pixel groups
# per 10,000 visible pixels, beyond the reference's; one speck in a 1920 x 1080 view is 0.0048), blotch (the survey's
# score against the build's own long reference; limit 0.04) and level (the still against its long reference, minus 1),
# and the survey's failed measures of each side. A measure counts as worse on X where X's value passes M's by more than
# 10 % and by more than its floor: one speck (0.0049) for specks, 0.001 for blotch, 0.002 for the level's magnitude. The
# same rule the other way counts as better. Every case with a worse measure needs a sheet of pictures (the record's rule).
import json
import sys
from pathlib import Path

RUNS = Path('/private/tmp/t0-5tyj/runs')
FLOORS = {'specks': 0.0049, 'blotch': 0.001, 'level': 0.002}
groups = sys.argv[1:] or [str(g) for g in range(1, 8)]


def results(side):
    out = {}
    for g in groups:
        for p in sorted((RUNS / f's{g}-{side}' / 'results' / 'still').glob('*.json')):
            out[p.stem] = json.loads(p.read_text())
    return out


def compare(key, m, x):
    if m is None or x is None:  # The survey's blotch is None where nothing is visible away from the emitters.
        return 'equal' if m is x else 'n/a'
    a, b = (abs(m), abs(x)) if key == 'level' else (m, x)
    if b > a * 1.10 + FLOORS[key]:
        return 'worse'
    if a > b * 1.10 + FLOORS[key]:
        return 'better'
    return 'equal'


def number(v, spec):
    return f'{"n/a":>{spec.lstrip("+").split(".")[0]}s}' if v is None else format(v, spec)


m, x = results('M'), results('X')
counts = {}
print(f"{'case':26s} {'specks M':>9s} {'X':>8s} {'blotch M':>9s} {'X':>8s} {'level M':>9s} {'X':>9s}  verdict per measure  failed M | X")
for case in sorted(set(m) | set(x)):
    if case not in m or case not in x:
        print(f'{case:26s} missing on {"M" if case not in m else "X"}')
        continue
    a, b = m[case], x[case]
    verdicts = {k: compare(k, a[k], b[k]) for k in FLOORS}
    worst = 'worse' if 'worse' in verdicts.values() else 'better' if 'better' in verdicts.values() else 'equal'
    counts[worst] = counts.get(worst, 0) + 1
    marks = ' '.join(f'{k}:{v}' for k, v in verdicts.items() if v != 'equal') or 'equal'
    print(f"{case:26s} {a['specks']:9.4f} {b['specks']:8.4f} {number(a['blotch'], '9.4f')} {number(b['blotch'], '8.4f')} "
          f"{a['level']:+9.4f} {b['level']:+9.4f}  "
          f"{marks:20s} {','.join(a['failed']) or '-'} | {','.join(b['failed']) or '-'}")
print('cases by their worst measure:', counts)
