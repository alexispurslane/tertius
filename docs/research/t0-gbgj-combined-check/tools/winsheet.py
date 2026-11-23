# Tool code of t0-gbgj. winsheet.py <out.png> <label=path>...: the window test's opening crops (wintest.py writes
# <run>/opening.png, the near right opening scaled to the window, at 2x) side by side with labels, each tile scaled to
# the height of the first.
import sys
from PIL import Image, ImageDraw
out, items = sys.argv[1], [a.split('=', 1) for a in sys.argv[2:]]
ims = [Image.open(p).convert('RGB') for _, p in items]
h = ims[0].height
ims = [im.resize((round(im.width * h / im.height), h), Image.NEAREST) for im in ims]
sheet = Image.new('RGB', (sum(im.width + 6 for im in ims) - 6, h), (40, 40, 40)); x = 0
for (label, _), im in zip(items, ims):
    ImageDraw.Draw(im).text((4, 4), label, fill=(255, 80, 80)); sheet.paste(im, (x, 0)); x += im.width + 6
sheet.save(out); print(out)
