# Tool code of t0-gbgj. rampcrops.py <out.png> <ramp folder> <width>x<height>...: the near right opening (output pixels
# 1700 to 1920 by 440 to 640, which holds the right jamb crop of sizeramp.py) at 2x nearest neighbor, from the kept frame
# of each named render size of a size ramp of the acceptance lead's sizeramp.py (b53c037f1). The ramp holds size i of
# its table for 40 draws, so the size's last frame is f-<40 (i + 1) - 1>.png in <ramp folder>/frames. Each tile is
# labeled with the size and the right jamb mean that ramp.txt gives. A size that does not leak keeps only its settle-check
# frame, 10 draws before its last one, where the ramp checked the settle; the label then says so.
import sys
from pathlib import Path
from PIL import Image, ImageDraw
out, ramp, sizes = sys.argv[1], Path(sys.argv[2]), sys.argv[3:]
rows = [l.split() for l in (ramp / 'ramp.txt').read_text().splitlines()]
table = [r for r in rows if len(r) >= 5 and r[0].isdigit() and r[1].isdigit()]
BOX = (1700, 440, 1920, 640)
tiles = []
for s in sizes:
    w, h = s.split('x'); i = next(k for k, r in enumerate(table) if r[0] == w and r[1] == h)
    last = ramp / 'frames' / f'f-{40 * (i + 1) - 1:06d}.png'; early = not last.exists()
    frame = ramp / 'frames' / f'f-{40 * (i + 1) - 11:06d}.png' if early else last
    im = Image.open(frame).convert('RGB').crop(BOX).resize((440, 400), Image.NEAREST)
    note = ', 10 draws before its last' if early else ''
    ImageDraw.Draw(im).text((4, 4), f'{w} x {h}: right jamb {table[i][3]}{note}', fill=(255, 80, 80)); tiles.append(im)
sheet = Image.new('RGB', (446 * len(tiles) - 6, 400), (40, 40, 40))
for k, t in enumerate(tiles): sheet.paste(t, (446 * k, 0))
sheet.save(out); print(out)
