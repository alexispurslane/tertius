# Tool code of t1-nyjl: the one-bounce pictures. (1) The full bay at each build's settled automatic exposure, capture 19,
# main left and the one-bounce switch right, each halved, with its stops. (2) The darkest region (the back wall below the
# slot, with 60 px of context) at 1:1, main and one bounce, at the fixed -4.089 stops and at each build's automatic
# exposure. Usage: python3 -I obcrops.py <stills dir> <out dir>
import json, sys
from pathlib import Path
from PIL import Image, ImageDraw
S, O = Path(sys.argv[1]), Path(sys.argv[2])
def stops(t):
    return json.loads((S / t / 'exposure.json').read_text())[19]['stops']
def sheet(panels, path):
    w = sum(p.width for _, p in panels) + 8 * (len(panels) - 1); h = max(p.height for _, p in panels) + 22
    s = Image.new('RGB', (w, h), 'white'); d = ImageDraw.Draw(s); x = 0
    for lab, p in panels:
        s.paste(p, (x, 22)); d.text((x + 4, 5), lab, fill='black'); x += p.width + 8
    s.save(path)
full = [(f'{lab}, auto exposure {stops(t):.3f} stops', Image.open(S / t / 'c-s19.png').convert('RGB').resize((960, 540), Image.LANCZOS))
        for lab, t in (('main', 'bay-auto'), ('one bounce', 'bay-ob-auto'))]
sheet(full, O / 'bay-auto-main-onebounce.png')
box = (500, 360, 1040, 620)
crops = [(f'{lab}, {stops(t):.3f} stops', Image.open(S / t / 'c-s19.png').convert('RGB').crop(box))
         for lab, t in (('main fixed', 'bay-0.25'), ('one bounce fixed', 'bay-ob'), ('main auto', 'bay-auto'), ('one bounce auto', 'bay-ob-auto'))]
sheet(crops[:2], O / 'back-wall-fixed.png'); sheet(crops[2:], O / 'back-wall-auto.png')
