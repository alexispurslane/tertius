# Tool code of t0-gbgj. stillcrops.py <out.png> <case> <x0,y0,x1,y1> <zoom> <label=run>...: one crop of a survey still
# (s/<run>/images/still/<case>.png, the probe's picture) per run, side by side at <zoom>x nearest neighbor, with labels.
# The stills sample at 0.956 uses it where the speck count differs from 2/3. m.sh runs it in $W/w.
import sys
from PIL import Image, ImageDraw
out, case, box, zoom = sys.argv[1], sys.argv[2], tuple(int(v) for v in sys.argv[3].split(',')), int(sys.argv[4])
w, h = (box[2] - box[0]) * zoom, (box[3] - box[1]) * zoom
runs = [a.split('=', 1) for a in sys.argv[5:]]
sheet = Image.new('RGB', ((w + 6) * len(runs) - 6, h), (40, 40, 40))
for i, (label, run) in enumerate(runs):
    im = Image.open(f's/{run}/images/still/{case}.png').convert('RGB').crop(box).resize((w, h), Image.NEAREST)
    ImageDraw.Draw(im).text((4, 4), label, fill=(255, 80, 80)); sheet.paste(im, ((w + 6) * i, 0))
sheet.save(out); print(out)
