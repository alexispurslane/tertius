# timing-table.py <round...>: the timing table of the film rect-bay from round.sh's folders
# /private/tmp/t0-xdck/timing/<round>-<side>/. Per run: the clock over the timed span (gpu-timings.json, "gpuClock"),
# from the per-frame span dump spans.txt (scratch switch TERTIUS_X_SPAN_DUMP) the median, mean and highest frame span,
# and the median of each requested range's per-frame sums (spans.txt.<first>..<last>). All times in ms. Per side over the
# rounds: the median of the runs' medians, the spread of the runs' medians (highest minus lowest), and the highest frame
# span of any run. The differences pair the runs of one round: per round, the side's run median minus the base's, then
# the median of those paired differences over the rounds that ran both, and their spread (highest minus lowest) in
# brackets. With --csv <file>, it writes each frame's span per run (the record's spans.csv, which spanchart.py reads).
import json
import statistics
import sys
from pathlib import Path

TIMING = Path('/private/tmp/t0-xdck/timing')
SIDES = ['M', 'X', 'Xt', 'D4', 'D8', 'D4s', 'C4', 'Xtc', 'D4c', 'D8c', 'Xtn']
RANGES = [('plan..rays', 'ray plan..diffuse rays'), ('plan..accum', 'ray plan..accumulate indirect specular')]

args = sys.argv[1:]
csv = None
if '--csv' in args:
    i = args.index('--csv')
    csv = args[i + 1]
    del args[i:i + 2]
rounds = args


def run(folder):
    spans = [float(l.split()[1]) for l in (folder / 'spans.txt').read_text().splitlines()]
    timings = json.loads((folder / 'gpu-timings.json').read_text())
    clock = timings.get('gpuClock', {}).get('meanMhz')
    ranges = {}
    for short, name in RANGES:
        sums = [float(l) for l in (folder / f'spans.txt.{name}').read_text().splitlines()]
        ranges[short] = statistics.median(sums)
    return {'spans': spans, 'median': statistics.median(spans), 'mean': statistics.fmean(spans), 'max': max(spans),
            'clock': clock, **ranges}


runs = {}
print('## Each run')
print()
print(f'{"run":10} {"clock MHz":>9} {"median":>7} {"mean":>7} {"highest":>8} {"plan..rays":>10} {"plan..accum":>11}')
for r in rounds:
    for side in SIDES:
        folder = TIMING / f'{r}-{side}'
        if not (folder / 'spans.txt').exists():
            continue
        x = run(folder)
        runs[(r, side)] = x
        clock = f'{x["clock"]:9.1f}' if x['clock'] else f'{"unknown":>9}'
        print(f'{r + "-" + side:10} {clock} {x["median"]:7.3f} {x["mean"]:7.3f} {x["max"]:8.3f} '
              f'{x["plan..rays"]:10.3f} {x["plan..accum"]:11.3f}')
print()
print('## Per side over the rounds: median of the runs\' medians (spread = highest minus lowest run median)')
print()
print(f'{"side":5} {"runs":>4} {"frame span":>18} {"highest":>8} {"plan..rays":>18} {"plan..accum":>18}')
summary = {}
for side in SIDES:
    mine = [runs[(r, side)] for r in rounds if (r, side) in runs]
    if not mine:
        continue
    cells = []
    for key in ('median', 'plan..rays', 'plan..accum'):
        values = [x[key] for x in mine]
        summary[(side, key)] = statistics.median(values)
        cells.append(f'{statistics.median(values):7.3f} ({max(values) - min(values):5.3f})')
    print(f'{side:5} {len(mine):4} {cells[0]:>18} {max(x["max"] for x in mine):8.3f} {cells[1]:>18} {cells[2]:>18}')
print()
print('## Paired differences against M, X, Xt and Xtc, ms: median over the rounds (spread), n rounds')
print('## (Xt: the drift test runs at every pixel but does not act. Xtc: the same at the blocks\' center pixels only)')
print()
for base in ('M', 'X', 'Xt', 'Xtc'):
    for side in SIDES:
        both = [r for r in rounds if (r, side) in runs and (r, base) in runs]
        if side == base or not both:
            continue
        cells = []
        for key in ('median', 'plan..rays', 'plan..accum'):
            d = [runs[(r, side)][key] - runs[(r, base)][key] for r in both]
            cells.append(f'{statistics.median(d):+7.3f} ({max(d) - min(d):5.3f})')
        print(f'{side:4} - {base:2}: frame span {cells[0]}, plan..rays {cells[1]}, plan..accum {cells[2]}, n {len(both)}')
if csv:
    keys = sorted(runs, key=lambda k: (SIDES.index(k[1]), k[0]))
    with open(csv, 'w') as out:
        out.write('frame,' + ','.join(f'{r}-{s}' for r, s in keys) + '\n')
        for i in range(min(len(runs[k]['spans']) for k in keys)):
            out.write(f'{i},' + ','.join(f'{runs[k]["spans"][i]:.4f}' for k in keys) + '\n')
