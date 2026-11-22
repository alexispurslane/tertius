# censusmap.py <out.png> <still run> <label=census run>...: where the young light-age records of a census still lie. It
# reads each census's block maps (TERTIUS_X_CENSUS_EVERY=1: one byte per block of 8 x 8 render pixels per draw, bit 2 a
# pixel of the block with a light cause, a recent light code). Per block, it counts the draws from draw 1 on with that
# bit, for the diffuse and the specular signal together. It marks each block with a count on the still rect-lamps of
# <still run> (1920 x 1080 output, so a block is 12 x 12 output pixels): yellow for 1 draw, through orange, to red for 50
# or more. One row per census run: the whole image at half size, and the near left window (output pixels 0 to 320 by
# 440 to 680) at 3 times. A last row shows the same window of the still without marks. It prints the blocks with the most
# draws. Run it through m.sh.
import json, sys
import numpy as np
from PIL import Image, ImageDraw

out, still = sys.argv[1], sys.argv[2]
WINDOW = (0, 440, 320, 680)
base = Image.open(f's/{still}/images/still/rect-lamps.png').convert('RGB')
rows = []
for arg in sys.argv[3:]:
    label, census = arg.split('=', 1)
    folder = f's/{census}/census/census-still/rect-lamps'
    header = json.load(open(f'{folder}/header.json'))
    wide, high = header['blocksWide'], header['blocksHigh']
    count = np.zeros((high, wide), np.int64)
    for signal in ('diffuse', 'specular'):
        maps = np.fromfile(f'{folder}/{signal}-0.u8', dtype=np.uint8).reshape(-1, high, wide)
        count += ((maps[1:] >> 2) & 1).astype(np.int64).sum(axis=0)
    image = base.copy()
    side = header['block'] * image.width / (wide * header['block'])
    d = ImageDraw.Draw(image)
    for y, x in zip(*np.nonzero(count)):
        d.rectangle([x * side, y * side, (x + 1) * side - 1, (y + 1) * side - 1],
                    outline=(255, int(255 * (1 - min(count[y, x] / 50, 1))), 0), width=2)
    print(f'{label}: blocks with a young record {int((count > 0).sum())} of {wide * high}, signal-draws {int(count.sum())}')
    for i in np.argsort(count, axis=None)[::-1][:6]:
        y, x = divmod(int(i), wide)
        print(f'  block ({x}, {y}): {count[y, x]} signal-draws, output x {x * side:.0f} to {(x + 1) * side:.0f},'
              f' y {y * side:.0f} to {(y + 1) * side:.0f}')
    rows.append((label, image.resize((960, 540), Image.LANCZOS),
                 image.crop(WINDOW).resize((960, 720), Image.NEAREST)))
plain = base.crop(WINDOW).resize((960, 720), Image.NEAREST)
height = sum(22 + 720 for _ in rows) + 22 + 720 + 6 * len(rows)
sheet = Image.new('RGB', (960 * 2 + 6, height), (60, 60, 60))
d = ImageDraw.Draw(sheet)
y = 0
for label, whole, window in rows:
    d.rectangle([0, y, sheet.width, y + 21], fill=(0, 0, 0))
    d.text((4, y + 5), f'{label}: whole image, half size', fill=(255, 255, 255))
    d.text((970, y + 5), f'{label}: near left window, 3 times', fill=(255, 255, 255))
    sheet.paste(whole, (0, y + 22)); sheet.paste(window, (966, y + 22))
    y += 22 + 720 + 6
d.rectangle([0, y, sheet.width, y + 21], fill=(0, 0, 0))
d.text((970, y + 5), 'the still without marks: near left window, 3 times', fill=(255, 255, 255))
sheet.paste(plain, (966, y + 22))
sheet = sheet.crop((0, 0, sheet.width, y + 22 + 720))
sheet.save(out)
print(out, sheet.size)
