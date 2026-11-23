# Tool code of t0-3vwx. walklogs.py <label|run folder>...: the walk's logs of a macdyn.sh picture run (queue-a.sh: w-M5,
# w-X5), per span of draws. The walk runs f 180 draws, g 120 and h 200, so the stop is at draw 333.
#   - history-none.txt (scratch-history-log.patch): "<draw - 1> <w> <h> <mean> <share < 8> <share <= 0>", the slow diffuse
#     history's mean length in draws and the shares of pixels under 8 draws and at 0, as the draw before left them; a
#     line written at the start of draw d holds draw d - 1's state, so this tool reads line d - 1 for draw d - 1.
#   - region-none.txt (scratch-region-log.patch, Design X only): "<draw> <draw index> <anchors> <tested> <fires> <row>";
#     tested = tested anchor-draws over all anchor-draws (an anchor is tested when it holds 36 valid draws at one render
#     size), fires = tested anchor-draws whose test found that the region's light moved, over the tested ones.
#   - size-none.txt: the render size changes inside the span.
import sys
from pathlib import Path
import numpy as np

SPANS = (('walk, last 60 draws', 273, 332), ('stop, draws 0 to 59 after', 333, 392), ('stop, draws 60 to 199 after', 393, 532))
for spec in sys.argv[1:]:
    label, folder = spec.split('|')
    folder = Path(folder)
    hist = {int(l.split()[0]): [float(v) for v in l.split()[3:6]] for l in open(folder / 'history-none.txt')}
    sizes = [tuple(l.split()[1:3]) for l in open(folder / 'size-none.txt')]
    region = {}
    if (folder / 'region-none.txt').exists():
        for l in open(folder / 'region-none.txt'):
            p = l.split()
            region[int(p[0])] = (int(p[2]), int(p[3]), int(p[4]))
    print(f'== {label} ({folder})')
    for name, first, last in SPANS:
        h = np.array([hist[d] for d in range(first, last + 1) if d in hist])
        changes = sum(sizes[d] != sizes[d - 1] for d in range(first, last + 1))
        line = (f'  {name} (draws {first}-{last}, {changes} size changes): history mean {h[:, 0].mean():.2f} draws, '
                f'share < 8 {h[:, 1].mean():.4f}, share 0 {h[:, 2].mean():.4f}')
        if region:
            a = sum(region[d][0] for d in range(first, last + 1) if d in region)
            t = sum(region[d][1] for d in range(first, last + 1) if d in region)
            f = sum(region[d][2] for d in range(first, last + 1) if d in region)
            line += f'; region tested {100 * t / a:.1f} % of {a} anchor-draws, fires {100 * f / max(t, 1):.1f} % of tested'
        print(line)
