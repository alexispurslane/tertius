# Tool code of t0-5tyj. motiontable.py [run prefix...]: the survey's motion part, M beside X, per film, from the runs
# <prefix>-<side> in /private/tmp/t0-5tyj/runs (default f-bay and f1 to f3). SIDES=<M side>,<X side> names the sides
# (default M,X; the merged code's films are fF with SIDES=MF,XF). Per film: flicker and grain flicker (the survey's per-pixel change
# against the neighbor frames, low and high frequency; limits 0.006 and 0.05), and the blotch and level of the worst
# sampled frame (the film's frame against a still of the same state; blotch limit 0.04, level limit 10 %), with each
# side's sampled frames (middle / end) and failed measures. A measure counts as worse on X where X's value passes M's by
# more than 10 % and by more than its floor (flicker 0.0001, grain flicker 0.0002, blotch 0.002, level's magnitude 0.002),
# and better the other way round. Every film with a worse measure needs a sheet of pictures (the record's rule).
import json
import os
import sys
from pathlib import Path

RUNS = Path('/private/tmp/t0-5tyj/runs')
FLOORS = {'flicker': 0.0001, 'grainFlicker': 0.0002, 'blotch': 0.002, 'level': 0.002}


def results(side):
    out = {}
    for run in sys.argv[1:] or ['f-bay'] + [f'f{g}' for g in range(1, 4)]:
        for p in sorted((RUNS / f'{run}-{side}' / 'results' / 'motion').glob('*.json')):
            out[p.stem] = json.loads(p.read_text())
    return out


def value(r, key):
    return r.get(key)


def compare(key, m, x):
    if m is None or x is None:
        return 'n/a'
    a, b = (abs(m), abs(x)) if key == 'level' else (m, x)
    if b > a * 1.10 + FLOORS[key]:
        return 'worse'
    if a > b * 1.10 + FLOORS[key]:
        return 'better'
    return 'equal'


def num(v, digits):
    return 'n/a' if v is None else f'{v:+.{digits}f}' if digits < 0 else f'{v:.{digits}f}'


m, x = (results(side) for side in os.environ.get('SIDES', 'M,X').split(','))
counts = {}
print(f"{'film':26s} {'flicker M':>9s} {'X':>8s} {'grain M':>8s} {'X':>8s} {'blotch M':>9s} {'X':>7s} {'level M':>8s} {'X':>8s}  verdict  failed M | X")
for film in sorted(set(m) | set(x)):
    if film not in m or film not in x:
        print(f'{film:26s} missing on {"M" if film not in m else "X"}')
        continue
    a, b = m[film], x[film]
    verdicts = {k: compare(k, value(a, k), value(b, k)) for k in FLOORS}
    worst = 'worse' if 'worse' in verdicts.values() else 'better' if 'better' in verdicts.values() else 'equal'
    counts[worst] = counts.get(worst, 0) + 1
    marks = ' '.join(f'{k}:{v}' for k, v in verdicts.items() if v not in ('equal', 'n/a')) or 'equal'
    lv = lambda r: 'n/a' if r.get('level') is None else f"{r['level']:+.3f}"
    bl = lambda r: 'n/a' if r.get('blotch') is None else f"{r['blotch']:.3f}"
    print(f"{film:26s} {num(a['flicker'], 5):>9s} {num(b['flicker'], 5):>8s} {num(a['grainFlicker'], 5):>8s} {num(b['grainFlicker'], 5):>8s} "
          f"{bl(a):>9s} {bl(b):>7s} {lv(a):>8s} {lv(b):>8s}  {marks}  {','.join(a['failed']) or '-'} | {','.join(b['failed']) or '-'}")
    for side, r in (('M', a), ('X', b)):
        s = r.get('samples') or {}
        print(f"    {side} samples: " + ', '.join(f"{k} blotch {bl(v)} level {lv(v)}" for k, v in s.items()))
print('films by their worst measure:', counts)
