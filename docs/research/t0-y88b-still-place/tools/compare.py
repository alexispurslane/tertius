# Tool code of t0-y88b. compare.py <main run> <fix run> <out name>: main's and the fix's PNGs of one take (yrun.sh), draw
# by draw, from /private/tmp/t0-y88b/runs. It prints, per pair of draws, the largest difference of a channel in 8-bit
# steps and the count of output pixels that differ by more than 2 steps, then the same over all draws. It writes
# <out name>-1x.png into the record's pictures/ folder: a 1:1 crop of 320 x 220 output pixels around the largest
# difference of the last draw, main left, the fix in the middle and the difference times 8 right, and <out name>-3x.png,
# the same at 3x (nearest). Run it in the dev shell.
import sys
from pathlib import Path

import numpy as np
from PIL import Image

R = Path('/private/tmp/t0-y88b/runs')
main, fix, name = sys.argv[1:4]
out = Path(__file__).resolve().parent.parent / 'pictures'
out.mkdir(exist_ok=True)
a_files = sorted((R / main).glob('f-*.png'))
b_files = sorted((R / fix).glob('f-*.png'))
assert [f.name for f in a_files] == [f.name for f in b_files], 'the two takes keep different draws'
largest, over = 0, 0
for fa, fb in zip(a_files, b_files):
    a = np.asarray(Image.open(fa).convert('RGB'), np.int16)
    b = np.asarray(Image.open(fb).convert('RGB'), np.int16)
    d = np.abs(a - b).max(axis=2)
    largest = max(largest, int(d.max()))
    over = max(over, int((d > 2).sum()))
    print(f'{fa.stem}: largest {int(d.max())}, pixels over 2: {int((d > 2).sum())}')
print(f'{name}: {len(a_files)} draws, largest difference {largest} of 255, at most {over} pixels over 2 in a draw')
h, w = d.shape
y, x = np.unravel_index(np.argmax(d), d.shape)
x0, y0 = int(min(max(x - 160, 0), w - 320)), int(min(max(y - 110, 0), h - 220))
box = (x0, y0, x0 + 320, y0 + 220)
pa = Image.open(a_files[-1]).convert('RGB').crop(box)
pb = Image.open(b_files[-1]).convert('RGB').crop(box)
pd = Image.fromarray(np.clip(d[y0:y0 + 220, x0:x0 + 320] * 8, 0, 255).astype(np.uint8)).convert('RGB')
row = Image.new('RGB', (960, 220), 'black')
for i, p in enumerate((pa, pb, pd)):
    row.paste(p, (320 * i, 0))
row.save(out / f'{name}-1x.png')
row.resize((2880, 660), Image.NEAREST).save(out / f'{name}-3x.png')
print(f'crop box {box} of draw {a_files[-1].stem}: {out / (name + "-1x.png")} and -3x.png')
