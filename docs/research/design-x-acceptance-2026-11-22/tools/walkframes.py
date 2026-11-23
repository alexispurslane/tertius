# Tool code of t0-5tyj. walkframes.py <out.png> <label=walkframes folder>...: the sheet of the walk cabin-walk's stop,
# from the runs of walkframes.sh. For each side, one row of the frames 0, 20, 40 and 150 draws after the stop, at half
# size, and one row of each frame's light against the same side's frame 199 (the settled frame): the ratio of the two
# display-linear luminances after a blur of 4 output pixels (so that the grain of single draws does not hide the
# trail), shown as grey 128 for no difference and FACTOR levels per unit of ratio, so +5 % is 100 levels brighter
# (white is +6.4 % or more, black -6.4 % or less). The sides' rows follow the order of the arguments.
import sys
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter

FRAMES, SETTLED, FACTOR, W, H = (0, 20, 40, 150), 199, 2000, 960, 540
out, sides = sys.argv[1], [a.split('=', 1) for a in sys.argv[2:]]


def linear(im):
    a = np.asarray(im.convert('RGB')).astype(float) / 255
    a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
    return gaussian_filter(a @ [0.2126, 0.7152, 0.0722], 4)


def tile(im, label):
    t = Image.new('RGB', (W, H + 22), 'black')
    t.paste(im.resize((W, H), Image.LANCZOS), (0, 22))
    ImageDraw.Draw(t).text((4, 5), label, fill='white')
    return t


rows = []
for label, folder in sides:
    settled = linear(Image.open(f'{folder}/h-{SETTLED:06d}.png'))
    frames = {k: Image.open(f'{folder}/h-{k:06d}.png').convert('RGB') for k in FRAMES}
    rows.append([tile(frames[k], f'{label}, {k} draws after the stop') for k in FRAMES])
    diffs = []
    for k in FRAMES:
        ratio = linear(frames[k]) / np.maximum(settled, 1e-4) - 1
        mean = 100 * float(np.median(ratio[settled > 0.002]))
        grey = np.clip(128 + FACTOR * ratio, 0, 255).astype(np.uint8)
        diffs.append(tile(Image.fromarray(grey).convert('RGB'),
                          f'{label}, {k} against {SETTLED}: grey 128 = 0, +5 % = +100 levels; median {mean:+.1f} %'))
    rows.append(diffs)
sheet = Image.new('RGB', (4 * W + 18, len(rows) * (H + 22) + 6 * (len(rows) - 1)), (60, 60, 60))
for r, row in enumerate(rows):
    for c, t in enumerate(row):
        sheet.paste(t, (c * (W + 6), r * (H + 28)))
sheet.save(out)
print(out, sheet.size)
