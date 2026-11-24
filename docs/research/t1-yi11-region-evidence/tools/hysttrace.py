# Tool code of t1-yi11. hysttrace.py <folder> | check <folder>...: checks the hyst rule of build yj (scratch-yj.patch, probeHystDecisionOf)
# through the shader. The folder holds a frozen bay run of build yj with TERTIUS_X_REGION_CLOSED=hyst,
# TERTIUS_X_HYST_TRACE=1 and TERTIUS_X_REGION_LOG (60 draws: slot = draw index for draws 0 to 71). In trace mode the
# shader takes t18, the gate and today's signed answer from the fixed table probeTraceOf at slots 40 to 46; every other
# anchor-draw is closed with t18 = 0. This tool holds its own copy of the table and of the rule, computes every anchor's
# signed decision at every draw, and compares it with the ring log's b (the stored signed answer) on the whole grid of
# anchors. It prints each case of the table with the expected and the logged answer, and exits 1 on any difference.
# "check <folder>..." checks real takes of the hx column (TERTIUS_X_REGION_CLOSED=hyst, with the ring log): for every
# anchor-draw whose gate is closed (cause age >= 128), it recomputes the rule from the logged t18 and tested flag
# (region-t.log) and the logged signed answers of the draw before (region-ring.log), and compares with the logged
# answer; for every anchor-draw whose gate is open, it checks that the answer is -1, 0 or 1. It prints the counts by case.
import sys
from pathlib import Path

import numpy as np

SEED, CONFIRM = 5.0, 2.6
# slot -> {(x, y): (kind, value)}: kind 1 a closed gate with t18 = value, kind 2 an open gate whose today's test answers
# with the sign value. A copy of probeTraceOf, with the case each entry checks.
TABLE = {
    40: {(2, 2): (1, 5.5), (10, 5): (1, -6.0), (5, 9): (2, -1.0), (8, 9): (2, 0.0), (0, 0): (1, 5.5), (19, 11): (1, -5.5)},
    41: {(2, 2): (1, 3.0), (3, 2): (1, 3.0), (11, 5): (1, -3.0), (9, 5): (1, 3.0), (10, 6): (1, -2.0), (6, 9): (1, -3.0),
         (4, 9): (1, 3.0), (9, 9): (1, 3.0), (1, 1): (1, 3.0), (18, 10): (1, -3.0)},
    42: {(4, 2): (1, 3.0), (12, 5): (1, -3.0)},
    43: {(5, 2): (1, 3.0), (15, 8): (1, -5.2)},
    44: {(6, 2): (1, 3.0), (8, 2): (1, 3.0)},
    46: {(7, 2): (1, 3.0)},
}
CASES = [
    (40, (2, 2), 'isolated + strong seed'), (40, (10, 5), 'isolated - strong seed (-6.0)'),
    (43, (15, 8), 'isolated - strong seed (-5.2)'), (40, (0, 0), '+ seed in the first corner'),
    (40, (19, 11), '- seed in the last corner'), (40, (5, 9), 'open gate, today\'s test answers -'),
    (40, (8, 9), 'open gate, no answer'),
    (41, (2, 2), 'self persistence: weak +3.0 after its own + firing'), (41, (3, 2), 'weak hop 1 (+)'),
    (42, (4, 2), 'weak hop 2 (+) from a confirmed weak anchor'), (43, (5, 2), 'weak hop 3 (+)'),
    (44, (6, 2), 'weak hop 4 (+)'), (44, (8, 2), 'weak + with no fired neighbor'),
    (46, (7, 2), 'weak + two draws after the neighbor fired (none in the draw before)'),
    (41, (11, 5), 'weak hop (-) from a - seed'), (42, (12, 5), 'weak hop 2 (-)'),
    (41, (9, 5), 'weak + beside a - seed (opposite sign)'), (41, (10, 6), '-2.0 beside a - seed (under the weak bar)'),
    (41, (6, 9), 'weak - seeded by the open gate\'s - answer'), (41, (4, 9), 'weak + beside the open gate\'s - answer'),
    (41, (9, 9), 'weak + beside an open gate without answer'), (41, (1, 1), 'diagonal weak hop (+) from the first corner'),
    (41, (18, 10), 'diagonal weak hop (-) to the last corner\'s neighbor'),
]


def read_log(path):
    raw, out, at = path.read_bytes(), {}, 0
    while at < len(raw):
        draw, index, w, h = np.frombuffer(raw, np.int32, 4, at)
        at += 16
        out[int(draw)] = (int(index), np.frombuffer(raw, np.float32, w * h * 4, at).reshape(h, w, 4))
        at += w * h * 16
    return out


if sys.argv[1] == 'check':
    failed = 0
    for folder in map(Path, sys.argv[2:]):
        ring, stat = read_log(folder / 'region-ring.log'), read_log(folder / 'region-t.log')
        counts = dict.fromkeys(['closed', 'seed+', 'seed-', 'weak fired+', 'weak fired-', 'weak held', 'under bar',
                                'untested', 'open', 'open answers+', 'open answers-', 'differences', 'bad open'], 0)
        previous_draw = None
        for draw in sorted(ring):
            index, r = ring[draw]
            t18, tested = stat[draw][1][..., 0], stat[draw][1][..., 2] != 0
            answer, age = r[..., 2], np.mod(r[..., 3], 8192)
            continues = previous_draw is not None and previous_draw == draw - 1 and index > 0
            before = ring[draw - 1][1][..., 2] if continues else np.zeros_like(answer)
            h, w = answer.shape
            for y in range(h):
                for x in range(w):
                    if age[y, x] < 128:
                        counts['open'] += 1
                        a = answer[y, x]
                        counts['open answers+'] += a > 0
                        counts['open answers-'] += a < 0
                        counts['bad open'] += a not in (-1.0, 0.0, 1.0)
                        continue
                    counts['closed'] += 1
                    t = float(t18[y, x])
                    if not tested[y, x]:
                        want = 0.0
                        counts['untested'] += 1
                    elif abs(t) >= SEED:
                        want = float(np.sign(t))
                        counts['seed+' if t > 0 else 'seed-'] += 1
                    elif abs(t) >= CONFIRM and continues:
                        sgn = float(np.sign(t))
                        near = before[max(y - 1, 0):y + 2, max(x - 1, 0):x + 2]
                        want = sgn if (sgn * near > 0).any() else 0.0
                        counts['weak held' if want == 0 else 'weak fired+' if sgn > 0 else 'weak fired-'] += 1
                    else:
                        want = 0.0
                        counts['under bar'] += 1
                    counts['differences'] += want != answer[y, x]
            previous_draw = draw
        print(folder.name, {k: int(v) for k, v in counts.items()})
        failed += counts['differences'] + counts['bad open']
    sys.exit(1 if failed else 0)

log = read_log(Path(sys.argv[1]) / 'region-ring.log')
h, w = next(iter(log.values()))[1].shape[:2]
expected = {}
previous = np.zeros((h, w))
for draw in sorted(log):
    index, _ = log[draw]
    slot = index % 72
    table = TABLE.get(slot, {}) if index < 72 else {}
    now = np.zeros((h, w))
    for y in range(h):
        for x in range(w):
            kind, value = table.get((x, y), (0, 0.0))
            if kind == 2:
                now[y, x] = value
            elif kind == 1:
                size = abs(value)
                if size >= SEED:
                    now[y, x] = np.sign(value)
                elif size >= CONFIRM and index > 0:
                    s = np.sign(value)
                    near = previous[max(y - 1, 0):y + 2, max(x - 1, 0):x + 2]
                    now[y, x] = s if (s * near > 0).any() else 0.0
    expected[draw] = now
    previous = now
bad = [(d, int(x), int(y), expected[d][y, x], log[d][1][y, x, 2]) for d in sorted(log)
       for y, x in zip(*np.nonzero(expected[d] != log[d][1][..., 2]))]
print(f'{len(log)} draws, {h} x {w} anchors, draw index = draw: {all(log[d][0] == d for d in log)}')
print('| draw | anchor | case | expected | logged |')
print('|---:|---|---|---:|---:|')
for slot, (x, y), case in CASES:
    print(f'| {slot} | ({x}, {y}) | {case} | {expected[slot][y, x]:+.0f} | {log[slot][1][y, x, 2]:+.0f} |')
nonzero = sum(int((log[d][1][..., 2] != 0).sum()) for d in log)
print(f'logged nonzero answers: {nonzero}; differences from the model on the whole grid: {len(bad)}')
for row in bad[:20]:
    print('  difference', row)
sys.exit(1 if bad else 0)
