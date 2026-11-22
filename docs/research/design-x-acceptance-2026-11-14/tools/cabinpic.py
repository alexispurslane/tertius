# cabinpic.py <out.png> <gain> <scale> <label=probe.png> ...: probe captures (24-bit log2 luminance) as gray tiles with a label bar.
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
def lum(path):
    a = np.asarray(Image.open(path).convert("RGB")).astype(np.uint32)
    q = (a[..., 0] << 16) | (a[..., 1] << 8) | a[..., 2]
    y = np.exp2(q / 16777215.0 * 40.0 - 24.0); y[q == 0] = 0.0
    return y
out, gain, scale = sys.argv[1], float(sys.argv[2]), float(sys.argv[3])
try: font = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 20)
except OSError: font = ImageFont.load_default()
tiles = []
for arg in sys.argv[4:]:
    label, path = arg.split("=", 1)
    v = np.clip(lum(path) * gain, 0, 1) ** (1 / 2.2)
    im = Image.fromarray((v * 255 + 0.5).astype(np.uint8), "L").convert("RGB")
    im = im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
    tile = Image.new("RGB", (im.width, im.height + 30), (0, 0, 0)); tile.paste(im, (0, 30))
    ImageDraw.Draw(tile).text((6, 4), label, fill=(255, 255, 255), font=font); tiles.append(tile)
w = sum(t.width for t in tiles) + 6 * (len(tiles) - 1); h = max(t.height for t in tiles)
sheet = Image.new("RGB", (w, h), (60, 60, 60)); x = 0
for t in tiles: sheet.paste(t, (x, 0)); x += t.width + 6
sheet.save(out); print(out, sheet.size)
