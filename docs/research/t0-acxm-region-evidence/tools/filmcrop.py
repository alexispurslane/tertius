# filmcrop.py <out.png> <sample> <x0,y0,x1,y1> <label=run[:still]>...: a 1:1 crop of the film rect-bay's <sample> frame
# (middle or end) for each argument, side by side, labels on top. "run:still" takes the still of the same state that the
# survey set the frame against. Run it through m.sh.
import sys
from PIL import Image, ImageDraw
out, sample, box = sys.argv[1], sys.argv[2], [int(c) for c in sys.argv[3].split(',')]
w, h = box[2] - box[0], box[3] - box[1]
tiles = []
for arg in sys.argv[4:]:
    label, v = arg.split('=', 1)
    name = f'rect-bay-{sample}-still.png' if v.endswith(':still') else f'rect-bay-{sample}.png'
    im = Image.open(f"s/{v.split(':')[0]}/images/motion/{name}").convert('RGB').crop(box)
    tile = Image.new('RGB', (w, h + 22)); tile.paste(im, (0, 22))
    ImageDraw.Draw(tile).text((4, 5), f'{label}, {"last" if sample == "end" else sample} frame, 1:1', fill=(255, 255, 255))
    tiles.append(tile)
sheet = Image.new('RGB', (len(tiles) * (w + 6) - 6, h + 22), (60, 60, 60))
for i, t in enumerate(tiles):
    sheet.paste(t, (i * (w + 6), 0))
sheet.save(out); print(out, sheet.size)
