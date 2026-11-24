# Tool code of t1-yi11. regionlog.py <folder> [after draw] [render width] [render height]: the anchors' causes and answers
# from <folder>/region-ring.log, which build yg writes under TERTIUS_X_REGION_LOG (scratch-yg.patch). Each record is one
# draw's ring layer: r the region's light, g the valid draws, b the answer (1 where the light moved), a the cause age plus
# 8192 times the count of the region's counted taps that bring a cause. Only the anchors that the render size covers
# count (default 1280 x 720: 160 x 90 blocks of 8 px, anchors every 8 blocks, 20 x 12 anchors).
# It prints:
#   - the anchor-draws after <after draw> (default 128) whose region has a tap with a cause: their count, and each one's
#     draw, anchor and tap count (at most 40 lines);
#   - per draw, a line where any anchor holds a cause or answers: the anchors with a cause, the median, 90 % and largest
#     tap count among them, the anchors whose cause age is under 36 and under 128, and the anchors that answer;
#   - the total of answers after <after draw>, and the rate: yes answers per tested anchor-draw (an anchor with 36 valid
#     draws in a row, which the test needs) after <after draw>;
#   - the answer runs after <after draw>: an anchor that answers yes in a draw after one in which it did not. Each run of
#     an answer that acts cuts the bounce light's history in its region's blocks, which a still shows as a patch.
# It writes <folder>/region-summary.json with the per-draw rows.
import json
import struct
import sys
from pathlib import Path

import numpy as np

folder = Path(sys.argv[1])
after = int(sys.argv[2]) if len(sys.argv) > 2 else 128
width = int(sys.argv[3]) if len(sys.argv) > 3 else 1280
height = int(sys.argv[4]) if len(sys.argv) > 4 else 720
ax = ((width + 7) // 8 - 1) // 8 + 1
ay = ((height + 7) // 8 - 1) // 8 + 1
data = (folder / 'region-ring.log').read_bytes()
rows, late, offset, answers_late = [], [], 0, 0
before, runs = None, []
while offset < len(data):
    draw, index, w, h = struct.unpack_from('<4i', data, offset)
    offset += 16
    ring = np.frombuffer(data, '<f4', w * h * 4, offset).reshape(h, w, 4)[:ay, :ax]
    offset += w * h * 16
    count = np.floor(ring[..., 3] / 8192.0)
    age = ring[..., 3] - 8192.0 * count
    moved = ring[..., 2] != 0
    held = count > 0
    sizes = count[held]
    row = {'draw': draw, 'index': index, 'causeAnchors': int(held.sum()),
           'median': float(np.median(sizes)) if sizes.size else 0.0,
           'p90': float(np.percentile(sizes, 90)) if sizes.size else 0.0,
           'largest': float(sizes.max()) if sizes.size else 0.0,
           'openAt36': int((age < 36).sum()), 'openAt128': int((age < 128).sum()), 'answers': int(moved.sum()),
           'valid': int((ring[..., 1] > 0).sum())}
    rows.append(row)
    tested = ring[..., 1] >= 36
    row['tested'] = int(tested.sum())
    row['testedAnswers'] = int((moved & tested).sum())
    if draw > after:
        # A run: an anchor that answers yes in a draw after one in which it did not (or the first draw counted).
        starts = moved & ~before if before is not None else moved
        for y, x in zip(*np.nonzero(starts)):
            runs.append((draw, int(x), int(y)))
    before = moved
    if draw > after:
        answers_late += row['answers']
        for y, x in zip(*np.nonzero(held)):
            late.append((draw, int(x), int(y), int(count[y, x])))
print(f'{folder}: {len(rows)} draws, {ax} x {ay} anchors')
print(f'anchor-draws after draw {after} with a cause: {len(late)}')
for draw, x, y, n in late[:40]:
    print(f'  draw {draw} anchor ({x}, {y}) taps {n}')
print('| draw | anchors with a cause | median taps | 90 % | largest | cause age < 36 | < 128 | answers | valid |')
print('|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
for r in rows:
    if r['causeAnchors'] or r['answers']:
        print(f"| {r['draw']} | {r['causeAnchors']} | {r['median']:.0f} | {r['p90']:.0f} | {r['largest']:.0f} | "
              f"{r['openAt36']} | {r['openAt128']} | {r['answers']} | {r['valid']} |")
print(f'answers after draw {after}: {answers_late}')
tested = sum(r['tested'] for r in rows if r['draw'] > after)
yes = sum(r['testedAnswers'] for r in rows if r['draw'] > after)
print(f'tested anchor-draws after draw {after} (36 valid draws in a row): {tested}, yes answers among them: {yes}, '
      f'rate {yes / tested if tested else float("nan"):.3g}')
print(f'answer runs after draw {after} (an anchor that answers after a draw without an answer): {len(runs)}')
for draw, x, y in runs[:40]:
    print(f'  run from draw {draw} at anchor ({x}, {y})')
(folder / 'region-summary.json').write_text(json.dumps(rows))
