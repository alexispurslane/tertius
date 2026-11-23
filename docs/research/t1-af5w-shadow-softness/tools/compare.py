"""Tool code of t1-af5w, part 0 on. compare.py <stills folder> <out.png> <scale> <view>:<x>:<y>:<size> <caption> <label>...:
one row of 1:1 crops of capture a of the same box from each label's still (<stills folder>/<label>/<view>/a.png), scaled
<scale> times with no smoothing, left to right in the order given, each under its label, with one caption line under
the row. The first label is main."""
import sys
from PIL import Image, ImageDraw, ImageFont

folder, out, k = sys.argv[1], sys.argv[2], int(sys.argv[3])
view, x, y, n = sys.argv[4].split(':')
x, y, n = int(x), int(y), int(n)
caption, labels = sys.argv[5], sys.argv[6:]
font = ImageFont.load_default(size=18)
pad, head = 12, 28
side = n * k
sheet = Image.new('RGB', (pad + len(labels) * (side + pad), head + side + 2 * head), 'white')
d = ImageDraw.Draw(sheet)
for i, label in enumerate(labels):
    tile = Image.open(f'{folder}/{label}/{view}/a.png').convert('RGB').crop((x, y, x + n, y + n))
    left = pad + i * (side + pad)
    sheet.paste(tile.resize((side, side), Image.NEAREST), (left, head))
    d.text((left, 4), label, fill='black', font=font)
d.text((pad, head + side + 8), caption, fill='black', font=font)
sheet.save(out)
