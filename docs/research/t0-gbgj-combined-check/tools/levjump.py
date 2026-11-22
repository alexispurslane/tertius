# Tool code of t0-gbgj. levjump.py <label|PNG folder|size log>...: the jumps of the light level at render size changes
# in the switch cabin's film (film frames 140 to 459, film frame k is draw 33 + k). The frames, the three crops, the
# display-linear blocks of 16 x 16 px and the spans are those of boilnoise.py of b53c037f1 (t0-5tyj), copied here.
# The level of a crop at a draw is the mean of its blocks. Per crop and span, two measures, each on the draws whose
# render size changed against the draw before and on the draws of one size with the draw before:
#   - rise: 100 * (level[k] - level[k - 1]) / level[k - 1], in % of the level. A fade also gives a rise.
#   - kink: 100 * (level[k] - (level[k - 1] + level[k + 1]) / 2) / level[k], the level against the straight line through
#     its two neighbouring draws. A linear fade gives zero.
# It prints the largest |rise| and the mean |rise|, then the largest |kink| and the mean |kink|, each as
# "changed / one size", with the number of draws of each kind in the span.
import sys
import numpy as np
from PIL import Image

CROPS = {'floor': (520, 700, 100, 1180), 'left wall': (150, 330, 60, 300), 'ceiling': (40, 180, 300, 980)}
SPANS = {'before off': range(1, 19), 'off 0-60': range(21, 80), 'off 60-200': range(80, 219), 'on 0-100': range(221, 319)}


def level(path, crop):
    y0, y1, x0, x1 = crop
    out = []
    for k in range(140, 460):
        a = np.asarray(Image.open(f'{path}/f-{k:06d}.png').convert('L').resize((1280, 720), Image.LANCZOS)).astype(float)
        a = a[y0:y1, x0:x1] / 255
        a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
        h, w = a.shape
        out.append(a[:h // 16 * 16, :w // 16 * 16].reshape(h // 16, 16, w // 16, 16).mean((1, 3)).mean())
    return np.array(out)


for label, path, size_log in (s.split('|') for s in sys.argv[1:]):
    sizes = [tuple(l.split()[1:3]) for l in open(size_log)]
    changed = np.array([sizes[173 + k] != sizes[172 + k] for k in range(320)])
    print(f'{label}:')
    for name, crop in CROPS.items():
        lv = level(path, crop)
        rise = np.full(320, np.nan)
        rise[1:] = 100 * np.diff(lv) / lv[:-1]
        kink = np.full(320, np.nan)
        kink[1:-1] = 100 * (lv[1:-1] - (lv[:-2] + lv[2:]) / 2) / lv[1:-1]
        parts = []
        for span, r in SPANS.items():
            r = np.array(list(r))
            cells = []
            for v in (rise, kink):
                m, s = r[changed[r]], r[~changed[r]]
                f = lambda ks: (f'{np.nanmax(np.abs(v[ks])):.2f}/{np.nanmean(np.abs(v[ks])):.2f}' if len(ks) else '-')
                cells.append(f'{f(m)} against {f(s)}')
            parts.append(f'{span} ({int(changed[r].sum())}/{int((~changed[r]).sum())}): rise {cells[0]}, kink {cells[1]}')
        print(f'  {name}: ' + ' | '.join(parts))
