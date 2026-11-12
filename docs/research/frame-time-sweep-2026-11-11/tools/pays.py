"""What each pass costs the frame span, from a trace's exported tables. At each instant of a frame's span, the time goes
to the earliest pass (in recording order) that has a GPU interval open; an instant with none open is idle time, which goes
to the next pass that starts. So the rows add up to the span exactly. Timestamp encoders count as their own row.
Usage: pays.py <export dir> <gpu-timings.json> [first settled frame, default 40] [--json out]"""
import importlib.util, json, sys, statistics
from collections import defaultdict
from pathlib import Path
W = '/Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0/tools'
sys.path.insert(0, W)
spec = importlib.util.spec_from_file_location('gputrace', W + '/gpu-trace.py'); g = importlib.util.module_from_spec(spec); spec.loader.exec_module(g)
exports = Path(sys.argv[1]); timings = json.loads(Path(sys.argv[2]).read_text())
first_settled = int(sys.argv[3]) if len(sys.argv) > 3 and not sys.argv[3].startswith('--') else 40
passes = [e['pass'] for e in timings['passes']]
order = {p: i for i, p in enumerate(['(before the first pass)'] + passes + ['(after the last pass)'])}
named, shape, listed = g.passes_by_encoder(exports / 'metal-application-encoders-list.xml', 'Tertius.Demo', passes)
rows = []; first = {}; stamped = set()
for row in g.rows(exports / 'metal-gpu-intervals.xml'):
    if not g.of_process(row, 'Tertius.Demo'): continue
    buf = row['cmdbuffer-id'][0]; start = int(row['start'][1]); dur = int(row['duration'][1])
    enc = row['encoder-id'][0]; label = row['event-label'][0] or ''
    first[buf] = min(first.get(buf, start), start)
    found = named.get(enc)
    stamp = g.STAMP in label
    if found is not None or stamp: stamped.add(buf)
    rows.append((buf, start, start + dur, row['channel-name'][0], found if found else ('(stamp)' if stamp else None), enc))
ordered = sorted(stamped, key=lambda b: first[b]); n = len(shape)
frame_of = {b: at // n for at, b in enumerate(ordered)}
frames = defaultdict(list)
for r in rows:
    if r[0] in frame_of and r[4] is not None: frames[frame_of[r[0]]].append(r)
work = defaultdict(list); idle = defaultdict(list); spans = []; encs = defaultdict(list)
for f in sorted(frames):
    if f < first_settled: continue
    rs = [r for r in frames[f]]
    ps = [r for r in rs if r[4] != '(stamp)']
    if not ps: continue
    s = min(r[1] for r in ps); e = max(r[2] for r in ps)
    cuts = sorted({s, e} | {t for r in rs for t in (r[1], r[2]) if s <= t <= e})
    w = defaultdict(float); idl = defaultdict(float)
    starts = sorted((r[1], r[4]) for r in rs)
    for a, b in zip(cuts, cuts[1:]):
        open_ = [r[4] for r in rs if r[1] <= a and r[2] >= b]
        real = [p for p in open_ if p != '(stamp)']
        if real:
            w[min(real, key=lambda p: order[p])] += (b - a) / 1e6
        elif open_:
            w['(stamp)'] += (b - a) / 1e6
        else:
            nxt = next((p for t, p in starts if t >= b), '(after the last pass)')
            idl[nxt] += (b - a) / 1e6
    spans.append((e - s) / 1e6)
    ec = defaultdict(set)
    for r in rs: ec[r[4]].add(r[5])
    for p in list(order) + ['(stamp)']:
        work[p].append(w.get(p, 0.0)); idle[p].append(idl.get(p, 0.0)); encs[p].append(len(ec.get(p, ())))
med = statistics.median
print('frames %d, span median %.3f ms, mean %.3f ms' % (len(spans), med(spans), statistics.mean(spans)))
print('%-30s %9s %9s %9s %9s' % ('pass', 'work', 'idle', 'pays', 'encoders'))
tw = ti = 0.0; out = {}
for p in list(order) + ['(stamp)']:
    if not work[p]: continue
    a = statistics.mean(work[p]); b = statistics.mean(idle[p]); tw += a; ti += b
    out[p] = {'work': a, 'idle': b, 'encoders': med(encs[p])}
    if a + b > 0.0005: print('%-30s %9.3f %9.3f %9.3f %9d' % (p, a, b, a + b, med(encs[p])))
print('%-30s %9.3f %9.3f %9.3f   (means over the settled frames)' % ('sum', tw, ti, tw + ti))
if '--json' in sys.argv:
    Path(sys.argv[sys.argv.index('--json') + 1]).write_text(json.dumps({'span_median': med(spans), 'span_mean': statistics.mean(spans), 'passes': out}, indent=1))
