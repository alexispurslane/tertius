# spanchart.py <out.png> <spans.csv>: a line chart of the GPU frame span over the film rect-bay's 180 frames, one line per
# side (M, X, Xt, D4, D8, D4s, D8c), each frame's median over that side's runs in the record's spans.csv (columns
# <round>-<side>, written by timing-table.py --csv from the scratch switch TERTIUS_X_SPAN_DUMP). C4, Xtc and D4c stay in
# the tables only, to keep the chart at seven lines. Colors: slots 1 to 7 of the dataviz skill's categorical palette,
# checked with its validator on the light surface. Each line carries a direct label at its right end, because three
# colors sit below 3:1 contrast on the surface. PIL only, because the dev shell has no matplotlib.
import csv
import statistics
import sys
from PIL import Image, ImageDraw, ImageFont

SIDES = [('M', '#2a78d6'), ('X', '#eb6834'), ('Xt', '#1baf7a'), ('D4', '#eda100'), ('D8', '#e87ba4'), ('D4s', '#008300'),
         ('D8c', '#6250d6')]
SURFACE, INK, MUTED, GRID = '#fcfcfb', '#1f1f1e', '#6b6a64', '#e4e3df'
W, H = 1400, 720
LEFT, RIGHT, TOP, BOTTOM = 90, 120, 80, 70
Y_MAX = 40.0

out, table = sys.argv[1], sys.argv[2]


def font(size):
    for name in ('/System/Library/Fonts/Supplemental/Arial.ttf', '/System/Library/Fonts/Helvetica.ttc'):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


with open(table) as f:
    rows = list(csv.DictReader(f))
series = {}
counts = {}
for side, _ in SIDES:
    columns = [c for c in rows[0] if c != 'frame' and c.split('-', 1)[1] == side]
    counts[side] = len(columns)
    series[side] = [statistics.median(float(row[c]) for c in columns) for row in rows]

im = Image.new('RGB', (W, H), SURFACE)
d = ImageDraw.Draw(im)
plot_w, plot_h = W - LEFT - RIGHT, H - TOP - BOTTOM
frames = max(len(v) for v in series.values())


def xy(i, ms):
    return LEFT + plot_w * i / (frames - 1), TOP + plot_h * (1 - min(ms, Y_MAX) / Y_MAX)


small, label, title = font(15), font(17), font(22)
for ms in range(0, int(Y_MAX) + 1, 5):
    _, y = xy(0, ms)
    d.line([(LEFT, y), (LEFT + plot_w, y)], fill=GRID, width=1)
    d.text((LEFT - 10, y), f'{ms}', fill=MUTED, font=small, anchor='rm')
for f in range(0, frames, 30):
    x, _ = xy(f, 0)
    d.text((x, TOP + plot_h + 12), f'{f}', fill=MUTED, font=small, anchor='mt')
d.text((LEFT + plot_w / 2, H - 22), 'film frame', fill=MUTED, font=small, anchor='mm')
d.text((24, TOP + plot_h / 2), 'ms', fill=MUTED, font=small, anchor='mm')
d.text((LEFT, 24), 'GPU frame span over the film rect-bay, 1920 x 1080, two frames in flight', fill=INK, font=title)
d.text((LEFT, 52), 'Each frame: the median over the side\'s timing runs (' + ', '.join(f'{s} {counts[s]}' for s, _ in SIDES)
       + '). Lines above 40 ms are cut at 40.',
       fill=MUTED, font=small)
ends = []
for side, color in SIDES:
    points = [xy(i, v) for i, v in enumerate(series[side])]
    d.line(points, fill=color, width=2, joint='curve')
    ends.append([points[-1][1], side, color])
# Direct labels at the right end, pushed apart by at least 20 px so that none overlap.
ends.sort()
for i in range(1, len(ends)):
    ends[i][0] = max(ends[i][0], ends[i - 1][0] + 20)
for y, side, color in ends:
    x = LEFT + plot_w + 10
    d.line([(x, y), (x + 14, y)], fill=color, width=4)
    d.text((x + 20, y), side, fill=INK, font=label, anchor='lm')
im.save(out)
print(out, im.size)
