# Tool code of t0-gbgj. layerteeth.py <out.png> <earlier sheet> <frame> <label=tile:N|run>...: does the back-face layer change
# the slit's teeth (bug tc-6v3d)? The earlier sheet is the acceptance lead's film-bay-sunbeam-slit.png
# (docs/research/design-x-acceptance-2026-11-22/pics of t0-5tyj): tiles of 810 x 1080 pixels with 8 pixels between them,
# under a label of 24 pixels, each the crop x 150 to 420, y 0 to 360 of the film's middle frame at 3x nearest: tile 0 is
# main c4c1f1f2f, tile 1 Design X alone. "tile:N" takes tile N and reads every third pixel back to the 1:1 crop. "run"
# takes frame <frame> of a run whose frames film.py kept (s/<run>/frames). For each, teeth.py's edge trace prints the
# tooth tips (rows and depths in output pixels), and the sheet shows the four crops at 3x nearest, labels on top.
import glob, sys
import numpy as np
from PIL import Image, ImageDraw
from teeth import edge, offset, tips, X0, X1, Y0, Y1, ZOOM
def lum(rgb):
    a = rgb.astype(float) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]
out, sheet_path, frame = sys.argv[1], sys.argv[2], int(sys.argv[3])
earlier = np.asarray(Image.open(sheet_path).convert('RGB'))
tiles = []
for arg in sys.argv[4:]:
    label, src = arg.split('=', 1)
    if src.startswith('tile:'):
        x = 818 * int(src[5:])
        crop = earlier[24 + 1:24 + 1080:3, x + 1:x + 810:3]
    else:
        f = sorted(glob.glob(f's/{src}/frames/m*-*.png'))[frame]
        crop = np.asarray(Image.open(f).convert('RGB').crop((X0, Y0, X1, Y1)))
    rows, off = offset(edge(lum(crop)))
    t = tips(rows, off)
    print(f"{label}: {len(t)} tips, rows/depths {t}")
    im = Image.fromarray(crop.astype(np.uint8)).resize((crop.shape[1] * ZOOM, crop.shape[0] * ZOOM), Image.NEAREST)
    tile = Image.new('RGB', (im.width, im.height + 22)); tile.paste(im, (0, 22))
    ImageDraw.Draw(tile).text((4, 4), f'{label}: film frame {frame}, x {X0}-{X1}, y {Y0}-{Y1}, 3x nearest', fill=(255, 255, 255))
    tiles.append(tile)
sheet = Image.new('RGB', (sum(t.width + 6 for t in tiles), tiles[0].height), (40, 40, 40)); x = 0
for t in tiles: sheet.paste(t, (x, 0)); x += t.width + 6
sheet.save(out); print(out)
