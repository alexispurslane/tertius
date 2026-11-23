"""Tool code of t1-af5w. crops.py <out.png> <scale> <png>:<x>:<y>:<size>:<label> ...: a sheet of square crops of stills,
side by side, each <size> output pixels from its top left corner (x, y), shown at <scale> times with no smoothing, so
that each output pixel stays a square. The label goes under each crop."""
import sys
from PIL import Image, ImageDraw, ImageFont
out, k = sys.argv[1], int(sys.argv[2])
specs = [a.split(':', 4) for a in sys.argv[3:]]
try:
    font = ImageFont.load_default(size=16)
except TypeError:
    font = ImageFont.load_default()
tiles = []
for path, x, y, n, label in specs:
    x, y, n = int(x), int(y), int(n)
    tiles.append((Image.open(path).convert('RGB').crop((x, y, x + n, y + n)).resize((n * k, n * k), Image.NEAREST), label))
W = sum(t.width for t, _ in tiles) + 12 * (len(tiles) + 1)
H = max(t.height for t, _ in tiles) + 60
sheet = Image.new('RGB', (W, H), 'white')
d = ImageDraw.Draw(sheet)
x = 12
for t, label in tiles:
    sheet.paste(t, (x, 12))
    for i, line in enumerate(label.split('|')):
        d.text((x, t.height + 16 + 18 * i), line, fill='black', font=font)
    x += t.width + 12
sheet.save(out)
