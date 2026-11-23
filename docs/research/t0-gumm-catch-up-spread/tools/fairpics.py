# Tool code of t0-gumm. fairpics.py <out folder> [fair folder]: the fair test's pictures, from the PNGs that queue-fair.sh and
# queue-fair2.sh keep (draws 2 and 8 after the start). Per scene and draw, fair-<scene>-draw<n>.png: today | none | C32 side by
# side at half size, each pane labelled with its code, the draw and the render scale 2/3; and fair-<scene>-narrow-draw<n>.png: the
# narrow crop (fairmeasure.py's CROPS) and the floor's middle, at 4 times and 1 times size, the three codes side by side.
import sys
from pathlib import Path
from PIL import Image, ImageDraw

out = Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
root = Path(sys.argv[2] if len(sys.argv) > 2 else '/private/tmp/t0-gbgj/gumm-fair')
CODES = ('today', 'none', 'C32')
NARROW = {'cabin': (156, 490, 178, 590), 'bay': (1030, 120, 1060, 460)}
FLOOR = {'cabin': (660, 800, 1260, 1040), 'bay': (560, 700, 1000, 1000)}


def label(im, text):
    g = ImageDraw.Draw(im)
    g.rectangle([0, 0, 8 * len(text) + 12, 22], fill='black')
    g.text((6, 5), text, fill='white')
    return im


for scene in ('bay', 'cabin'):
    for d in (2, 8):
        frames = [Image.open(root / f'{scene}-start-{c}' / f'f-{d:06d}.png').convert('RGB') for c in CODES]
        half = [label(f.resize((960, 540), Image.LANCZOS), f'{c}, draw {d} after the start, scale 2/3')
                for c, f in zip(CODES, frames)]
        sheet = Image.new('RGB', (3 * 960 + 20, 540), 'white')
        for i, h in enumerate(half):
            sheet.paste(h, (i * 970, 0))
        sheet.save(out / f'fair-{scene}-draw{d}.png')
        x0, y0, x1, y1 = NARROW[scene]
        nw, nh = (x1 - x0) * 4, (y1 - y0) * 4
        fx0, fy0, fx1, fy1 = FLOOR[scene]
        fw, fh = fx1 - fx0, fy1 - fy0
        h = max(nh, fh) + 30
        sheet = Image.new('RGB', (3 * (nw + fw + 30), h), 'white')
        for i, (c, f) in enumerate(zip(CODES, frames)):
            x = i * (nw + fw + 30)
            sheet.paste(f.crop(NARROW[scene]).resize((nw, nh), Image.NEAREST), (x, 30))
            sheet.paste(f.crop(FLOOR[scene]), (x + nw + 10, 30))
            ImageDraw.Draw(sheet).text((x + 4, 8), f'{c}, draw {d}: narrow crop x4, floor x1', fill='black')
        sheet.save(out / f'fair-{scene}-narrow-draw{d}.png')
print(sorted(p.name for p in out.glob('fair-*.png')))
