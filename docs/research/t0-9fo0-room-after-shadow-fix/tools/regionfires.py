# Tool code of t0-9fo0, part B. regionfires.py <log folder>...: how often the region's drift test fires, from the logs of
# scratch-region-log.patch (TERTIUS_X_REGION_LOG). Each log line is one draw: the draw since the process start, the draw
# index since the last reset, the anchors, the anchors with 36 valid draws in a row (the test runs only at those) and the
# anchors whose test found that the region's light moved (a fire). An anchor-draw is one anchor in one draw. The share is
# fires over the anchor-draws at which the test ran. In the switch film (region-193-393.txt, the run "switch" of the case
# "cabin"), film frame k is draw index 33 + k, as in the survey (STILL_DRAWS + k), and the windows are the frames before
# the lamps go off (0 to 159), the 100 draws after they go off (160 to 259), the rest of the dark time (260 to 359) and
# the 100 draws after they come on (360 to 459). Any other log is a still: one window over all its draws.
import sys
from pathlib import Path

WINDOWS = (('before the switch, frames 0 to 159', 0, 159), ('100 draws after off, frames 160 to 259', 160, 259),
           ('frames 260 to 359, lamps still off', 260, 359), ('100 draws after on, frames 360 to 459', 360, 459))


def rows(path):
    return [[int(v) for v in line.split()] for line in path.read_text().splitlines()]


def report(name, lines):
    tested = sum(r[3] for r in lines)
    fires = sum(r[4] for r in lines)
    firing = [r for r in lines if r[4]]
    share = f'{fires / tested:.4f}' if tested else 'no test ran'
    span = f', first / last draw index with a fire {firing[0][1]} / {firing[-1][1]}' if firing else ''
    print(f'  {name}: draws {len(lines)}, anchors {lines[0][2] if lines else 0}, anchor-draws tested {tested},'
          f' fires {fires}, share {share}, draws with a fire {len(firing)}{span}')


for folder in sys.argv[1:]:
    for path in sorted(Path(folder).glob('region-*.txt')):
        lines = rows(path)
        print(f'{path}')
        if path.name == 'region-193-393.txt':
            for name, first, last in WINDOWS:
                report(name, [r for r in lines if first <= r[1] - 33 <= last])
        else:
            report('the whole still', lines)
