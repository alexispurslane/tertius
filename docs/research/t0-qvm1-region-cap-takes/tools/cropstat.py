# Tool code of the region cap's takes (t0-qvm1). cropstat.py: in crops.py's two boxes of the bay at draw 890, the error
# of each build's still against its own late mean (blurred, as the blotch, and per pixel), and the relative difference
# stretched to 5 % (pics/crops-bay-difference.png). Run it in the dev shell from the checkout's root.
import importlib.util
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
spec = importlib.util.spec_from_file_location('survey', Path('tools/renderer-survey.py'))
survey = importlib.util.module_from_spec(spec); spec.loader.exec_module(survey)
R = Path('/private/tmp/t0-qvm1c/runs')
boxes = [(786, 75, 1106, 295), (1477, 375, 1797, 595)]
out = []
for b in ('pm', 'pc'):
    f = R / f'{b}-bay' / 'frames'
    ref = sum(survey.luminance(f / f'v-s{i:02d}.png') for i in range(8, 28)) / 20
    y = survey.luminance(f / f'v-s26.png')
    br, by = survey.blur(ref), survey.blur(y)
    rel = (by - br) / (br + survey.RELATIVE_FLOOR)
    raw = (y - ref) / (ref + survey.RELATIVE_FLOOR)
    for k, (x0, y0, x1, y1) in enumerate(boxes):
        r = rel[y0:y1, x0:x1]; p = raw[y0:y1, x0:x1]
        print(b, 'box', k + 1, f'blurred relative difference: RMS {np.sqrt((r**2).mean())*100:.2f} %, 90th pct {np.percentile(abs(r),90)*100:.2f} %; per pixel RMS {np.sqrt((p**2).mean())*100:.2f} %')
    out.append((y, ref))
rows = []
for k, (x0, y0, x1, y1) in enumerate(boxes):
    row = Image.new('L', (650, 244), 255)
    for i, (y, ref) in enumerate(out):
        d = (y - ref)[y0:y1, x0:x1] / (ref[y0:y1, x0:x1] + 1e-3)
        row.paste(Image.fromarray(np.clip(128 + d * 128 / 0.05, 0, 255).astype(np.uint8)), (i * 330, 24))
    ImageDraw.Draw(row).text((4, 6), f'box {k + 1}: the still less the late mean, relative, gray = 0, white or black = 5 %: main left, cap right', fill=0)
    rows.append(row)
s = Image.new('L', (650, 488), 255); s.paste(rows[0], (0, 0)); s.paste(rows[1], (0, 244))
s.save('/private/tmp/t0-qvm1c/pics/crops-bay-difference.png')
