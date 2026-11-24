# Tool code of ter-fe63: capture 19 of each take side by side at 1:1 for the first box of a boxes file, captioned (lines
# split at |). Usage: python3 -I sheet.py <boxes.json> <out png> <label=take dir>...
import json, sys
from pathlib import Path
from PIL import Image, ImageDraw
box = next(iter(json.loads(Path(sys.argv[1]).read_text()).values()))
takes = [(a.split('=', 1)[0], Path(a.split('=', 1)[1])) for a in sys.argv[3:]]
w, h = box[2] - box[0], box[3] - box[1]
s = Image.new('RGB', (len(takes) * (w + 8) - 8, h + 34), 'white'); d = ImageDraw.Draw(s)
for i, (label, t) in enumerate(takes):
    s.paste(Image.open(t / 'c-s19.png').convert('RGB').crop(box), (i * (w + 8), 34))
    for j, line in enumerate(label.split('|')):
        d.text((i * (w + 8) + 4, 3 + 14 * j), line, fill='black')
s.save(sys.argv[2])
