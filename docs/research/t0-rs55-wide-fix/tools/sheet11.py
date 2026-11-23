# Tool code of t0-rs55. sheet11.py <out.png> <left label|folder> <right label|folder> <crop x0,y0,x1,y1> <frame name>...:
# one sheet of a region at 1:1 pixels (no scaling), one row per frame (the PNG <frame name>.png of each folder), left | right,
# the same crop and the same exposure as the runs wrote them. SCALE=2 doubles each pixel (nearest neighbour). A caption
# over each tile names its side and frame; the row's caption carries the render scale of that draw when the folder holds a
# size log and the frame name is "draw:<n>:<frame name>".
import glob
import os
import sys
from PIL import Image, ImageDraw

out, left, right, crop, *frames = sys.argv[1:]
x0, y0, x1, y1 = map(int, crop.split(','))
k = int(os.environ.get('SCALE', '1'))
w, h = (x1 - x0) * k, (y1 - y0) * k
sides = [s.split('|') for s in (left, right)]


def scale_of(folder, draw):
    logs = glob.glob(f'{folder}/size-*.txt')
    if not logs or draw is None:
        return ''
    for line in open(logs[0]):
        v = line.split()
        if int(v[0]) == draw:
            return f', render scale {float(v[3]):.3f}'
    return ''


sheet = Image.new('RGB', (2 * w + 12, len(frames) * (h + 24)), 'white')
g = ImageDraw.Draw(sheet)
for r, spec in enumerate(frames):
    draw, name = (int(spec.split(':')[1]), spec.split(':')[2]) if spec.startswith('draw:') else (None, spec)
    for c, (label, folder) in enumerate(sides):
        tile = Image.open(f'{folder}/{name}.png').convert('RGB').crop((x0, y0, x1, y1))
        if k != 1:
            tile = tile.resize((w, h), Image.NEAREST)
        x, y = c * (w + 12), r * (h + 24)
        sheet.paste(tile, (x, y + 20))
        g.text((x + 2, y + 4), f'{label}, {name}{scale_of(folder, draw)}, 1:1' + (f' x{k}' if k != 1 else ''), fill='black')
sheet.save(out)
print(out, sheet.size)
