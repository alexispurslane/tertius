# blotchmap.py <out.png> <label=variant>...: what the survey's blotch score counts in each variant's still rect-lamps.
# For each still, the survey's blotch map: the blurred still, divided by its whole level, against its own build's
# blurred long reference (tools/renderer-survey.py, blotch), as a signed relative difference per output pixel, shown at
# half size on one diverging scale from -5 % (blue) through 0 (gray) to +5 % (red). Pixels that the score leaves out
# (not visible, or near an emitter) are black. Under each map, and printed: the score (the 90th percentile of the
# magnitude). Run it through m.sh.
# The formula below repeats survey.blotch (tools/renderer-survey.py), which stays the source of truth: the survey returns
# only the score, and this map needs the signed difference per pixel. The map reads the stored reference, an 8-bit gray
# PNG, where the survey scored against the float mean of the captures, so its score differs from the survey's a little.
import importlib.util
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

spec = importlib.util.spec_from_file_location('survey', Path(__file__).resolve().parents[4] / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
SCALE = 0.05
tiles = []
for arg in sys.argv[2:]:
    label, v = arg.split('=', 1)
    folder = Path(f's/{v}/images/still')
    y = survey.luminance(folder / 'rect-lamps.png')
    r = survey.luminance(folder / 'rect-lamps-reference.png')
    mask = survey.visible(r) & ~survey.near_emitters(r)
    b = survey.blur(r)
    signed = (survey.blur(y) / (1 + survey.level(y, r)) - b) / (b + survey.RELATIVE_FLOOR)
    score = float(np.percentile(np.abs(signed[mask]), 90))
    print(f'{label}: map score against the 8-bit reference {score:.4f}')
    t = np.clip(signed / SCALE, -1, 1)
    rgb = np.zeros(signed.shape + (3,))
    gray = np.array([0.55, 0.55, 0.55])
    red, blue = np.array([0.85, 0.15, 0.1]), np.array([0.1, 0.3, 0.9])
    pos, neg = np.clip(t, 0, 1)[..., None], np.clip(-t, 0, 1)[..., None]
    rgb = gray * (1 - pos - neg) + red * pos + blue * neg
    rgb[~mask] = 0
    im = Image.fromarray((rgb * 255).astype(np.uint8)).resize((y.shape[1] // 2, y.shape[0] // 2), Image.BILINEAR)
    tile = Image.new('RGB', (im.width, im.height + 44), (0, 0, 0)); tile.paste(im, (0, 22))
    d = ImageDraw.Draw(tile)
    d.text((4, 5), f'{label}: still / own reference - 1, blurred, -5 % blue to +5 % red', fill=(255, 255, 255))
    d.text((4, im.height + 27), f'blotch score (90th percentile of the magnitude) {score:.4f}', fill=(255, 255, 255))
    tiles.append(tile)
sheet = Image.new('RGB', (sum(t.width for t in tiles) + 6 * (len(tiles) - 1), tiles[0].height), (60, 60, 60))
x = 0
for t in tiles:
    sheet.paste(t, (x, 0)); x += t.width + 6
sheet.save(sys.argv[1]); print(sys.argv[1], sheet.size)
