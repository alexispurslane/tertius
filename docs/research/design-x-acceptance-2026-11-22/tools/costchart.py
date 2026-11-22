# Tool code of t0-5tyj. costchart.py <out.png> <label|time log>...: the frame's GPU cost around the cabin's lamp switch,
# from the time logs of macdyn.sh cost (scratch-dynamic-scale.patch, TERTIUS_X_TIME_LOG: one line per collect,
# "<frame number> <GPU span> <before the output> <at the render size> <output>" in ms; a frame collected twice gives two
# equal lines, and the last line of a frame counts). Frame number d is film frame d - 33. The lamps go off at film
# frame 160 and come on at 360.
# The chart: per run, the GPU span per draw from 20 draws before off to 100 draws after on. It prints per run the mean
# span in four spans: settled (film frames 40 to 159, lamps on long since), 0 to 59 draws after off, 100 to 199 draws
# after off (settled off), 0 to 59 draws after on; and per side (the label up to its first space) the mean over its
# rounds. Per run also the peak span in the 100 draws after each switch, and the count of draws in those 100 that lie
# more than 1 ms above the run's settled span ("block"), with the first and last such draw after the switch. Then the
# excess of each other side (X, XB, XC) over M in each span, and per 10 draws after each switch, so that a cost of young
# histories shows as an excess that falls with the draws since the switch. Each side has one colour on the chart, and
# its later rounds a lighter shade.
import sys
from collections import defaultdict
from PIL import Image, ImageDraw

out, runs = sys.argv[1], [a.split('|') for a in sys.argv[2:]]
SPANS = {'settled on': range(40, 160), 'off 0-59': range(160, 220), 'off 100-199': range(260, 360),
         'on 0-59': range(360, 420)}
SIDE_COLOURS = {'M': (200, 60, 60), 'X': (40, 110, 200), 'XB': (40, 150, 60), 'XC': (200, 130, 20)}


def load(path):
    span = {}
    for line in open(path):
        f = line.split()
        span[int(f[0]) - 33] = float(f[1])
    return span


data = {label: load(path) for label, path in runs}
mean = lambda s, r: sum(s[k] for k in r if k in s) / max(1, sum(1 for k in r if k in s))
sides = defaultdict(list)
for label, s in data.items():
    sides[label.split()[0]].append(s)
    print(f'{label}: ' + ', '.join(f'{n} {mean(s, r):.2f} ms' for n, r in SPANS.items()))
    settled = mean(s, SPANS['settled on'])
    for name, start in (('off', 160), ('on', 360)):
        after = [k for k in range(start, start + 100) if k in s]
        block = [k - start for k in after if s[k] > settled + 1]
        print(f'    after {name}: peak {max(s[k] for k in after):.2f} ms, {len(block)} draws above settled + 1 ms'
              + (f' (draws {block[0]} to {block[-1]} after the switch)' if block else ''))
side_mean = {side: {n: sum(mean(s, r) for s in ss) / len(ss) for n, r in SPANS.items()} for side, ss in sides.items()}
for side, m in side_mean.items():
    print(f'{side} mean of rounds: ' + ', '.join(f'{n} {v:.2f} ms' for n, v in m.items()))
for other in [side for side in side_mean if side != 'M' and 'M' in side_mean]:
    print(f'{other} - M: ' + ', '.join(f'{n} {side_mean[other][n] - side_mean["M"][n]:+.2f} ms' for n in SPANS))
    for name, start in (('after off', 160), ('after on', 360)):
        cells = []
        for a in range(0, 100, 10):
            r = range(start + a, start + a + 10)
            x = sum(mean(s, r) for s in sides[other]) / len(sides[other])
            m = sum(mean(s, r) for s in sides['M']) / len(sides['M'])
            cells.append(f'{a}-{a + 9} {x - m:+.2f}')
        print(f'{other} - M per 10 draws {name} (ms): ' + ', '.join(cells))

W, H, L, T0, T1 = 1300, 560, 70, 140, 459
values = [v for s in data.values() for k, v in s.items() if T0 <= k <= T1]
lo, hi = min(values) * 0.95, max(values) * 1.05
im = Image.new('RGB', (W, H), 'white')
d = ImageDraw.Draw(im)
x = lambda k: L + (k - T0) / (T1 - T0) * (W - L - 20)
y = lambda v: 30 + (hi - v) / (hi - lo) * (H - 80)
step = max(0.5, round((hi - lo) / 8 * 2) / 2)
v = lo - lo % step
while v <= hi:
    d.line([(L, y(v)), (W - 20, y(v))], fill=(230, 230, 230))
    d.text((8, y(v) - 6), f'{v:.1f} ms', fill='black')
    v += step
for k in (160, 360):
    d.line([(x(k), 30), (x(k), H - 50)], fill=(140, 140, 140))
seen = defaultdict(int)
for i, (label, s) in enumerate(data.items()):
    side = label.split()[0]
    shade = 0.45 * min(seen[side], 2) / 2
    seen[side] += 1
    colour = tuple(int(c + (255 - c) * shade) for c in SIDE_COLOURS.get(side, (90, 90, 90)))
    d.line([(x(k), y(s[k])) for k in range(T0, T1 + 1) if k in s], fill=colour, width=1)
    d.text((W - 260, 36 + 16 * i), label, fill=colour)
d.text((L, 8), 'GPU span per draw (ms), the fair cabin switch at the fixed 2/3. Grey lines: lamps off (frame 160), on (360).',
       fill='black')
d.text((L, H - 24), 'film frame 140 to 459 (draw = 33 + frame)', fill='black')
im.save(out)
print(out, im.size)
