# Tool code of ter-bcwr. scalechart.py <card job folder> <out.png>: the requested render scale per draw of card.sh's runs,
# one panel per event (start: draws 0 to 60; cut: draws 134 - 5 to 134 + 60), today in orange and the build in blue, one
# line per round, with the rest of the build's first round less 0.02 as a dashed line. A red line marks each reset: the
# start's draw 0, and in the cut both camera changes of cut.json, the one frame on the chase camera (draw 133) and the cut
# back to the person camera (draw 134). Each point sits on the draw that drew at that scale. Pillow only.
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root, out = Path(sys.argv[1]) / 'out' / 'card', sys.argv[2]
EVENTS = (('start', 0), ('cut', 134))
RESETS = {'start': ((0, 'start'),), 'cut': ((-1, 'chase camera'), (0, 'cut back'))}
W, H, PAD = 900, 420, 60
LO, HI = 0.70, 1.01
COLORS = {'today': (214, 120, 20), 'build': (40, 100, 200)}
try:
    font = ImageFont.load_default(size=18)
except TypeError:
    font = ImageFont.load_default()
image = Image.new('RGB', (W, H * len(EVENTS)), 'white')
draw = ImageDraw.Draw(image)
for panel, (event, at) in enumerate(EVENTS):
    top = panel * H
    first, last = -5, 60
    x = lambda k: PAD + (k - first) * (W - 2 * PAD) / (last - first)
    y = lambda s: top + PAD + (HI - s) * (H - 2 * PAD) / (HI - LO)
    draw.rectangle([PAD, top + PAD, W - PAD, top + H - PAD], outline=(160, 160, 160))
    for s in (0.70, 0.75, 0.80, 0.85, 0.90, 0.95, 1.00):
        draw.line([PAD, y(s), W - PAD, y(s)], fill=(230, 230, 230))
        draw.text((8, y(s) - 9), f'{s:.2f}', fill='black', font=font)
    for k in range(0, last + 1, 8):
        draw.line([x(k), top + PAD, x(k), top + H - PAD], fill=(235, 235, 235))
        draw.text((x(k) - 8, top + H - PAD + 6), str(k), fill='black', font=font)
    draw.text((PAD, top + 12), f'{event}: requested render scale per draw since the {event} (draw {at})', fill='black',
              font=font)
    for n, (k, words) in enumerate(RESETS[event]):
        draw.line([x(k), top + PAD, x(k), top + H - PAD], fill=(200, 30, 30), width=1)
        draw.text((x(k) + 6, y(0.745) - 24 * n), f'reset: {words}', fill=(200, 30, 30), font=font)
    for code in ('today', 'build'):
        for folder in sorted(root.glob(f'{event}-{code}-r*')):
            scale = {}
            for line in open(next(folder.glob('size-*.txt'))):
                scale.setdefault(int(line.split()[0]), float(line.split()[3]))
            points = [(x(k), y(scale[at + k])) for k in range(first, last + 1) if at + k in scale]
            draw.line(points, fill=COLORS[code], width=2)
            if code == 'build' and folder.name.endswith('r1'):
                rest = sorted(scale[d] for d in sorted(scale)[-40:])[20]
                for k in range(first, last, 2):
                    draw.line([x(k), y(rest - 0.02), x(k + 1), y(rest - 0.02)], fill=(90, 90, 90))
    draw.text((W - PAD - 330, top + H - PAD - 60), 'orange: today (c776bed47), 3 rounds', fill=COLORS['today'], font=font)
    draw.text((W - PAD - 330, top + H - PAD - 38), 'blue: the build, 3 rounds; dashed: rest - 0.02', fill=COLORS['build'],
              font=font)
image.save(out)
