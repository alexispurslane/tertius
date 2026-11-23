# Tool code of t0-5tyj. boilchart.py <out.png> <label|switch result|size log or "fixed">...: the switch cabin's light per
# draw beside each draw's render size, to see whether the "boil" (the light rising and falling after the lamps go off)
# follows the changes of the render size. A switch result is the survey's results/switch/cabin.json: per film frame t
# (draw 33 + t) the 3 x 3 regions' mean light of the switch run and of its two controls, "off" (lamps off all run) and
# "on". The size log is the scratch patch's log of the switch run (one line per draw), or "fixed" for a run at the fixed
# scale 2/3. One panel per run: the center region r11 (red) and the whole image (black) of the switch run as % of its own
# control in the same lamp state ("off" from frame 160, "on" from 360), and a grey tick under each draw whose render size
# differs from the draw before. It prints per run, over the 200 draws after off and the 100 draws after on: the mean
# absolute change of the whole image's % from one draw to the next, on draws where the size changed and on draws where
# it did not, and the count of each.
import json
import sys
from PIL import Image, ImageDraw

out, specs = sys.argv[1], [s.split('|') for s in sys.argv[2:]]
W, PH, L, T0, T1, STILL, LO, HI = 1300, 200, 70, 140, 459, 33, 60, 160
im = Image.new('RGB', (W, PH * len(specs) + 40), 'white')
d = ImageDraw.Draw(im)
x = lambda t: L + (t - T0) / (T1 - T0) * (W - L - 20)
for n, (label, result, size_log) in enumerate(specs):
    series = json.load(open(result))['series']
    control = lambda t: series['off' if 160 <= t < 360 else 'on'][t]
    whole = [100 * series['switch'][t][0] / control(t)[0] for t in range(T1 + 1)]
    center = [100 * series['switch'][t][5] / control(t)[5] for t in range(T1 + 1)]
    if size_log == 'fixed':
        changed = [False] * (T1 + 1)
    else:
        sizes = [tuple(l.split()[1:3]) for l in open(size_log)]
        changed = [sizes[STILL + t] != sizes[STILL + t - 1] for t in range(T1 + 1)]
    top = n * PH
    y = lambda v: top + 24 + (HI - max(LO, min(HI, v))) / (HI - LO) * (PH - 50)
    for v in range(LO, HI + 1, 20):
        d.line([(L, y(v)), (W - 20, y(v))], fill=(225, 225, 225) if v != 100 else (150, 150, 150))
        d.text((8, y(v) - 6), f'{v} %', fill='black')
    for t in (160, 360):
        d.line([(x(t), top + 24), (x(t), top + PH - 26)], fill=(120, 120, 120))
    for t in range(T0, T1 + 1):
        if changed[t]:
            d.line([(x(t), top + PH - 24), (x(t), top + PH - 14)], fill=(150, 150, 150))
    d.line([(x(t), y(whole[t])) for t in range(T0, T1 + 1)], fill=(30, 30, 30), width=2)
    d.line([(x(t), y(center[t])) for t in range(T0, T1 + 1)], fill=(200, 60, 60), width=2)
    d.text((L, top + 6), f'{label}: switch run as % of its own control (black: whole image, red: center r11). Ticks: '
                         f'render size changed. Lines: lamps off at 160, on at 360.', fill='black')
    cells = []
    for span, (a, b) in (('200 after off', (161, 359)), ('100 after on', (361, 459))):
        steps = [(abs(whole[t] - whole[t - 1]), changed[t]) for t in range(a, b + 1)]
        on = [s for s, c in steps if c]
        off = [s for s, c in steps if not c]
        mean = lambda v: f'{sum(v) / len(v):.2f}' if v else 'n/a'
        cells.append(f'{span}: |change| {mean(on)} % on {len(on)} size-change draws, {mean(off)} % on {len(off)} others')
    print(f'{label}: ' + '; '.join(cells))
d.text((L, PH * len(specs) + 12), 'film frame 140 to 459 (draw = 33 + frame)', fill='black')
im.save(out)
print(out, im.size)
