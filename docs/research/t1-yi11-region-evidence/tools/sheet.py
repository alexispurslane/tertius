# Tool code of t1-yi11. sheet.py <out.png> <draws> <note> <side>...: a frame sheet of a film, one row per draw and one
# column per side, each frame scaled to 1600 / sides px wide (bicubic), with a label above each row. <draws> is a list
# "d1,d2,..." of film draws. A side is "label|folder|first": its folder holds one PNG per draw in draw order, and the first
# PNG is draw <first>. <note> goes into each row's label (the exposure).
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

out, draws, note, *specs = sys.argv[1:]
draws = [int(d) for d in draws.split(',')]
sides = [s.split('|') for s in specs]
files = [sorted(Path(folder).glob('*.png')) for _, folder, _ in sides]
w0, h0 = Image.open(files[0][0]).size
w = 1600 // len(sides)
h = round(h0 * w / w0)
BAND = 26
try:
    font = ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf', 15)
except OSError:
    font = ImageFont.load_default()
sheet = Image.new('RGB', (w * len(sides), len(draws) * (h + BAND)), (16, 16, 16))
draw = ImageDraw.Draw(sheet)
for r, d in enumerate(draws):
    y = r * (h + BAND)
    for i, (label, _, first) in enumerate(sides):
        frame = Image.open(files[i][d - int(first)]).convert('RGB').resize((w, h), Image.BICUBIC)
        sheet.paste(frame, (i * w, y + BAND))
        draw.text((i * w + 4, y + 5), f'{label}, draw {d}' + (f', {note}' if i == 0 else ''), fill=(255, 255, 255),
                  font=font)
sheet.save(out)
