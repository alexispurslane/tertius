# Tool code of t0-rs55. boilparts.py [fair folder]: where the boil of draw 1 rises in the bay's start (queue-bay.sh's kept
# PNGs of draws 0, 1 and 2 of the runs at +0, +1 and +2). With fairmeasure.py's broad crops and blocks (display-linear
# luminance averaged in 24 x 24 px blocks), per code and crop: the boil of draw 1, the mean over the blocks of
# |b1 - (b0 + b2) / 2| in % of the crop's mean; and, between the codes, the mean over the blocks of |wide - today| of each
# draw's block means in % of the crop's mean: which draw's block means the wide fix moves.
import sys
from pathlib import Path
import numpy as np
from PIL import Image

root = Path(sys.argv[1] if len(sys.argv) > 1 else '/private/tmp/t0-gbgj/gumm-fair')
CROPS = {'floor': (560, 700, 1000, 1000), 'wall': (1300, 160, 1840, 560), 'back wall': (620, 420, 980, 560)}


def blocks(png, crop):
    a = np.asarray(Image.open(png).convert('RGB')).astype(float) / 255
    y = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]
    x0, y0, x1, y1 = crop
    c = y[y0:y1, x0:x1]
    h, w = c.shape[0] // 24 * 24, c.shape[1] // 24 * 24
    return c[:h, :w].reshape(h // 24, 24, w // 24, 24).mean(axis=(1, 3)), c.mean()


for t in (0, 1, 2):
    for crop, box in CROPS.items():
        b = {c: [blocks(root / f'rs-bay-all-{c}+{t}' / f'f-{d:06d}.png', box) for d in (0, 1, 2)] for c in ('today', 'wide')}
        mean = b['today'][1][1]
        boil = {c: 100 * np.mean(np.abs(b[c][1][0] - (b[c][0][0] + b[c][2][0]) / 2)) / mean for c in b}
        moved = [100 * np.mean(np.abs(b['wide'][d][0] - b['today'][d][0])) / mean for d in range(3)]
        print(f'+{t} {crop}: boil of draw 1 today {boil["today"]:.2f} %, wide {boil["wide"]:.2f} %; '
              f'|wide - today| of the block means at draws 0, 1, 2: ' + ', '.join(f'{m:.2f} %' for m in moved))
