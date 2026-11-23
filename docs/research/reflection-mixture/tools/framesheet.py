# Tool code of ter-fe63: three consecutive film frames of each take at 1:1, one row per frame.
# Usage: python3 -I framesheet.py <box> <first frame> <out png> <label=take dir>...
import sys
from pathlib import Path
from PIL import Image, ImageDraw
x0, y0, x1, y1 = map(int, sys.argv[1].split(',')); k0 = int(sys.argv[2])
takes = [(a.split('=')[0], Path(a.split('=')[1])) for a in sys.argv[4:]]
w, h = x1 - x0, y1 - y0
s = Image.new('RGB', (len(takes) * (w + 8) - 8, 3 * (h + 20)), 'white'); d = ImageDraw.Draw(s)
for r, k in enumerate(range(k0, k0 + 3)):
    for c, (label, t) in enumerate(takes):
        s.paste(Image.open(t / f'n-{k:06d}.png').convert('RGB').crop((x0, y0, x1, y1)), (c * (w + 8), r * (h + 20) + 20))
        d.text((c * (w + 8) + 4, r * (h + 20) + 4), f'{label} frame {k}', fill='black')
s.save(sys.argv[3])
