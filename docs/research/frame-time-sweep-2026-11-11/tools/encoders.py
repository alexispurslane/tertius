"""Counts, for one settled frame, the encoders and GPU intervals of each pass, and the idle gap before each interval."""
import importlib.util, json, sys, statistics
from collections import defaultdict, OrderedDict
from pathlib import Path
W = '/Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0/tools'
sys.path.insert(0, W)
spec = importlib.util.spec_from_file_location('gputrace', W + '/gpu-trace.py'); g = importlib.util.module_from_spec(spec); spec.loader.exec_module(g)
exports = Path(sys.argv[1]); timings = json.loads(Path(sys.argv[2]).read_text())
passes = [e['pass'] for e in timings['passes']]
named, shape, listed = g.passes_by_encoder(exports / 'metal-application-encoders-list.xml', 'Tertius.Demo', passes)
rows = []; first = {}; stamped = set()
for row in g.rows(exports / 'metal-gpu-intervals.xml'):
    if not g.of_process(row, 'Tertius.Demo'): continue
    buf = row['cmdbuffer-id'][0]; start = int(row['start'][1]); dur = int(row['duration'][1])
    enc = row['encoder-id'][0]; label = row['event-label'][0] or ''
    first[buf] = min(first.get(buf, start), start)
    found = named.get(enc)
    if found is not None or g.STAMP in label: stamped.add(buf)
    rows.append((buf, start, dur, row['channel-name'][0], found if found else ('(stamp)' if g.STAMP in label else '(other)'), enc))
ordered = sorted(stamped, key=lambda b: first[b]); n = len(shape)
frame_of = {b: at // n for at, b in enumerate(ordered)}
frames = defaultdict(list)
for r in rows:
    if r[0] in frame_of: frames[frame_of[r[0]]].append(r)
# per-pass medians over settled frames: encoders, intervals, and the idle gap that precedes the pass's intervals
agg = defaultdict(lambda: defaultdict(list))
idle_total = []
for f in sorted(frames):
    if f < 40: continue
    rs = sorted(frames[f], key=lambda r: r[1])
    enc = defaultdict(set); ints = defaultdict(int); gaps = defaultdict(float)
    last_end = rs[0][1]; tot = 0.0
    for r in rs:
        enc[r[4]].add(r[5]); ints[r[4]] += 1
        gap = max(0, r[1] - last_end) / 1e6
        gaps[r[4]] += gap; tot += gap
        last_end = max(last_end, r[1] + r[2])
    idle_total.append(tot)
    for p in enc:
        agg[p]['enc'].append(len(enc[p])); agg[p]['ints'].append(ints[p]); agg[p]['gap'].append(gaps[p])
print('%-30s %8s %9s %12s' % ('pass', 'encoders', 'intervals', 'idle before (ms)'))
te = 0; tg = 0
for p in passes + ['(stamp)', '(other)']:
    if p in agg:
        e = statistics.median(agg[p]['enc']); i = statistics.median(agg[p]['ints']); gp = statistics.median(agg[p]['gap'])
        te += e; tg += gp
        print('%-30s %8d %9d %12.3f' % (p, e, i, gp))
print('sum of medians: encoders %d, idle %.3f ms; idle per frame median %.3f ms' % (te, tg, statistics.median(idle_total)))
