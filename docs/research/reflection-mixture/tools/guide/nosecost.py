# Tool code of t1-nyjl: the cost side on the nose's ceiling box, guided share 0.25 against 0.5. Per take: the capture
# specks of every kept capture by the speck counter's rule (capspecks.py: output rows 0-500, columns 480-1880, brighter
# than the 7 x 7 median by more than 12 of 255, outside the lamps), their median and largest, the largest diffuse sample
# of the box over the 400 counted draws, the per-sample variance pooled over the box (from census.sums, Xpre), the box's
# mean, and a 1:1 crop of capture 19 of the box (output 750,0,1170,330), side by side.
# Usage: python3 -I nosecost.py <take 0.25> <take 0.5> <out png>
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import median_filter, maximum_filter
takes = [Path(sys.argv[1]), Path(sys.argv[2])]; out = Path(sys.argv[3])
box = (750, 0, 1170, 330); rbox = (500, 0, 780, 220)
res = {}
for t in takes:
    shots = {}
    for png in sorted(t.glob('c-s*.png')):
        g = np.asarray(Image.open(png).convert('L'), float)[0:500, 480:1880]
        lamp = maximum_filter(g > 200, size=13)
        shots[png.stem] = int(((g - median_filter(g, size=7) > 12) & ~lamp).sum())
    m = json.loads((t / 'census.json').read_text())
    s = np.fromfile(t / 'census.sums', '<f8').reshape(24, m['Height'], m['Width'])
    x0, y0, x1, y1 = rbox
    n, S, Q = s[12][y0:y1, x0:x1], s[13][y0:y1, x0:x1], s[14][y0:y1, x0:x1]
    mean = S / n; var = Q / n - mean ** 2
    rec = np.fromfile(t / 'census.keep0', dtype=np.dtype([('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'),
                                                           ('ds', '<f4'), ('d14', '<f4', 4), ('d15', '<f4', 4)]))
    v = list(shots.values())
    res[t.name] = {'capture specks median': float(np.median(v)), 'capture specks largest': max(v),
                   'capture specks of captures 4 to 19 median': float(np.median(v[4:])),
                   'largest diffuse sample': float(rec['x'].max()), 'box mean X': float(mean.mean()),
                   'pooled per-sample variance': float(var.sum())}
a, b = res[takes[0].name], res[takes[1].name]
res['variance factor 0.5 / 0.25'] = b['pooled per-sample variance'] / a['pooled per-sample variance']
res['mean ratio 0.5 / 0.25'] = b['box mean X'] / a['box mean X']
w, h = box[2] - box[0], box[3] - box[1]
sheet = Image.new('RGB', (2 * w + 8, h + 22), 'white'); d = ImageDraw.Draw(sheet)
for i, (t, lab) in enumerate(zip(takes, ('guided share 0.25 (main)', 'guided share 0.5'))):
    sheet.paste(Image.open(t / 'c-s19.png').convert('RGB').crop(box), (i * (w + 8), 22))
    d.text((i * (w + 8) + 4, 5), f'{lab}, sunlit nose 2/3, -1.7507 stops, capture 19', fill='black')
sheet.save(out)
print(json.dumps(res, indent=1))
