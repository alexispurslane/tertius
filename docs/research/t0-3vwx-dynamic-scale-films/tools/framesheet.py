# Tool code of t0-3vwx. framesheet.py <out.png> <scale> <frame,...> <label|run folder>...: one row per kept frame (named
# as the run keeps it, such as f-000220), one column per run, each frame scaled by <scale> with Lanczos, the frame's name
# and each run's label above.
import sys
from PIL import Image, ImageDraw

out, scale, names, specs = sys.argv[1], float(sys.argv[2]), sys.argv[3].split(','), [s.split('|') for s in sys.argv[4:]]
w, h = (round(v * scale) for v in Image.open(f'{specs[0][1]}/{names[0]}.png').size)
sheet = Image.new('RGB', (len(specs) * (w + 6), len(names) * (h + 24) + 24), 'white')
d = ImageDraw.Draw(sheet)
for c, (label, _) in enumerate(specs):
    d.text((c * (w + 6) + 4, 4), label, fill='black')
for r, name in enumerate(names):
    for c, (label, folder) in enumerate(specs):
        tile = Image.open(f'{folder}/{name}.png').convert('RGB').resize((w, h), Image.LANCZOS)
        sheet.paste(tile, (c * (w + 6), 24 + r * (h + 24) + 20))
    d.text((4, 24 + r * (h + 24) + 4), name, fill='black')
sheet.save(out)
print(out, sheet.size)
