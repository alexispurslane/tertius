# Tool code of ter-yglw: the record's tables from facecost.py's lines (one JSON object per run) of one machine.
# Per variant (face size W, cull on or off), over its runs named r<round>-w<W>-c<cull>:
#   face cost       each run's mean pair difference of the GPU span (facecost.py), then the mean and the range over runs
#   per 216 faces   the face cost times 216 / 60: the cost per draw of refreshing 216 faces within 60 draws
#   passes          the mean pair differences of the passes "environment faces" and "environment prefilter", per map
#   counts          per map, the mean kept instances, the instances that only the size cull left out, and triangles
# Then the fixed part: with cost(W) = a + b W^2 fitted through the two sizes (texels W^2), a is the part of a face's
# cost that does not shrink with its size, per pass and for the span.
# Usage: python3 -I summarize.py <facecost lines> <machine name>
import json, sys
from collections import defaultdict

rows = [json.loads(l) for l in open(sys.argv[1]) if l.strip()]
machine = sys.argv[2]
by = defaultdict(list)
for r in rows:
    name = r['run']
    if not name.startswith('r'):
        continue
    w, c = name.split('-')[1:3]
    by[(int(w[1:]), int(c[1:]))].append(r)


def mean(v):
    return sum(v) / len(v)


print(f'### {machine}: face cost per variant\n')
print('| Face | Cull | Runs | Load at the lock (1 min) | Face cost (ms) | Range over runs (ms) | 216 faces in 60 draws (ms per draw) |')
print('|---:|---|---:|---|---:|---|---:|')
cost = {}
for (w, c) in sorted(by, key=lambda k: (-k[0], k[1])):
    runs = by[(w, c)]
    v = [r['face_cost_span_ms'][0] for r in runs]
    cost[(w, c)] = mean(v)
    loads = ', '.join('-' if r['load_1min'] is None else f"{r['load_1min']:.1f}" for r in runs)
    print(f"| {w} | {'on' if c else 'off'} | {len(runs)} | {loads} | {mean(v):.3f} | {min(v):.3f} to {max(v):.3f} | "
          f"{mean(v) * 216 / 60:.2f} |")
print(f'\n### {machine}: the passes per map (mean pair differences, ms)\n')
print('| Face | Cull | Map | Span | environment faces | environment prefilter | Faces + prefilter | Kept, culled instances | Triangles |')
print('|---:|---|---:|---:|---:|---:|---:|---|---:|')
passes = {}
for (w, c) in sorted(by, key=lambda k: (-k[0], k[1])):
    runs = by[(w, c)]
    for m in ('map 0', 'map 1'):
        p = {k: mean([r[m][k][0] for r in runs]) for k in ('span', 'faces', 'prefilt', 'both')}
        passes[(w, c, m)] = p
        k = runs[0][m]['kept_culled_commands_triangles_mean']
        print(f"| {w} | {'on' if c else 'off'} | {m[-1]} | {p['span']:.3f} | {p['faces']:.3f} | {p['prefilt']:.3f} | "
              f"{p['both']:.3f} | {k[0]:.1f}, {k[1]:.1f} | {k[3]:,.0f} |")
print(f'\n### {machine}: the part of a face\'s cost that does not shrink with its size (cost = a + b W², no cull)\n')
print('| Quantity | At 128 (ms) | At 32 (ms) | a, the fixed part (ms) | Fixed share at 32 |')
print('|---|---:|---:|---:|---:|')
for label, get in (('Span, both maps', lambda w: cost[(w, 0)]),
                   ('environment faces, map 1', lambda w: passes[(w, 0, 'map 1')]['faces']),
                   ('environment faces, map 0', lambda w: passes[(w, 0, 'map 0')]['faces']),
                   ('environment prefilter, map 1', lambda w: passes[(w, 0, 'map 1')]['prefilt'])):
    hi, lo = get(128), get(32)
    b = (hi - lo) / (128 ** 2 - 32 ** 2)
    a = lo - b * 32 ** 2
    print(f'| {label} | {hi:.3f} | {lo:.3f} | {a:.3f} | {a / lo:.0%} |')
