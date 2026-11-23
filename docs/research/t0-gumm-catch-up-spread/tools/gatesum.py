# Tool code of t0-gumm. gatesum.py <out prefix> [fair folder]: the gate's table, chart and strip (queue-sun.sh: the bay's sun
# off at draw 200 and on at 400, the cap against today's cliff), from fairmeasure.py's fair-off.json and fair-on.json and the
# own-ray logs. <prefix>-zoom.png: the floor (560, 700)-(1000, 1000) and a part of the right wall (1300, 160)-(1740, 460) at
# full size, at draws 4 and 8 after the sun on, cap | today. Per code and event it prints the slowest region's settle, the boil of draws 1 to 8 and 1 to 15, the grain of
# draws 1 to 15 (the mean of the floor and the wall), the same on the narrow crop, and the diffuse own rays of draws 0 to 15
# after the event above the settled mean before it (draws 150 to 199), per pixel of the render size.
# The chart <prefix>-chart.png: per event a row of four panes, grain and boil of draws 1 to 15, broad and narrow; black is
# today, red the cap. The strip <prefix>-strip.png: the floor and the right wall at draws 2 and 8 after each event, cap | today.
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

prefix = sys.argv[1]
root = Path(sys.argv[2] if len(sys.argv) > 2 else '/private/tmp/t0-gbgj/gumm-fair')
CODES = [('today', (30, 30, 30)), ('cap', (220, 60, 60))]
EVENTS = {'off': 200, 'on': 400}
GRAIN = ('floor', 'wall')
CROP = (500, 100, 1860, 1060)
data = {}
for code, _ in CODES:
    logs = sorted((root / f'sun-{code}').glob('ownrays-*.txt'))
    rays = np.loadtxt(logs[0], dtype=np.int64) if logs else None
    for ev, at in EVENTS.items():
        f = root / f'sun-{code}' / f'fair-{ev}.json'
        if not f.exists():
            continue
        r = json.loads(f.read_text())
        broad = [c for c in r['boil'] if c != 'narrow']
        row = {'settle': r['settleSlowest'], 'boil': np.mean([r['boil'][c] for c in broad], axis=0)[:15],
               'grain': np.mean([r['grain'][c] for c in GRAIN], axis=0)[1:16], 'nboil': np.array(r['boil']['narrow'][:15]),
               'ngrain': np.array(r['grain']['narrow'][1:16])}
        if rays is not None:
            pixels = rays[0, 2] * rays[0, 3]
            settled = rays[150:200, 7].mean()
            row['catchUp'] = np.clip(rays[at:at + 16, 7] - settled, 0, None).sum() / pixels
            row['peak'] = rays[at:at + 16, 7].max() / pixels
        data[code, ev] = row

print('| Event | Code | Settle | Boil 1-8 | Boil 1-15 | Grain 1-15 | Narrow boil 1-8 | Narrow grain 1-15 | Catch-up rays per pixel, draws 0-15 | Peak diffuse own-ray share |')
print('|' + '---|' * 10)
for ev in EVENTS:
    for code, _ in CODES:
        r = data.get((code, ev))
        if r is None:
            continue
        own = f"{r['catchUp']:.2f} | {100 * r['peak']:.1f} %" if 'catchUp' in r else 'n/a | n/a'
        settle = 'none' if r['settle'] is None else r['settle']
        print(f"| {ev} | {code} | {settle} | {r['boil'][:8].mean():.3f} | {r['boil'].mean():.3f} | {r['grain'].mean():.2f} | "
              f"{r['nboil'][:8].mean():.3f} | {r['ngrain'].mean():.2f} | {own} |")

PW, PH, M = 330, 170, 50
im = Image.new('RGB', (4 * (PW + M) + M, 2 * (PH + 50) + 40), 'white')
g = ImageDraw.Draw(im)
g.text((M, 6), 'The bay, the sun off at draw 200 and on at 400, constant scale 2/3: per draw 1-15 after the event. '
              'Black today\'s cliff, red the cap.', fill='black')
for i, ev in enumerate(EVENTS):
    for j, (key, title) in enumerate((('grain', 'grain %'), ('boil', 'boil %'), ('ngrain', 'narrow grain %'),
                                      ('nboil', 'narrow boil %'))):
        x0, y0 = M + j * (PW + M), 50 + i * (PH + 50)
        series = [(colour, data[code, ev][key]) for code, colour in CODES if (code, ev) in data]
        if not series:
            continue
        top = max(float(v.max()) for _, v in series) * 1.08
        g.rectangle([x0, y0, x0 + PW, y0 + PH], outline=(200, 200, 200))
        g.text((x0, y0 - 16), f'sun {ev}: {title}, draws 1-15', fill='black')
        for frac in (0.5, 1.0):
            yy = y0 + PH * (1 - frac)
            g.line([(x0, yy), (x0 + PW, yy)], fill=(230, 230, 230))
            g.text((x0 - 44, yy - 6), f'{top * frac:.2f}', fill='black')
        for colour, v in series:
            g.line([(x0 + k / 14 * PW, y0 + PH * (1 - val / top)) for k, val in enumerate(v)], fill=colour, width=2)
im.save(f'{prefix}-chart.png')

cw, ch = int((CROP[2] - CROP[0]) * 0.4), int((CROP[3] - CROP[1]) * 0.4)
cols = [(ev, d) for ev in EVENTS for d in (2, 8)]
strip = Image.new('RGB', (len(cols) * (cw + 8), 2 * (ch + 26)), 'white')
g = ImageDraw.Draw(strip)
for i, (ev, d) in enumerate(cols):
    for k, (code, _) in enumerate((('cap', 0), ('today', 0))):
        p = root / f'sun-{code}' / f'f-{EVENTS[ev] + d:06d}.png'
        if not p.exists():
            continue
        x, y = i * (cw + 8), k * (ch + 26)
        strip.paste(Image.open(p).convert('RGB').crop(CROP).resize((cw, ch), Image.LANCZOS), (x, y + 22))
        g.text((x + 4, y + 5), f'{code}, draw {d} after the sun {ev}, scale 2/3', fill='black')
strip.save(f'{prefix}-strip.png')
print(f'chart {prefix}-chart.png, strip {prefix}-strip.png')

zoom = Image.new('RGB', (2 * (880 + 12), 2 * (300 + 26)), 'white')
g = ImageDraw.Draw(zoom)
for i, code in enumerate(('cap', 'today')):
    for k, d in enumerate((4, 8)):
        p = root / f'sun-{code}' / f'f-{400 + d:06d}.png'
        if not p.exists():
            continue
        im = Image.open(p).convert('RGB')
        x, y = i * (880 + 12), k * (300 + 26)
        zoom.paste(im.crop((560, 700, 1000, 1000)), (x, y + 22))
        zoom.paste(im.crop((1300, 160, 1740, 460)), (x + 440, y + 22))
        g.text((x + 4, y + 5), f'{code}, draw {d} after the sun on: the floor | the right wall, full size', fill='black')
zoom.save(f'{prefix}-zoom.png')
