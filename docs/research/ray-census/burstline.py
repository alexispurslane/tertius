# Tool code of ter-8pco's lamp-hall part (for t7-3e22): the picture of the burst line. Reads the hit-light dumps of the
# takes burst-1072x603 and burst-1040x585 (RayCensus with TERTIUS_PROBE_CENSUS_DUMP_AT=606,630,631,632) and writes:
#   - top: the diffuse hit light of 1072 x 603 at draw 631 (a burst draw), whole frame, with a red box around the far
#     window's lower edge;
#   - below: that box at nearest sampling, 6 x, for 1072 x 603 at draws 630 (quiet) and 631 (burst), and for 1040 x 585
#     at draw 631, the same piece of the screen.
# Gray = log2 of the luminance of the diffuse hit light, from -6 (black) to +8 (white); the ceiling's 0.22 is gray 104,
# and the line's 162 is white (clipped at 256).
# Usage: python3 burstline.py <takes dir> <out png>
import json, sys
import numpy as np
from PIL import Image, ImageDraw

L = np.array([0.2126, 0.7152, 0.0722])
takes, out = sys.argv[1], sys.argv[2]


def get(take, draw):
    w, h = json.load(open(f'{takes}/{take}/census.json'))['Viewport']
    iw, ih, ch = json.load(open(f'{takes}/{take}/census.d{draw}.dump.json'))['hit-diffuse']
    a = np.fromfile(f'{takes}/{take}/census.d{draw}.hit-diffuse.f32', '<f4').reshape(ih, iw, ch)[:h, :w, :3] @ L
    return a, w


def gray(a):
    return (np.clip((np.log2(np.maximum(a, 1e-4)) + 6) / 14, 0, 1) * 255).astype(np.uint8)


box = (470, 300, 600, 330)  # render px of 1072 x 603
full, _ = get('burst-1072x603', 631)
top = Image.fromarray(gray(full)).convert('RGB')
ImageDraw.Draw(top).rectangle(box, outline=(255, 0, 0), width=2)
crops = []
for take, draw in (('burst-1072x603', 630), ('burst-1072x603', 631), ('burst-1040x585', 631)):
    a, w = get(take, draw)
    s = w / 1072
    x0, y0, x1, y1 = (round(v * s) for v in box)
    c = Image.fromarray(gray(a[y0:y1, x0:x1])).resize((130 * 6, 30 * 6), Image.NEAREST).convert('RGB')
    crops.append(c)
W = max(top.width, 780)
canvas = Image.new('RGB', (W, top.height + 3 * (180 + 8) + 8), 'white')
canvas.paste(top, (0, 0))
for i, c in enumerate(crops):
    canvas.paste(c, (0, top.height + 8 + i * 188))
canvas.save(out)
