# crops.py <out folder> <view> <sheet.png> <x0,y0,w,h> <zoom> <columns> <name[=label]>...: a grid of zoomed crops.
# The name "reference" is the long reference's mean image; any other name is a variant's still of the view.
import sys
from PIL import Image, ImageDraw, ImageFont
out, view, sheet = sys.argv[1], sys.argv[2], sys.argv[3]
x0, y0, w, h = [int(v) for v in sys.argv[4].split(',')]
zoom, columns = int(sys.argv[5]), int(sys.argv[6])
def font(size):
    for path in ('/System/Library/Fonts/Helvetica.ttc', '/System/Library/Fonts/SFNS.ttf'):
        try: return ImageFont.truetype(path, size)
        except OSError: pass
    return ImageFont.load_default()
F = font(22)
tiles = []
for a in sys.argv[7:]:
    name, _, label = a.partition('=')
    path = f'{out}/reference-mean.png' if name == 'reference' else f'{out}/{name}/{view}.png'
    tiles.append((label or name, Image.open(path).convert('RGB')))
pad, label_h = 6, 32
rows = (len(tiles) + columns - 1) // columns
img = Image.new('RGB', (columns * (w * zoom + pad) - pad, rows * (h * zoom + label_h + pad) - pad), (24, 24, 24))
d = ImageDraw.Draw(img)
for i, (label, im) in enumerate(tiles):
    cx, cy = (i % columns) * (w * zoom + pad), (i // columns) * (h * zoom + label_h + pad)
    img.paste(im.crop((x0, y0, x0 + w, y0 + h)).resize((w * zoom, h * zoom), Image.NEAREST), (cx, cy + label_h))
    d.text((cx + 6, cy + 4), label, fill=(240, 240, 240), font=F)
img.save(sheet)
print(sheet, img.size)
