# Tool code of t0-5tyj (ticket t4f-71qp). dynscale.py <log folder>...: the render size and the region test of the switch
# cabin's switch run, from the logs of ../scratch-dynamic-scale.patch (size-<name>.txt: "<frame> <width> <height>
# <scale>" per draw) and ../scratch-region-log.patch (region-<name>.txt: "<draw> <draw index> <anchors> <tested>
# <fires> <row>" per draw). The switch run's name is 193-393: the lamps go off at draw 193 (film frame 160) and come on
# at draw 393 (film frame 360), and draw k of a log is its k-th line, counted from 0 (the run's first draw).
# Per span of draws:
#   changes:  draws whose render size differs from the draw before
#   longest:  the longest run of draws at one render size inside the span
#   runs>=36: the runs of at least 36 draws at one size inside the span (the region test needs 36 valid draws in a row)
#   scale:    the lowest and highest render scale in the span
#   tested:   tested anchor-draws over all anchor-draws (an anchor is tested when it holds 36 valid draws)
#   fires:    tested anchor-draws whose test found that the region's light moved, over the tested anchor-draws
import sys
from pathlib import Path

NAME = '193-393'
SPANS = (('before the switch', 36, 192), ('200 draws after off', 193, 392), ('100 draws after off', 193, 292),
         ('100 draws after on', 393, 492))


def runs_in(sizes, first, last):
    runs, start = [], first
    for d in range(first + 1, last + 2):
        if d > last or sizes[d] != sizes[d - 1]:
            runs.append(d - start)
            start = d
    return runs


for folder in map(Path, sys.argv[1:]):
    size_lines = [l.split() for l in (folder / f'size-{NAME}.txt').read_text().splitlines()]
    sizes = [(int(w), int(h)) for _, w, h, _ in size_lines]
    scales = [float(s) for *_, s in size_lines]
    region_path = folder / f'region-{NAME}.txt'
    region = [list(map(int, l.split())) for l in region_path.read_text().splitlines()] if region_path.exists() else []
    print(f'== {folder.name}: {len(sizes)} draws in the size log, {len(region)} in the region log, '
          f'sizes {sorted(set(sizes))[:1]} to {sorted(set(sizes))[-1:]} ({len(set(sizes))} different)')
    print(f"{'span':22s} {'draws':>9s} {'changes':>8s} {'longest':>8s} {'runs>=36':>9s} {'scale':>13s} {'tested':>8s} {'fires':>8s}")
    for label, first, last in SPANS:
        last = min(last, len(sizes) - 1)
        changes = sum(sizes[d] != sizes[d - 1] for d in range(max(first, 1), last + 1))
        runs = runs_in(sizes, first, last)
        tested = fires = cells = 0
        for row in region[first:last + 1]:
            cells += row[2]
            tested += row[3]
            fires += row[4]
        tested_share = f'{100 * tested / cells:7.1f}%' if cells else '     n/a'
        fire_share = f'{100 * fires / tested:7.1f}%' if tested else '     n/a'
        print(f'{label:22s} {first:4d}-{last:<4d} {changes:8d} {max(runs):8d} {sum(r >= 36 for r in runs):9d} '
              f'{min(scales[first:last + 1]):6.3f}-{max(scales[first:last + 1]):.3f} {tested_share} {fire_share}')
