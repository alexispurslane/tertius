# Tool code of t1-6tn2: the tables of section 10 of the record (the fixed part, split) from facecost.py's lines.
# Each argument is <label>=<facecost lines>: one machine and one measure, for example "Mac, spacing 2=mac2.jsonl". A run
# is named [<prefix>-]r<round>-w<size>-c<cull>[-<parts>] (queue-mac.sh, card.sh); the parts are TERTIUS_PROBE_FACE_PARTS
# (FaceProbe.cs), none for today's face.
#   face cost   a run's face_cost_span_ms (both maps), or with --map 1 the ship's map's span only (the lean field face)
#   saving      today's face cost less the variant's, the means over runs of the same measure and session
# The share table (without --map): each source's saving as a share of today's face cost:
#   the Earth                       today - earth-off
#     its near range                today - earth-reach (inside the Earth's saving)
#     a coarse Earth would save     today - earth-coarse
#   the irradiance convolution      today - irr-off
#   the rest of the prefilter       (today - prefilter-off) - (today - irr-off)
#   what is left                    earth-off+prefilter-off
#   interaction                     today - (Earth + prefilter + left): what the separate savings do not add up to
# Usage: python3 -I split.py [--map 1] <label>=<jsonl> ...
import json, re, sys
from collections import defaultdict

args = sys.argv[1:]
map_only = None
if args[:1] == ['--map']:
    map_only, args = f'map {args[1]}', args[2:]
NAME = re.compile(r'^(?:\dx\d-)?r(\d+)-w(\d+)-c(\d)(?:-(.+))?$')


def mean(v):
    return sum(v) / len(v)


def cost(row):
    return row[map_only]['span'][0] if map_only else row['face_cost_span_ms'][0]


sets = []
for arg in args:
    label, path = arg.split('=', 1)
    by = defaultdict(list)
    for line in open(path):
        if not line.strip():
            continue
        row = json.loads(line)
        m = NAME.match(row['run'])
        if not m:
            continue
        by[(int(m[2]), int(m[3]), m[4] or 'none')].append(row)
    sets.append((label, by))

print('| Measure | Face | Parts | Runs | Load at the lock | Face cost per run (ms) | Mean (ms) | Saving against today (ms) | Share of today | 216 faces in 60 draws (ms per draw) | Triangles per face, ship\'s map |')
print('|---|---:|---|---:|---|---|---:|---:|---:|---:|---:|')
for label, by in sets:
    base = mean([cost(r) for r in by[(128, 0, 'none')]]) if (128, 0, 'none') in by else None
    for (w, c, parts), runs in sorted(by.items(), key=lambda kv: (kv[0][2] != 'none', -kv[0][0], kv[0][2])):
        v = [cost(r) for r in runs]
        loads = ', '.join('-' if r['load_1min'] is None else f"{r['load_1min']:.1f}" for r in runs)
        saving = f'{base - mean(v):.3f}' if base is not None and parts != 'none' else ''
        share = f'{mean(v) / base:.0%}' if base else ''
        tri = mean([r['map 1']['kept_culled_commands_triangles_mean'][3] for r in runs if 'map 1' in r])
        print(f"| {label} | {w} | {parts} | {len(runs)} | {loads} | {', '.join(f'{x:.3f}' for x in v)} | {mean(v):.3f} | "
              f"{saving} | {share} | {mean(v) * 216 / 60:.2f} | {tri:,.0f} |")

if map_only:
    sys.exit()
print('\n| Source | ' + ' | '.join(label for label, _ in sets) + ' |')
print('|---|' + '---:|' * len(sets))
rows = defaultdict(list)
for label, by in sets:
    c = {k[2]: mean([cost(r) for r in v]) for k, v in by.items() if k[0] == 128}
    today = c['none']
    earth = today - c['earth-off']
    prefilter = today - c['prefilter-off']
    irradiance = today - c['irr-off']
    left = c['earth-off+prefilter-off']
    for name, value in (('Today, one face (ms)', today),
                        ('The Earth', earth),
                        ('  of it, its draws in the near range', today - c['earth-reach']),
                        ('  a 4,096-triangle Earth would save', today - c['earth-coarse']),
                        ('The irradiance convolution', irradiance),
                        ('The rest of the prefilter', prefilter - irradiance),
                        ('What is left: the room, the light pass, the passes\' fixed cost', left),
                        ('Interaction: today - (Earth + prefilter + left)', today - earth - prefilter - left)):
        rows[name].append(f'{value:.3f}' if name.startswith('Today') else f'{value:.3f} ({value / today:.0%})')
for name, cells in rows.items():
    print(f'| {name} | ' + ' | '.join(cells) + ' |')
