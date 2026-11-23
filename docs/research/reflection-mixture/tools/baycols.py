# Tool code of ter-fe63, stage 1: the bay's columns at 1:1. For each region (output pixels) a sheet of capture 19 of each
# take side by side, captioned. Usage: python3 -I baycols.py <out prefix> <label=take dir>...
import sys
from pathlib import Path
from PIL import Image, ImageDraw
import json, os
# BAYCOLS_ALL=1 crops all four regions of the bay's boxes file.
regions = {'right-wall': (1300, 100, 1800, 500), 'sunlit-patch': (100, 40, 420, 360)}
if os.environ.get('BAYCOLS_ALL'):
    regions = {k.replace(' ', '-'): tuple(v) for k, v in json.load(open('/private/tmp/t1-wl7c/guide/daub/boxes.json')).items()}
takes = [(a.split('=', 1)[0], Path(a.split('=', 1)[1])) for a in sys.argv[2:]]
for name, box in regions.items():
    w, h = box[2] - box[0], box[3] - box[1]
    s = Image.new('RGB', (len(takes) * (w + 8) - 8, h + 34), 'white'); d = ImageDraw.Draw(s)
    for i, (label, t) in enumerate(takes):
        s.paste(Image.open(t / 'c-s19.png').convert('RGB').crop(box), (i * (w + 8), 34))
        for j, line in enumerate(label.split('|')):
            d.text((i * (w + 8) + 4, 3 + 14 * j), line, fill='black')
    s.save(f'{sys.argv[1]}-{name}.png')
