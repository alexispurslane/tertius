# Tool code (t1-gout, step 3). openings.py <evidence step3 folder> <out.png>: the near right opening at 2x (take.sh) of the
# window 1800 x 1008 at the render scales 2/3, 0.96 and 1 and the ticks 100 and 6380, main in the top row of each tick and
# the fix below it.
import sys
from PIL import Image, ImageDraw
E, out = sys.argv[1:3]
cols = [('2/3', 'ramp/{b}-1800x1008-t{t}-opening.png'), ('0.96', 'window/{b}-s0.96-1800x1008-t{t}-opening.png'),
        ('1', 'window/{b}-s1-1800x1008-t{t}-opening.png')]
rows = [(t, b) for t in (100, 6380) for b in ('main', 'A')]
w, h = Image.open(f'{E}/' + cols[0][1].format(b='main', t=100)).size
sheet = Image.new('RGB', (130 + 3 * (w + 8), 24 + 4 * (h + 8)), 'white')
d = ImageDraw.Draw(sheet)
for j, (label, _) in enumerate(cols):
    d.text((130 + j * (w + 8) + 4, 6), f'render scale {label}', fill='black')
for i, (t, b) in enumerate(rows):
    d.text((6, 24 + i * (h + 8) + h // 2), f'{"main" if b == "main" else "the fix"}, tick {t}', fill='black')
    for j, (_, pattern) in enumerate(cols):
        sheet.paste(Image.open(f'{E}/' + pattern.format(b=b, t=t)).convert('RGB'), (130 + j * (w + 8), 24 + i * (h + 8)))
sheet.save(out)
