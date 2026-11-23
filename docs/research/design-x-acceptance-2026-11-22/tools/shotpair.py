# Tool code of t0-5tyj. shotpair.py <out.png> <M image> <X image> <caption>: take T19's screenshot pair, the real
# renderer's headless capture of a script (queue-mac4.sh: scripts/rect-bay.json, scripts/rect-lamps.json) on M and on X,
# side by side at half size (960 x 540 each), with a label above each half.
import sys
from PIL import Image, ImageDraw

out, m, x, caption = sys.argv[1:5]
W, H, TOP = 960, 540, 22
sheet = Image.new('RGB', (2 * W + 8, H + TOP), (40, 40, 40))
d = ImageDraw.Draw(sheet)
for i, (path, label) in enumerate(((m, 'M = main c4c1f1f2f'), (x, 'X = Design X 57b4045ae'))):
    sheet.paste(Image.open(path).convert('RGB').resize((W, H), Image.LANCZOS), (i * (W + 8), TOP))
    d.text((i * (W + 8) + 6, 5), f'{label}, {caption}', fill='white')
sheet.save(out)
print(out, sheet.size)
