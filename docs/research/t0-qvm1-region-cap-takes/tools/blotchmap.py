# Tool code of the region cap's takes (t0-qvm1). blotchmap.py <view> <capture>: for main (pm) and the cap (pc), the still
# capture of the still.py run beside its blurred relative difference from the mean of captures 8 to 27 (the survey's blotch
# before its percentile; red brighter, blue darker, full at 3 %), into /private/tmp/t0-qvm1c/pics/blotch-<view>-<capture>.png.
# Run it in the dev shell from the checkout's root.
import importlib.util, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
spec = importlib.util.spec_from_file_location('survey', Path('tools/renderer-survey.py'))
survey = importlib.util.module_from_spec(spec); spec.loader.exec_module(survey)
R = Path('/private/tmp/t0-qvm1c/runs')
view, cap = sys.argv[1], int(sys.argv[2])
rows = []
for b, label in (('pm', 'main'), ('pc', 'region cap')):
    f = R / f'{b}-{view}' / 'frames'
    ref = sum(survey.luminance(f / f'v-s{i:02d}.png') for i in range(8, 28)) / 20
    y = survey.luminance(f / f'v-s{cap:02d}.png')
    bl = survey.blur(ref)
    rel = (survey.blur(y) / (1 + survey.level(y, ref)) - bl) / (bl + survey.RELATIVE_FLOOR)
    mask = survey.visible(ref) & ~survey.near_emitters(ref)
    v = np.clip(rel / 0.03, -1, 1) * mask
    rgb = np.zeros((*v.shape, 3), np.uint8) + 128
    rgb[..., 0] = np.clip(128 + 127 * v, 0, 255); rgb[..., 2] = np.clip(128 - 127 * v, 0, 255); rgb[..., 1] = 128 - (np.abs(v) * 60).astype(np.uint8)
    pic = Image.open(f / f'v-s{cap:02d}.png').convert('RGB')
    row = Image.new('RGB', (pic.width * 2 + 10, pic.height + 30), 'white')
    row.paste(pic, (0, 30)); row.paste(Image.fromarray(rgb), (pic.width + 10, 30))
    ImageDraw.Draw(row).text((6, 8), f'{view} {label}, capture {cap} (draw {33*cap+32}): the still, and its blurred relative difference from the late mean (red = brighter, blue = darker, full at 3 %)', fill='black')
    rows.append(row)
s = Image.new('RGB', (rows[0].width, rows[0].height * 2), 'white')
s.paste(rows[0], (0, 0)); s.paste(rows[1], (0, rows[0].height))
s = s.resize((s.width // 2, s.height // 2), Image.LANCZOS)
s.save(f'/private/tmp/t0-qvm1c/pics/blotch-{view}-{cap}.png')
