# Tool code of t1-nyjl: 1:1 crops of the bay's regions: the full picture, the diffuse signal alone and the specular signal
# alone (the probe's TERTIUS_PROBE_SIGNAL 2 and 3), capture 19 of each take at -4.089 stops, side by side; a fourth
# panel shows the specular signal times 8 in linear light, so that its grain shows. Usage: python3 -I signalcrops.py <out>
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
S = Path('/private/tmp/t1-wl7c/guide/stills'); out = Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
boxes = json.load(open('/private/tmp/t1-wl7c/guide/daub/boxes.json'))
def brighten(img, k):
    a = np.asarray(img).astype(np.float64) / 255
    l = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) * k
    e = np.where(l <= 0.0031308, l * 12.92, 1.055 * np.clip(l, 0, 1) ** (1 / 2.4) - 0.055)
    return Image.fromarray((np.clip(e, 0, 1) * 255 + 0.5).astype(np.uint8))
panels = [('full picture (main)', 'bay-0.25', 1), ('diffuse signal alone', 'bay-diffuse-0.25', 1),
          ('specular signal alone', 'bay-specular-0.25', 1), ('specular alone x8 in linear light', 'bay-specular-0.25', 8)]
for name, (x0, y0, x1, y1) in boxes.items():
    w, h = x1 - x0, y1 - y0
    sheet = Image.new('RGB', (4 * w + 24, h + 22), 'white'); d = ImageDraw.Draw(sheet)
    for i, (lab, take, k) in enumerate(panels):
        c = Image.open(S / take / 'c-s19.png').convert('RGB').crop((x0, y0, x1, y1))
        sheet.paste(brighten(c, k) if k != 1 else c, (i * (w + 8), 22))
        d.text((i * (w + 8) + 4, 5), lab, fill='black')
    sheet.save(out / f"signals-{name.replace(' ', '-')}.png")
print('ok')
