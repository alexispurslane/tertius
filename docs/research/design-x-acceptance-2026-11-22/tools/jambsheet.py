# Tool code of t0-5tyj. jambsheet.py <out prefix> <label|run folder|reference folder>...: the jambs and the floor's side
# of the switch cabin at six moments, from macdyn.sh's picture runs (pics/<name>/f-<frame>.png, 1920 x 1080, ACES and
# bloom, the fixed exposure of 4.08 stops). The moments are the film frames 150 (10 draws before the lamps go off), 180,
# 220 and 355 (20, 60 and 195 draws after off), 380 and 455 (20 and 95 draws after they come on again).
# The crops, in output pixels:
#   - the jamb: x 1770 to 1807, y 495 to 584, the lit side faces of the nearest window opening on the right wall;
#   - the floor's side: x 0 to 449, y 840 to 1049, the left part of the floor near the camera.
# Per run and moment it prints:
#   - jamb: the crop's mean display-linear luminance, as % of the same crop of the reference folder at that moment (the
#     clamps-off reference, TERTIUS_A_NO_CLAMP=1 on X, at the same render scale);
#   - specks: the survey's speck score of the floor crop (renderer-survey.py specks: groups of speck pixels by
#     ter-9371's rule with no speck of the reference within 2 px, per 10,000 visible pixels), against the reference's
#     floor crop at that moment.
# It writes <out prefix>-jamb.png (the jamb crops at 4 times their size) and <out prefix>-floor.png (the floor crops at
# their size): one row per moment, one column per run.
import importlib.util
import os
import sys
import numpy as np
from PIL import Image, ImageDraw

spec = importlib.util.spec_from_file_location('survey', os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                                                     '../../../../tools/renderer-survey.py'))
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)

prefix, runs = sys.argv[1], [a.split('|') for a in sys.argv[2:]]
MOMENTS = [(150, '10 before off'), (180, '20 after off'), (220, '60 after off'), (355, '195 after off'),
           (380, '20 after on'), (455, '95 after on')]
JAMB = (1770, 495, 1808, 585)
FLOOR = (0, 840, 450, 1050)


def picture(folder, frame):
    return Image.open(f'{folder}/f-{frame:06d}.png').convert('RGB')


def luminance(im):
    a = np.asarray(im).astype(float) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]


for kind, box, zoom in (('jamb', JAMB, 4), ('floor', FLOOR, 1)):
    w, h = (box[2] - box[0]) * zoom, (box[3] - box[1]) * zoom
    sheet = Image.new('RGB', (130 + len(runs) * (w + 10), 30 + len(MOMENTS) * (h + 10)), 'white')
    d = ImageDraw.Draw(sheet)
    for c, (label, folder, _) in enumerate(runs):
        d.text((130 + c * (w + 10), 8), label, fill='black')
    for r, (frame, name) in enumerate(MOMENTS):
        d.text((6, 30 + r * (h + 10) + h // 2), name, fill='black')
        for c, (label, folder, _) in enumerate(runs):
            crop = picture(folder, frame).crop(box).resize((w, h), Image.NEAREST)
            sheet.paste(crop, (130 + c * (w + 10), 30 + r * (h + 10)))
    sheet.save(f'{prefix}-{kind}.png')
    print(f'{prefix}-{kind}.png', sheet.size)

print('moment'.ljust(16) + ''.join(label[:22].rjust(24) for label, _, _ in runs))
for frame, name in MOMENTS:
    cells = []
    for label, folder, reference in runs:
        y = luminance(picture(folder, frame))
        ref = luminance(picture(reference, frame))
        jamb = y[JAMB[1]:JAMB[3], JAMB[0]:JAMB[2]].mean() / ref[JAMB[1]:JAMB[3], JAMB[0]:JAMB[2]].mean()
        floor = survey.specks(y[FLOOR[1]:FLOOR[3], FLOOR[0]:FLOOR[2]], ref[FLOOR[1]:FLOOR[3], FLOOR[0]:FLOOR[2]])
        cells.append(f'jamb {100 * jamb:5.1f}% sp {floor:6.2f}')
    print(name.ljust(16) + ''.join(c.rjust(24) for c in cells))
