# Tool code of t0-gbgj. wintest.py: the cabin's window jambs at a window of any shape, with the render scale held at 1,
# so that the render size equals the window. The line orchestrator's test of the reading that the jamb's leak follows
# the aspect error of the render size, (width / height) / (16 / 9) - 1, whatever causes that aspect.
#   wintest.py prepare <folder> <W> <H> [N]: script.json (the view of the acceptance lead's sizeramp.py of b53c037f1:
#     scene orbit, camera person, 60 ticks, the still, then a film "f" of N draws, the lamps on, the window W x H),
#     replay.txt (every draw at the scale 1.0, through TERTIUS_X_SIZE_REPLAY) and keep.txt (the last film frame).
#   wintest.py measure <folder> <W> <H>: the display-linear mean of the right and the left jamb crop of sizeramp.py,
#     scaled to the window. The vertical field of view is fixed (SceneBatcher.DefaultVerticalFov), so a point's offset
#     from the picture's center grows with the window's height: x' = W / 2 + (x - 960) H / 1080, y' = y H / 1080. It
#     prints one line "W H aspect-error right left", and writes <folder>/opening.png, the near right opening at 2x.
import json
import sys
import numpy as np
from PIL import Image

RIGHT, LEFT, OPENING = (1770, 495, 1808, 585), (113, 500, 146, 580), (1700, 440, 1920, 640)


def box(b, w, h):
    s = h / 1080
    return tuple(int(round(w / 2 + (b[i] - 960) * s)) if i % 2 == 0 else int(round(b[i] * s)) for i in range(4))


def luminance(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(float) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]


mode, folder, w, h = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4])
n = int(sys.argv[5]) if len(sys.argv) > 5 else 40
if mode == 'prepare':
    script = {'scene': 'orbit', 'window': {'width': w, 'height': h},
              'steps': [{'do': 'camera', 'entity': 'person'}, {'do': 'run', 'ticks': 60}, {'do': 'capture', 'name': 'still'},
                        {'do': 'film', 'name': 'f', 'frames': n, 'every': 1}]}
    json.dump(script, open(f'{folder}/script.json', 'w'), indent=1)
    open(f'{folder}/replay.txt', 'w').write(''.join(f'{d} {w} {h} 1.0\n' for d in range(33 + n)))
    open(f'{folder}/keep.txt', 'w').write(f'f-{n - 1:06d}')
else:
    y = luminance(f'{folder}/frames/f-{n - 1:06d}.png')
    r, l = box(RIGHT, w, h), box(LEFT, w, h)
    m = lambda b: float(y[b[1]:b[3], b[0]:b[2]].mean())
    print(f'{w} {h} {(w / h) / (16 / 9) - 1:+.4%} {m(r):.4f} {m(l):.4f}  right box {r} left box {l}')
    o = box(OPENING, w, h)
    Image.open(f'{folder}/frames/f-{n - 1:06d}.png').convert('RGB').crop(o).resize(((o[2] - o[0]) * 2, (o[3] - o[1]) * 2), Image.NEAREST).save(f'{folder}/opening.png')
