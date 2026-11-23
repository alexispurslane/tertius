# Tool code of t0-gbgj. layerdiff.py <out.png> <case> <zoom> <label=before:after>...: where the back-face layer changed a
# survey still. The two stills differ by their automatic exposure too, so the tool takes that out first: in
# display-linear luminance (the 8-bit sRGB decoded), it scales <after> by the median of before / after over the visible
# pixels (before above 0.01), the exposure's ratio. Then each pixel's relative change is after / before - 1, both blurred
# by a Gaussian of 2 output pixels so that noise does not hide a change of a surface. The tile maps it: gray is no
# change, white is +25 % or more (brighter with the layer), black is -25 % or less. Printed per pair: the exposure's
# ratio, and the share of visible pixels that changed by more than 10 %, brighter and darker. m.sh runs it in $W/w.
import sys
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter
def lin(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(float) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]
out, case, zoom = sys.argv[1], sys.argv[2], float(sys.argv[3])
tiles = []
for arg in sys.argv[4:]:
    label, pair = arg.split('=', 1); a, b = pair.split(':')
    before = gaussian_filter(lin(f's/{a}/images/still/{case}.png'), 2)
    after = gaussian_filter(lin(f's/{b}/images/still/{case}.png'), 2)
    vis = before > 0.01
    k = np.median(before[vis] / np.maximum(after[vis], 1e-4))
    rel = np.where(vis, after * k / np.maximum(before, 1e-4) - 1, 0)
    up, down = (rel > 0.10) & vis, (rel < -0.10) & vis
    print(f"{label}: exposure ratio {k:.3f}; brighter by over 10 %: {up.sum() / vis.sum():.4f} of visible pixels, "
          f"darker: {down.sum() / vis.sum():.4f}")
    im = Image.fromarray(np.clip(128 + rel * 512, 0, 255).astype(np.uint8)).convert('RGB')
    im = im.resize((int(im.width * zoom), int(im.height * zoom)), Image.LANCZOS)
    tile = Image.new('RGB', (im.width, im.height + 22)); tile.paste(im, (0, 22))
    ImageDraw.Draw(tile).text((4, 4), label, fill=(255, 255, 255)); tiles.append(tile)
sheet = Image.new('RGB', (sum(t.width + 6 for t in tiles), tiles[0].height), (40, 40, 40)); x = 0
for t in tiles: sheet.paste(t, (x, 0)); x += t.width + 6
sheet.save(out); print(out)
