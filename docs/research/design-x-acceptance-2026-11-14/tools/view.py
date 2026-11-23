# view.py <out.png> <gain> <label=probe.png> ...: decodes probe captures (24-bit log2 luminance) to gray sRGB, same gain, side by side
# with labels. Optional crop via env CROP=x0,y0,x1,y1 (output pixels) and ZOOM=<n>.
import os, sys
import numpy as np
from PIL import Image, ImageDraw
def lum(path):
    a = np.asarray(Image.open(path).convert("RGB")).astype(np.uint32)
    q = (a[..., 0] << 16) | (a[..., 1] << 8) | a[..., 2]
    y = np.exp2(q / 16777215.0 * 40.0 - 24.0); y[q == 0] = 0.0
    return y
out, gain = sys.argv[1], float(sys.argv[2])
crop = [int(v) for v in os.environ["CROP"].split(",")] if os.environ.get("CROP") else None
zoom = int(os.environ.get("ZOOM", "1"))
tiles = []
for arg in sys.argv[3:]:
    label, path = arg.split("=", 1)
    y = lum(path)
    if crop: y = y[crop[1]:crop[3], crop[0]:crop[2]]
    v = np.clip(y * gain, 0, 1) ** (1 / 2.2)
    im = Image.fromarray((v * 255 + 0.5).astype(np.uint8), "L").convert("RGB")
    if zoom > 1: im = im.resize((im.width * zoom, im.height * zoom), Image.NEAREST)
    d = ImageDraw.Draw(im); d.rectangle([0, 0, 9 * len(label) + 8, 18], fill=(0, 0, 0)); d.text((4, 3), label, fill=(255, 255, 0))
    tiles.append(im)
w = sum(t.width for t in tiles) + 4 * (len(tiles) - 1); h = max(t.height for t in tiles)
sheet = Image.new("RGB", (w, h), (40, 40, 40)); x = 0
for t in tiles: sheet.paste(t, (x, 0)); x += t.width + 4
sheet.save(out); print(out, sheet.size)
