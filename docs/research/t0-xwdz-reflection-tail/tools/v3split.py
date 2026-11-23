# Tool code of t0-xwdz. v3split.py <run folder>: the v3 log's three splits of one take, from the run's last reset on
# (scratch-tail-log.patch v3: tail.txt and cuts.txt). It prints Markdown tables:
#   1. per draw 1 to 9: where the lengths less than 1e-3 below an integer ("near below") come from. "mean low": the
#      reprojected length fell below the least weighted tap; "blend low": the blend added less than 1; "tiny cut": a cut
#      of at most 1e-3; "no sample": the blend added nothing; then the near-below lengths of the draw, and of them mean low,
#      tiny cut and inherited from the own texel. Counts of surface pixels.
#   2. draw 8's own reflection rays by the length that the pixel's own texel held after draw 7.
#   3. per draw 9 to 12: the cuts by their length before and after the cut, in bins of 1 draw.
#   4. with weights.txt (v4): per draw 3 to 8, the surface pixels by the weight share of the three taps other than the
#      heaviest, for all surface pixels and for those whose reprojected length fell below their own texel's.
import sys
from pathlib import Path

folder = Path(sys.argv[1])
rows = [list(map(int, line.split())) for line in (folder / 'tail.txt').read_text().splitlines()]
reset = max(i for i, r in enumerate(rows) if r[1] == 0)
by_draw = {r[1]: r[4:] for r in rows[reset:]}
print(f'## {folder.name}: reset at frame {rows[reset][0]}')
print()
print('| draw | mean low | blend low | tiny cut | no sample | near below | of them mean low | tiny cut | inherited | before in [7.99, 8) | after in [7.99, 8) from 8 or more |')
print('|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
for d in range(1, 10):
    c = by_draw[d]
    print(f'| {d} | {c[25]} | {c[26]} | {c[27]} | {c[28]} | {c[29]} | {c[30]} | {c[31]} | {c[32]} | {c[17]} | {c[18]} |')
print()
c = by_draw[8]
print('| draw 8 own rays | count |')
print('|---|---:|')
print(f'| all | {c[12]} |')
print(f'| own texel in [7.99, 8) | {c[21]} |')
print(f'| own texel in [7.5, 7.99) | {c[24]} |')
print(f'| own texel under 7.5 | {c[22] - c[24]} |')
print(f'| own texel 8 or more, a tap under 8 | {c[15]} |')
print(f'| every tap 8 or more, the mean under 8 | {c[14]} |')
print(f'| reprojected 8 or more (the lobe fit) | {c[13]} |')
print(f'| surface pixels with a length in [7.5, 8) after draw 7 | {by_draw[7][23]} |')
print()
cuts = {}
for line in (folder / 'cuts.txt').read_text().splitlines():
    head, after = line.split('|')
    head = list(map(int, head.split()))
    cuts.setdefault(head[1], []).append((head[0], head[2:], list(map(int, after.split()))))
frame0 = rows[reset][0]
print('| draw | side | ' + ' | '.join(f'[{k}, {k + 1})' for k in range(3, 13)) + ' | 13 or more |')
print('|---:|---|' + '---:|' * 11)
for d in range(9, 13):
    frame, before, after = next(x for x in cuts[d] if x[0] >= frame0)
    for side, bins in (('from', before), ('to', after)):
        print(f'| {d} | {side} | ' + ' | '.join(str(bins[k]) for k in range(3, 13)) + f' | {sum(bins[13:])} |')
weights = folder / 'weights.txt'
if weights.exists():
    print()
    names = ['exactly 0', 'under 1e-6', '[1e-6, 1e-5)', '[1e-5, 1e-4)', '[1e-4, 1e-3)', '[1e-3, 1e-2)', '1e-2 or more']
    print('| draw | pixels | ' + ' | '.join(names) + ' |')
    print('|---:|---|' + '---:|' * 7)
    for line in weights.read_text().splitlines():
        w = list(map(int, line.split()))
        if w[0] < frame0 or not 3 <= w[1] <= 8:
            continue
        print(f'| {w[1]} | all surface | ' + ' | '.join(str(v) for v in w[2:9]) + ' |')
        print(f'| {w[1]} | reprojected below own | ' + ' | '.join(str(v) for v in w[9:16]) + ' |')
