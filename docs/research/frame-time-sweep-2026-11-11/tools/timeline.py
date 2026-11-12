"""Prints the GPU timeline of settled frames of a trace: each interval with its channel, pass and encoder, the idle time
inside the frame span, and how many encoders a frame holds. Usage: timeline.py <export dir> <gpu-timings.json> [frame]"""
import importlib.util, json, sys, statistics
from collections import defaultdict
from pathlib import Path
W = '/Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0/tools'
sys.path.insert(0, W)
spec = importlib.util.spec_from_file_location('gputrace', W + '/gpu-trace.py'); g = importlib.util.module_from_spec(spec); spec.loader.exec_module(g)
exports = Path(sys.argv[1]); timings = json.loads(Path(sys.argv[2]).read_text())
passes = [e['pass'] for e in timings['passes']]
named, shape, listed = g.passes_by_encoder(exports / 'metal-application-encoders-list.xml', 'Tertius.Demo', passes)
rows = []
first = {}; stamped = set()
for row in g.rows(exports / 'metal-gpu-intervals.xml'):
    if not g.of_process(row, 'Tertius.Demo'):
        continue
    buf = row['cmdbuffer-id'][0]; start = int(row['start'][1]); dur = int(row['duration'][1])
    enc = row['encoder-id'][0]; label = row['event-label'][0] or ''; chan = row['channel-name'][0]
    first[buf] = min(first.get(buf, start), start)
    found = named.get(enc)
    if found is not None or g.STAMP in label:
        stamped.add(buf)
    rows.append((buf, start, dur, chan, found, label, enc, row.get('state', (None, None))[0], row.get('start-latency', (None, None))[1]))
ordered = sorted(stamped, key=lambda b: first[b])
n = len(shape)
frame_of = {b: at // n for at, b in enumerate(ordered)}
frames = defaultdict(list)
for r in rows:
    if r[0] in frame_of:
        frames[frame_of[r[0]]].append(r)
def stats(f):
    rs = [r for r in frames[f] if r[4] is not None]  # pass encoders only
    allr = frames[f]
    if not rs: return None
    s = min(r[1] for r in rs); e = max(r[1] + r[2] for r in rs)
    u = g.union([(r[1], r[1] + r[2]) for r in allr if r[1] + r[2] > s and r[1] < e])
    busy = sum(min(b, e) - max(a, s) for a, b in u)
    return s, e, busy, len({r[6] for r in allr}), len(allr)
out = []
for f in sorted(frames):
    st = stats(f)
    if st: out.append((f, (st[1] - st[0]) / 1e6, (st[1] - st[0] - st[2]) / 1e6, st[3], st[4]))
settled = [o for o in out if o[0] >= 40]
print('frames', len(out), 'settled', len(settled))
print('span median %.3f  idle-inside-span median %.3f ms  encoders/frame median %d  intervals/frame median %d' % (
    statistics.median(o[1] for o in settled), statistics.median(o[2] for o in settled),
    statistics.median(o[3] for o in settled), statistics.median(o[4] for o in settled)))
pick = int(sys.argv[3]) if len(sys.argv) > 3 else settled[len(settled) // 2][0]
rs = sorted(frames[pick], key=lambda r: r[1])
s = min(r[1] for r in rs if r[4] is not None)
print('frame', pick)
last_end = s
for r in rs:
    gap = (r[1] - last_end) / 1e6
    print('%8.3f +%7.3f  %-9s %-28s %s  %s' % ((r[1] - s) / 1e6, r[2] / 1e6, r[3], r[4] or '-', r[5][:60], ('gap %.3f' % gap) if gap > 0.02 else ''))
    last_end = max(last_end, r[1] + r[2])
