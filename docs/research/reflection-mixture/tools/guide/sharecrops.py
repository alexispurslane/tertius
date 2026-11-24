# Tool code of t1-nyjl: 1:1 crops of the bay's census regions in the stills at the guided shares 0.25 (main), 0.5 and
# 0.75, side by side with a label band, one PNG per region. Usage: python3 -I sharecrops.py <daub folder> <out folder>
import json, sys
from pathlib import Path
from PIL import Image, ImageDraw
src, out = Path(sys.argv[1]), Path(sys.argv[2]); out.mkdir(parents=True, exist_ok=True)
boxes = json.loads((src / 'boxes.json').read_text())
for name, (x0, y0, x1, y1) in boxes.items():
    w, h = x1 - x0, y1 - y0
    sheet = Image.new('RGB', (3 * w + 16, h + 22), 'white')
    d = ImageDraw.Draw(sheet)
    for i, s in enumerate(('0.25', '0.5', '0.75')):
        sheet.paste(Image.open(src / f's{s}' / 'c-s19.png').convert('RGB').crop((x0, y0, x1, y1)), (i * (w + 8), 22))
        d.text((i * (w + 8) + 4, 5), f'guided share {s}' + (' (main)' if s == '0.25' else '') + ', bay-sunbeam 2/3, -4.089 stops, capture 19', fill='black')
    sheet.save(out / f"bay-{name.replace(' ', '-')}.png")
print('ok')
