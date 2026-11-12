"""idle.py <trace folder> [first settled frame, default 40]: the frame span, the idle time inside it and the encoder
count of each frame, from a recorded trace with or without pass time stamps. It needs no stamp encoder: a frame is a run
of the demo's command buffers, found from the repeating pattern of their encoder counts. Exports the two tables it
needs into <trace folder>/export, from <trace folder>/run.trace.
  span       from the first GPU interval of the frame's command buffers to the last one's end
  idle       the part of the span in which no GPU interval of the demo is open (other frames' intervals count as work)
  encoders   the Metal encoders of the frame's command buffers that have a GPU interval
  intervals  the GPU intervals of the frame's command buffers (a render encoder has one per channel)
Prints one line of medians and means over the settled frames, and writes <trace folder>/idle.json."""
import importlib.util, json, statistics, sys
from collections import Counter, defaultdict
from pathlib import Path
W = '/Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0/tools'
sys.path.insert(0, W)
spec = importlib.util.spec_from_file_location('gputrace', W + '/gpu-trace.py'); g = importlib.util.module_from_spec(spec); spec.loader.exec_module(g)
folder = Path(sys.argv[1]); settled_from = int(sys.argv[2]) if len(sys.argv) > 2 else 40
exports = folder / 'export'; exports.mkdir(exist_ok=True)
trace = folder / 'run.trace'
enc_xml = exports / 'metal-application-encoders-list.xml'; gpu_xml = exports / 'metal-gpu-intervals.xml'
if not enc_xml.exists(): g.export(trace, 'metal-application-encoders-list', exports)
if not gpu_xml.exists(): g.export(trace, 'metal-gpu-intervals', exports)
labels = {}
for row in g.rows(enc_xml):
    if g.of_process(row, 'Tertius.Demo'):
        labels[row['encoder-id'][0]] = row['encoder-label'][0] or ''
by_buffer = defaultdict(list)
for row in g.rows(gpu_xml):
    if not g.of_process(row, 'Tertius.Demo'): continue
    start = int(row['start'][1]); dur = int(row['duration'][1])
    by_buffer[row['cmdbuffer-id'][0]].append((start, start + dur, row['encoder-id'][0], row['channel-name'][0]))
buffers = sorted(by_buffer.items(), key=lambda kv: min(i[0] for i in kv[1]))
counts = [len({i[2] for i in ivs}) for _, ivs in buffers]
# A frame is a large command buffer and the smaller ones after it, up to the next large one: with FSR 3.1 the renderer
# submits a frame in two command buffers, and the first holds most passes. A buffer is large when it holds more encoders
# than the mean of all buffers.
limit = statistics.mean(counts)
frames = []
for (buf, ivs), count in zip(buffers, counts):
    if count > limit: frames.append([(buf, ivs)])
    elif frames: frames[-1].append((buf, ivs))
period = statistics.median(len(f) for f in frames)
frames = [f for f in frames if len(f) == period]
everything = sorted((a, b) for _, ivs in buffers for a, b, _, _ in ivs)
out = []
for f in frames:
    ivs = [i for _, group in f for i in group]
    s = min(i[0] for i in ivs); e = max(i[1] for i in ivs)
    u = g.union([(a, b) for a, b in everything if b > s and a < e])
    busy = sum(min(b, e) - max(a, s) for a, b in u)
    encs = {i[2] for i in ivs}
    kinds = Counter()
    for enc in encs:
        label = labels.get(enc, '')
        kinds['stamp' if g.STAMP in label else 'compute' if 'Compute' in label else 'render' if 'Render' in label else 'blit' if 'Blit' in label else 'other'] += 1
    # Each gap goes to the kind of the encoder that starts at its end, as pays.py gives idle time to the next pass.
    def kind_of(enc):
        label = labels.get(enc, '')
        return 'stamp' if g.STAMP in label else 'compute' if 'Compute' in label else 'render' if 'Render' in label else 'blit' if 'Blit' in label else 'other'
    gaps = Counter(); gap_counts = Counter(); cursor = s
    for a, b, enc, _ in sorted(ivs):
        if a > cursor:
            # Only the part of the gap in which no interval of another frame is open counts as idle.
            covered = sum(min(y, a) - max(x, cursor) for x, y in u if y > cursor and x < a)
            if a - cursor - covered > 0:
                gaps[kind_of(enc)] += (a - cursor - covered) / 1e6; gap_counts[kind_of(enc)] += 1
        cursor = max(cursor, b)
    out.append(dict(span=(e - s) / 1e6, idle=(e - s - busy) / 1e6, encoders=len(encs), intervals=len(ivs), kinds=dict(kinds), gaps=dict(gaps), gapCounts=dict(gap_counts)))
settled = out[settled_from:] if len(out) > settled_from + 10 else out[len(out) // 2:]
med = lambda k: statistics.median(o[k] for o in settled)
mean = lambda k: statistics.mean(o[k] for o in settled)
kinds = Counter()
for o in settled:
    for k, v in o['kinds'].items(): kinds[k] += v
per = {k: round(v / len(settled), 1) for k, v in sorted(kinds.items())}
gaps = Counter(); gap_counts = Counter()
for o in settled:
    for k, v in o['gaps'].items(): gaps[k] += v
    for k, v in o['gapCounts'].items(): gap_counts[k] += v
idle_by = {k: round(v / len(settled), 3) for k, v in sorted(gaps.items())}
gaps_by = {k: round(v / len(settled), 1) for k, v in sorted(gap_counts.items())}
res = dict(frames=len(out), settled=len(settled), buffersPerFrame=period, spanMedian=med('span'), spanMean=mean('span'),
           idleMedian=med('idle'), idleMean=mean('idle'), encodersMedian=med('encoders'), intervalsMedian=med('intervals'), encodersByKind=per, idleBeforeKind=idle_by, gapsBeforeKind=gaps_by)
(folder / 'idle.json').write_text(json.dumps(res, indent=1))
print(f"{folder.name}: frames {len(out)} (settled {len(settled)}, {period} buffers each)  span median {res['spanMedian']:.3f} mean {res['spanMean']:.3f} ms  "
      f"idle median {res['idleMedian']:.3f} mean {res['idleMean']:.3f} ms  encoders {res['encodersMedian']:.0f}  intervals {res['intervalsMedian']:.0f}  by kind {per}\n    idle before each kind (ms per frame) {idle_by}  gaps {gaps_by}")
