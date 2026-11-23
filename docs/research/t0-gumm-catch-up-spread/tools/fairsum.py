# Tool code of t0-gumm. fairsum.py <out.png> [fair folder]: the fair test's table and per-draw charts, from the fair.json
# files that fairmeasure.py wrote (queue-fair.sh's runs <scene>-<event>-<code> under /private/tmp/t0-gbgj/gumm-fair).
# The table, per scene, event and code: the slowest region's settle, the boil over draws 1 to 59 and 1 to 8 (the mean of the
# broad crops), the grain over draws 0 to 15 (the mean of the floor and the wall), and the narrow crop's boil 1 to 59, 1 to 8
# and grain 0 to 15. The cut's draws equal the start's bit for bit (queue-fair2.sh), so the table shows the cut once as
# a check. The spread: per scene, code and measure, the largest less the smallest value over the start with the world run
# 0, 1 and 2 ticks more (start, start+t1, start+t2). A difference between two codes that is smaller than the spread is not
# shown by this test. The chart: one row per scene (the start; the cut is identical), four panes: the grain of each draw 0 to 15 (the mean of the floor and the wall),
# the boil of each draw 1 to 15 (the mean of the broad crops), and the same two for the narrow crop. One line per code.
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

out = Path(sys.argv[1])
root = Path(sys.argv[2] if len(sys.argv) > 2 else '/private/tmp/t0-gbgj/gumm-fair')
CODES = ('today', 'none', 'C32')
COLOURS = {'today': (40, 40, 40), 'none': (220, 60, 60), 'C32': (40, 110, 200)}
EVENTS = ('start', 'cut', 'start+t1', 'start+t2')
SPREAD = ('start', 'start+t1', 'start+t2')
GRAIN = ('floor', 'wall')
MEASURES = (('settleSlowest', 'settle', '{}'), ('boil1to59', 'boil 1-59 %', '{:.3f}'), ('boil1to8', 'boil 1-8 %', '{:.3f}'),
            ('grain0to15', 'grain 0-15 %', '{:.2f}'), ('narrowBoil1to59', 'narrow boil 1-59 %', '{:.3f}'),
            ('narrowBoil1to8', 'narrow boil 1-8 %', '{:.3f}'), ('narrowGrain0to15', 'narrow grain 0-15 %', '{:.2f}'))

runs = {}
for scene in ('bay', 'cabin'):
    for event in EVENTS:
        for code in CODES:
            f = root / f'{scene}-{event}-{code}' / 'fair.json'
            if f.exists():
                runs[scene, event, code] = json.loads(f.read_text())

print('| Scene | Event | Code | ' + ' | '.join(m[1] for m in MEASURES) + ' |')
print('|' + '---|' * (3 + len(MEASURES)))
for (scene, event, code), r in runs.items():
    cells = ['none' if r[k] is None else fmt.format(r[k]) for k, _, fmt in MEASURES]
    print(f'| {scene} | {event} | {code} | ' + ' | '.join(cells) + ' |')
print('\nThe spread over start, start+t1 and start+t2 (largest less smallest):')
print('| Scene | Code | ' + ' | '.join(m[1] for m in MEASURES) + ' |')
print('|' + '---|' * (2 + len(MEASURES)))
for scene in ('bay', 'cabin'):
    for code in CODES:
        group = [runs[scene, e, code] for e in SPREAD if (scene, e, code) in runs]
        if len(group) < 2:
            continue
        cells = []
        for k, _, fmt in MEASURES:
            v = [g[k] for g in group]
            cells.append('n/a' if None in v else fmt.format(max(v) - min(v)))
        print(f'| {scene} | {code} | ' + ' | '.join(cells) + ' |')

rows = [(s, 'start') for s in ('bay', 'cabin') if any((s, 'start', c) in runs for c in CODES)]
PW, PH, M = 330, 170, 44
im = Image.new('RGB', (4 * (PW + M) + M, len(rows) * (PH + 40) + 60), 'white')
g = ImageDraw.Draw(im)
g.text((M, 8), 'The fair test at the constant render scale 2/3: per draw after the event. Black today, red none, blue C32.',
       fill='black')
for i, (scene, event) in enumerate(rows):
    y0 = 40 + i * (PH + 40)
    panes = []
    for crops, name in ((GRAIN, 'grain'), (None, 'boil'), (('narrow',), 'narrow grain'), (('narrow',), 'narrow boil')):
        series = {}
        for code in CODES:
            r = runs.get((scene, event, code))
            if r is None:
                continue
            if name.endswith('grain'):
                series[code] = (list(range(16)), np.mean([r['grain'][c] for c in crops], axis=0))
            else:
                cs = crops or [c for c in r['boil'] if c != 'narrow']
                series[code] = (list(range(1, 16)), np.mean([r['boil'][c][:15] for c in cs], axis=0))
        panes.append((name, series))
    for j, (name, series) in enumerate(panes):
        x0 = M + j * (PW + M)
        top = max(max(v) for _, v in series.values()) * 1.08
        g.rectangle([x0, y0 + 16, x0 + PW, y0 + 16 + PH], outline=(200, 200, 200))
        g.text((x0, y0), f'{scene} {event}: {name} %, draws {"0" if "grain" in name else "1"}-15', fill='black')
        for frac in (0.5, 1.0):
            yy = y0 + 16 + PH * (1 - frac)
            g.line([(x0, yy), (x0 + PW, yy)], fill=(230, 230, 230))
            g.text((x0 - 40, yy - 6), f'{top * frac:.2f}', fill='black')
        for code, (xs, v) in series.items():
            pts = [(x0 + (d / 15) * PW, y0 + 16 + PH * (1 - val / top)) for d, val in zip(xs, v)]
            g.line(pts, fill=COLOURS[code], width=2)
        g.text((x0 + PW - 20, y0 + 20 + PH), '15', fill='black')
im.save(out)
print(f'\nchart: {out}')
