# Tool code of the region cap's takes (t0-qvm1). crops.py: crops at 1:1 of the bay's still at draw 890 (capture 26)
# where main's history is 8 to under 16 draws and the cap's is 31.5 or more, in the same run's dump. Prints the boxes;
# writes crops-bay.png (1:1 and a 3x nearest copy). Run it in the dev shell.
import re
import numpy as np
from pathlib import Path
from PIL import Image, ImageDraw
from scipy import ndimage
R = Path('/private/tmp/t0-qvm1c/runs')
def L(b):
    p = next((R / f'{b}-bay' / 'dump').glob('history-890-*.f32'))
    w, h = map(int, re.search(r'-(\d+)x(\d+)\.f32$', p.name).groups())
    return np.fromfile(p, np.float32).reshape(h, w)
m, c = L('pm'), L('pc')
both = (m >= 8) & (m < 16) & (c >= 31.5)
lab, n = ndimage.label(both)
sizes = ndimage.sum(both, lab, range(1, n + 1))
order = np.argsort(sizes)[::-1][:2]
pics = {b: Image.open(R / f'{b}-bay' / 'frames' / 'v-s26.png').convert('RGB') for b in ('pm', 'pc')}
rows = []
for i in order:
    ys, xs = np.nonzero(lab == i + 1)
    cy, cx = int(ys.mean() * 1.5), int(xs.mean() * 1.5)
    box = (max(cx - 160, 0), max(cy - 110, 0), max(cx - 160, 0) + 320, max(cy - 110, 0) + 220)
    print('patch', i + 1, 'render px', xs.min(), xs.max(), ys.min(), ys.max(), 'pixels', int(sizes[i]), 'output box', box,
          'main L', float(np.median(m[lab == i + 1])), 'cap L', float(np.median(c[lab == i + 1])))
    row = Image.new('RGB', (2 * 320 + 10, 220 + 24), 'white')
    for k, b in enumerate(('pm', 'pc')):
        row.paste(pics[b].crop(box), (k * 330, 24))
    ImageDraw.Draw(row).text((4, 6), f'bay, draw 890, output pixels {box[0]}-{box[2]} x {box[1]}-{box[3]}: main (left, history {np.median(m[lab == i + 1]):.0f} draws) and the cap (right, {np.median(c[lab == i + 1]):.0f})', fill='black')
    rows.append(row)
sheet = Image.new('RGB', (rows[0].width, sum(r.height for r in rows)), 'white')
y = 0
for r in rows:
    sheet.paste(r, (0, y)); y += r.height
sheet.save('/private/tmp/t0-qvm1c/pics/crops-bay.png')
sheet.resize((sheet.width * 3, sheet.height * 3), Image.NEAREST).save('/private/tmp/t0-qvm1c/pics/crops-bay-3x.png')
