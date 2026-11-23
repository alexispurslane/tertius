# floorcrop.py <out.png>: the film rect-bay's last frame at 1:1, output pixels 700 to 1100 by 600 to 1000 (the floor
# above the hole), for the still of the same state and the sides M, D4, D8 and D4s. Run it through m.sh.
import sys
from PIL import Image, ImageDraw
sides = [('still', 't-M-rb', 'rect-bay-end-still.png'), ('M', 't-M-rb', 'rect-bay-end.png'),
         ('D4', 't-D4-rb', 'rect-bay-end.png'), ('D8', 't-D8-rb', 'rect-bay-end.png'), ('D4s', 't-D4s-rb', 'rect-bay-end.png')]
tiles = []
for label, v, name in sides:
    im = Image.open(f's/{v}/images/motion/{name}').convert('RGB').crop((700, 600, 1100, 1000))
    tile = Image.new('RGB', (400, 422)); tile.paste(im, (0, 22))
    ImageDraw.Draw(tile).text((4, 5), f'{label}, last frame, floor at 1:1', fill=(255, 255, 255)); tiles.append(tile)
sheet = Image.new('RGB', (len(tiles) * 406 - 6, 422), (60, 60, 60))
for i, t in enumerate(tiles):
    sheet.paste(t, (i * 406, 0))
sheet.save(sys.argv[1]); print(sys.argv[1], sheet.size)
