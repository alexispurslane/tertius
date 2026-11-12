"""breaks.py <stamped run folder> [<other run folder>] [--frame N]: the Metal encoders of one frame, by pass, from the
log of the patched MoltenVK (MVK_SWEEP_LOG). A run folder is <name>, with its log beside it as <name>.log and its
gpu-timings.json inside.

The stamped run has a time stamp after every pass, so its stamps name the passes. With a second run (for example one
without pass time stamps), the tool walks the same frame of both logs in step: both hold the same Vulkan calls in the same
order, so each call of the second run takes the pass of its twin. The table then gives the second run's encoders.

Columns: the pass's dispatches (direct and indirect), its barrier calls outside a render pass, how many of those kept the
open compute encoder, and its new Metal encoders by kind:
  compute after a barrier   a barrier call ended a compute encoder, and the next dispatch started a new one
  compute, first            the first dispatch after a render pass, a blit, a clear or a time stamp
  render                    a render pass (Metal has one encoder per render pass)
  clear                     vkCmdClearColorImage, which MoltenVK draws as a render pass per mip level
  blit                      a copy or a buffer update
  stamp                     the small blit encoder of a time stamp
"""
import json, sys
from collections import Counter, OrderedDict
from pathlib import Path

def events(folder):
    lines = [l.split('[sweep] ', 1)[1].strip() for l in open(str(folder) + '.log', errors='replace') if '[sweep] ' in l]
    # A barrier or clear call prints before it ends the encoder, and the end writes the pending stamps. So the call
    # belongs after the stamp lines that follow it directly.
    out = []; i = 0
    while i < len(lines):
        l = lines[i]
        if l.startswith(('barrier ', 'clear-image')):
            j = i + 1
            while j < len(lines) and lines[j] == 'encoder stamp': j += 1
            out.extend(lines[i + 1:j]); out.append(l); i = j
        else:
            out.append(l); i += 1
    return out

def frames_of(ev, frames):
    stamps = [i for i, l in enumerate(ev) if l == 'encoder stamp']
    per = len(stamps) // frames
    assert per * frames == len(stamps), (len(stamps), frames)
    cuts = [stamps[f * per] for f in range(frames)] + [len(ev)]
    return [ev[cuts[f]:cuts[f + 1]] for f in range(frames)], per

def is_key(l):  # a Vulkan call of the application, the same in every run of the script
    return l.startswith(('barrier ', 'clear-image')) or l in ('dispatch', 'encoder render pass', 'encoder render transfer-or-clear')

def key_kind(l):
    return l.split()[0] if l.startswith(('barrier ', 'clear-image')) else l

args = [a for a in sys.argv[1:] if not a.startswith('--')]
frame = int(sys.argv[sys.argv.index('--frame') + 1]) if '--frame' in sys.argv else 50
stamped = Path(args[0]); other = Path(args[1]) if len(args) > 1 else None
passes = [p['pass'] for p in json.loads((stamped / 'gpu-timings.json').read_text())['passes']]
FRAMES = 61  # deck.json: 33 draws of the still and 28 film frames
sf, per = frames_of(events(stamped), FRAMES)
assert per == len(passes) + 1, (per, len(passes))
# The pass of each line of the stamped frame: the stamps count the passes.
named = []; k = -1
for l in sf[frame]:
    if l == 'encoder stamp': k += 1
    # Stamp 0 opens the frame, and stamp k + 1 follows pass k. So the lines after stamp k belong to pass k.
    name = '(before the first pass)' if k < 0 else passes[k] if k < len(passes) else '(after the last pass)'
    named.append((l, name))
if other is None:
    walk = named
else:
    of, _ = frames_of(events(other), FRAMES)
    keys = [(key_kind(l), name) for l, name in named if is_key(l)]
    walk = []; at = 0; cur = '(before the first pass)'
    for l in of[frame]:
        if is_key(l):
            assert at < len(keys) and keys[at][0] == key_kind(l), (at, l, keys[at] if at < len(keys) else None)
            cur = keys[at][1]; at += 1
        walk.append((l, cur))
    assert at == len(keys), (at, len(keys))

rows = OrderedDict((p, Counter()) for p in ['(before the first pass)'] + passes + ['(after the last pass)'])
last = 'first'; pending_indirect = False
for l, name in walk:
    r = rows[name]
    if l.startswith('barrier '):
        m = dict(kv.split('=') for kv in l.split()[1:])
        r['barriers'] += 1; r['kept'] += int(m['kept'])
        if m['kept'] == '0' and m['open'] == 'dispatch':
            last = 'barrier'
            r['ended a compute encoder, dst compute only' if m['dst'] == '4' else 'ended a compute encoder, other dst'] += 1
    elif l == 'dispatch':
        r['dispatches'] += 1; pending_indirect = False
    elif l.startswith('clear-image'):
        last = 'clear'
    elif l.startswith('encoder '):
        kind = l.split()[1]
        if kind == 'stamp': col = 'stamp'
        elif kind == 'render': col = 'clear' if 'transfer-or-clear' in l else 'render'
        elif kind == 'blit': col = 'blit'
        else: col = 'compute after a barrier' if last == 'barrier' else 'compute, first'
        r[col] += 1; r['encoders'] += 1
        last = 'first'
cols = ['compute after a barrier', 'compute, first', 'render', 'clear', 'blit', 'stamp']
print(f"frame {frame} of {other.name if other else stamped.name}" + (f", passes named by {stamped.name}" if other else ''))
print('| pass | dispatches | barrier calls | kept | encoders | ' + ' | '.join(cols) + ' |')
print('|---|---:|---:|---:|---:|' + '---:|' * len(cols))
total = Counter()
for name, r in rows.items():
    for key, v in r.items(): total[key] += v
    if not (r['encoders'] or r['dispatches'] or r['barriers']): continue
    print(f"| {name} | {r['dispatches']} | {r['barriers']} | {r['kept']} | {r['encoders']} | " + ' | '.join(str(r[c]) for c in cols) + ' |')
print(f"| sum | {total['dispatches']} | {total['barriers']} | {total['kept']} | {total['encoders']} | " + ' | '.join(str(total[c]) for c in cols) + ' |')
print(f"barrier calls that ended a compute encoder: {total['ended a compute encoder, dst compute only']} with the compute stage as the only destination, {total['ended a compute encoder, other dst']} with another destination stage")
json.dump({'rows': {n: dict(r) for n, r in rows.items()}, 'total': dict(total)}, open(str(other or stamped) + '.breaks.json', 'w'), indent=1)
