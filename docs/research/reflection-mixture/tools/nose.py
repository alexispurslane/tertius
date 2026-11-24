# Tool code of ter-fe63, stage 1: main beside the reflection mixture on one box. For each take: the capture specks of
# every capture by the speck counter's rule (capspecks.py: output rows 0-500, columns 480-1880, brighter than the 7 x 7
# median by more than 12 of 255 in gray, outside pixels above 200 and their 6-pixel surround), their median and largest;
# from the census keep of the box (65-byte records) the largest diffuse and specular samples, the count per draw of
# specular samples above the speck bound B (the main take's diffuse 99.9th percentile), the mean specular sample; and the
# box's mean linear luminance over the 20 captures. Writes a 1:1 crop sheet of capture 19, main left.
# Usage: python3 -I nose.py <box x0,y0,x1,y1 in output px> <out png> <label=take dir>...
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import median_filter, maximum_filter
x0, y0, x1, y1 = map(int, sys.argv[1].split(','))
fields = [('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'), ('ds', '<f4'),
          ('d14', '<f4', 4), ('d15', '<f4', 4), ('d22', '<f4', 4)]
W = [0.2126, 0.7152, 0.0722]
lin = lambda a: np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
import os
# NOSE_BOUND gives B when the first take's keep is gone (main's 99.9th percentile, 3.8886, nose-result.json).
out, crops, bound = {}, [], (float(os.environ['NOSE_BOUND']) if 'NOSE_BOUND' in os.environ else None)
for arg in sys.argv[3:]:
    label, d = arg.split('='); d = Path(d)
    specks = []
    for png in sorted(d.glob('c-s*.png')):
        g = np.asarray(Image.open(png).convert('L'), float)[0:500, 480:1880]
        lamp = maximum_filter(g > 200, size=13)
        specks.append(int(((g - median_filter(g, size=7) > 12) & ~lamp).sum()))
    crops.append((label, Image.open(d / 'c-s19.png').convert('RGB').crop((x0, y0, x1, y1))))
    # A take whose captures 0 to 18 and keep were deleted gives only its crop.
    if not (d / 'census.keep0').exists() or len(specks) < 20:
        continue
    rec = np.fromfile(d / 'census.keep0', dtype=np.dtype(fields))
    census = json.loads((d / 'census.json').read_text())
    draws = census['DrawEnd'] - census['DrawFirst']
    bound = bound if bound is not None else float(np.percentile(rec['x'], 99.9))
    mean = np.mean([(lin(np.asarray(Image.open(d / f'c-s{k:02d}.png').convert('RGB')) / 255) @ W)[y0:y1, x0:x1].mean()
                    for k in range(20)])
    out[label] = {'capture specks median': float(np.median(specks)), 'largest': max(specks),
                  'largest diffuse sample': float(rec['x'].max()), 'largest specular sample': float(rec['xs'].max()),
                  'specular samples above B per draw': float((rec['xs'] > bound).sum() / draws),
                  'diffuse samples above B per draw': float((rec['x'] > bound).sum() / draws),
                  'mean specular sample': float(rec['xs'].mean()), 'mean diffuse sample': float(rec['x'].mean()),
                  'box mean, 20 captures': float(mean), 'B': bound}
w = sum(c.width for _, c in crops) + 8 * (len(crops) - 1)
s = Image.new('RGB', (w, crops[0][1].height + 22), 'white'); dr = ImageDraw.Draw(s); x = 0
for label, c in crops:
    s.paste(c, (x, 22)); dr.text((x + 4, 5), label, fill='black'); x += c.width + 8
s.save(sys.argv[2])
print(json.dumps(out, indent=1))
