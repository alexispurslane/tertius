# Tool code of t0-gbgj. gridspecks.py <still> <label=run>...: the specks of a survey still (s/<run>/images/still/<still>.png)
# per cell of a 3 x 3 grid over the image, named r<row><column> from r00 at the top left. A speck is counted with the rule
# of edgefaults.py (its twin: keep the two rules equal): a group of 8-connected pixels whose display-linear luminance is
# at least 1.5 times and at least 0.01 above the median of its 7 x 7 neighborhood. This relative rule counts dim specks,
# which the survey's own speck score (per 10,000 visible pixels, with an absolute floor of +0.03 above the neighborhood)
# leaves out, so its counts are much larger than the survey's score times the visible pixels. A group that crosses a
# cell's border counts in each cell it touches. m.sh runs it in $W/w.
import sys
import numpy as np
from PIL import Image
from scipy.ndimage import label, median_filter


def linear(rgb):  # the same decoding as edgefaults.py
    c = rgb / 255.0; c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    return c @ np.array([0.2126, 0.7152, 0.0722])


still, runs = sys.argv[1], [a.split('=', 1) for a in sys.argv[2:]]
print(f'== {still}: specks per cell (r00 r01 r02 | r10 r11 r12 | r20 r21 r22), and in the whole image')
for name, run in runs:
    y = linear(np.asarray(Image.open(f's/{run}/images/still/{still}.png').convert('RGB')).astype(float))
    m = median_filter(y, size=7)
    speck = (y >= 1.5 * m) & (y - m >= 0.01)
    h, w = y.shape
    cells = [[label(speck[r * h // 3:(r + 1) * h // 3, c * w // 3:(c + 1) * w // 3], structure=np.ones((3, 3)))[1]
              for c in range(3)] for r in range(3)]
    whole = label(speck, structure=np.ones((3, 3)))[1]
    print(f'{name:14s} ' + ' | '.join(' '.join(f'{v:4d}' for v in row) for row in cells) + f'   whole {whole}')
