# Tool code of ter-fe63: rect-bay's wall at draw 130, main, coin and exact side by side at 1:1.
from PIL import Image, ImageDraw
D = '/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/fe63/wall'
ims = [(l, Image.open(f'{D}/wall-{l}-d130.png').convert('RGB')) for l in ('main', 'coin', 'exact')]
w, h = ims[0][1].size
s = Image.new('RGB', (3 * w + 16, h + 20), 'white'); d = ImageDraw.Draw(s)
for i, (l, im) in enumerate(ims):
    s.paste(im, (i * (w + 8), 20)); d.text((i * (w + 8) + 4, 4), f'{l}, rect-bay draw 130, -0.359 stops', fill='black')
s.save(f'{D}/../wall-d130-main-coin-exact.png')
