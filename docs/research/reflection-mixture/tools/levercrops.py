# Tool code of t1-nyjl: the diffuse lever at 1:1. The diffuse signal alone (the probe's TERTIUS_PROBE_SIGNAL 2), capture 19
# at -4.089 stops, of three takes: cap 32 with all writers (main), cap 128 with all writers, cap 128 with region evidence
# off. One sheet per region, each column captioned with its blob number. Usage: python3 -I levercrops.py <out dir>
import sys
from PIL import Image, ImageDraw
G = '/private/tmp/t1-wl7c/guide/stills'; F = '/private/tmp/t1-wl7c/fe63'
takes = [('cap 32, all writers (main)', f'{G}/bay-diffuse-0.25'), ('cap 128, all writers', f'{F}/d128-on'),
         ('cap 128, region evidence off', f'{F}/d128-noregion')]
regions = {'floor': ((600, 650, 1200, 950), (0.0108, 0.0093, 0.0041)),
           'back-wall': ((560, 420, 980, 560), (0.0096, 0.0075, 0.0040)),
           'right-wall': ((1300, 100, 1800, 500), (0.0065, 0.0063, 0.0022))}
for name, (box, blobs) in regions.items():
    w, h = box[2] - box[0], box[3] - box[1]
    s = Image.new('RGB', (3 * (w + 8) - 8, h + 34), 'white'); d = ImageDraw.Draw(s)
    for i, ((label, t), b) in enumerate(zip(takes, blobs)):
        s.paste(Image.open(f'{t}/c-s19.png').convert('RGB').crop(box), (i * (w + 8), 34))
        d.text((i * (w + 8) + 4, 3), f'diffuse alone, {label}', fill='black')
        d.text((i * (w + 8) + 4, 17), f'blob {b:.4f}, -4.089 stops, capture 19', fill='black')
    s.save(f'{sys.argv[1]}/lever-{name}.png')
