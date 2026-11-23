# Tool code of t0-gbgj. scalepair.py <out.png> <run at 0.956> <run at 2/3> <frame>...: whole kept frames of the switch
# "cabin" (s/<run>/images/scale/cabin-<frame>.png), the 0.956 run left and the 2/3 run right, each at half scale, one row
# per frame, in the form of the acceptance lead's jamb-m-0956-vs-23-off195.png. Where the two runs have the same output
# size, it also prints per frame how many pixels differ in any channel, the largest difference in 8-bit steps, and the
# largest difference inside the jamb crop of jambsheet.py (x 1770 to 1807, y 495 to 584). m.sh runs it in $W/w.
import sys
import numpy as np
from PIL import Image, ImageDraw
out, a, b, frames = sys.argv[1], sys.argv[2], sys.argv[3], [int(k) for k in sys.argv[4:]]
sheet = Image.new('RGB', (1920, 546 * len(frames) - 6), (40, 40, 40))
for k in frames:
    pa, pb = (np.asarray(Image.open(f's/{r}/images/scale/cabin-{k:03d}.png').convert('RGB')).astype(int) for r in (a, b))
    if pa.shape == pb.shape:
        d = np.abs(pa - pb)
        print(f'frame {k}: {int((d.max(axis=2) > 0).sum())} pixels differ, largest difference {int(d.max())} of 255, '
              f'jamb crop largest {int(d[495:585, 1770:1808].max())}')
for i, k in enumerate(frames):
    for j, run in enumerate((a, b)):
        im = Image.open(f's/{run}/images/scale/cabin-{k:03d}.png').convert('RGB').resize((960, 540))
        ImageDraw.Draw(im).text((6, 6), f'{run}, film frame {k}', fill=(255, 80, 80))
        sheet.paste(im, (960 * j, 546 * i))
sheet.save(out); print(out)
