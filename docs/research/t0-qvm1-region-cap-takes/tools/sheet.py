# Tool code of the region cap's takes (t0-qvm1). sheet.py <film> <first draw> <x0,y0,x1,y1> <draw,...> <out.png>: 1:1
# crops of a film.py film at the listed draws, main (pm) above the cap (pc). Run it in the dev shell.
import sys
from pathlib import Path
from PIL import Image, ImageDraw
F = Path('/private/tmp/t0-qvm1c/films')
film, first, box, draws, out = sys.argv[1], int(sys.argv[2]), tuple(map(int, sys.argv[3].split(','))), list(map(int, sys.argv[4].split(','))), sys.argv[5]
w, h = box[2] - box[0], box[3] - box[1]
sheet = Image.new('RGB', (len(draws) * (w + 6), 2 * (h + 22)), 'white')
d = ImageDraw.Draw(sheet)
for r, (b, label) in enumerate((('pm', 'main'), ('pc', 'cap'))):
    files = sorted((F / f'{b}-{film}').glob('*.png'))
    for c, dr in enumerate(draws):
        sheet.paste(Image.open(files[dr - first]).convert('RGB').crop(box), (c * (w + 6), r * (h + 22) + 20))
        d.text((c * (w + 6) + 4, r * (h + 22) + 4), f'{label}, draw {dr}', fill='black')
sheet.save(out)
