# sheet.py <out.png> <case> <x0,y0,x1,y1|full> <zoom> <label=variant[:reference]> ...: crops of survey stills side by side, labels on top.
import sys
from PIL import Image, ImageDraw
out, case, crop, zoom = sys.argv[1], sys.argv[2], sys.argv[3], float(sys.argv[4])
tiles = []
for arg in sys.argv[5:]:
    label, v = arg.split("=", 1)
    name = f"{case}-reference.png" if v.endswith(":reference") else f"{case}.png"
    im = Image.open(f"s/{v.split(':')[0]}/images/still/{name}").convert("L").convert("RGB")
    if crop != "full": im = im.crop([int(c) for c in crop.split(",")])
    if zoom != 1: im = im.resize((int(im.width * zoom), int(im.height * zoom)), Image.NEAREST if zoom > 1 else Image.LANCZOS)
    tile = Image.new("RGB", (im.width, im.height + 22), (0, 0, 0)); tile.paste(im, (0, 22))
    ImageDraw.Draw(tile).text((4, 5), label, fill=(255, 255, 255)); tiles.append(tile)
w = sum(t.width for t in tiles) + 6 * (len(tiles) - 1); h = max(t.height for t in tiles)
sheet = Image.new("RGB", (w, h), (60, 60, 60)); x = 0
for t in tiles: sheet.paste(t, (x, 0)); x += t.width + 6
sheet.save(out); print(out, sheet.size)
