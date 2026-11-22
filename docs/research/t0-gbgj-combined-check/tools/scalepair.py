# Tool code of t0-gbgj. scalepair.py <out.png> <run at 0.956> <run at 2/3> <frame>...: whole kept frames of the switch
# "cabin" (s/<run>/images/scale/cabin-<frame>.png), the 0.956 run left and the 2/3 run right, each at half scale, one row
# per frame, in the form of the acceptance lead's jamb-m-0956-vs-23-off195.png. m.sh runs it in $W/w.
import sys
from PIL import Image, ImageDraw
out, a, b, frames = sys.argv[1], sys.argv[2], sys.argv[3], [int(k) for k in sys.argv[4:]]
sheet = Image.new('RGB', (1920, 546 * len(frames) - 6), (40, 40, 40))
for i, k in enumerate(frames):
    for j, run in enumerate((a, b)):
        im = Image.open(f's/{run}/images/scale/cabin-{k:03d}.png').convert('RGB').resize((960, 540))
        ImageDraw.Draw(im).text((6, 6), f'{run}, film frame {k}', fill=(255, 80, 80))
        sheet.paste(im, (960 * j, 546 * i))
sheet.save(out); print(out)
