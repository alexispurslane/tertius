# Tool code of ter-fe63: the flicker of a film's box. Per take, over frames [first, end): the RMS over the box's pixels
# and the frames of the change between consecutive frames, in display gray (0 to 255); the per-pixel temporal standard
# deviation, its mean over the box; and the box's mean gray per frame, with the amplitude of its period-4 component
# (the exact count's period at a share of 1/4) against the series' standard deviation.
# Usage: python3 -I flicker.py <box x0,y0,x1,y1> <first-end> <label=take dir>...
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image
x0, y0, x1, y1 = map(int, sys.argv[1].split(',')); a, b = map(int, sys.argv[2].split('-'))
out = {}
for arg in sys.argv[3:]:
    label, d = arg.split('=')
    g = np.stack([np.asarray(Image.open(Path(d) / f'n-{k:06d}.png').convert('L'), np.float64)[y0:y1, x0:x1] for k in range(a, b)])
    m = g.mean((1, 2)); n = len(m)
    spec = np.abs(np.fft.rfft(m - m.mean())) * 2 / n
    f4 = n // 4
    out[label] = {'frame-to-frame RMS change': float(np.sqrt((np.diff(g, axis=0) ** 2).mean())),
                  'mean temporal std per pixel': float(g.std(0).mean()),
                  'box mean gray': float(m.mean()), 'std of the box mean over frames': float(m.std()),
                  'period-4 amplitude of the box mean': float(spec[f4]),
                  'largest other amplitude': float(np.delete(spec[1:], f4 - 1).max())}
print(json.dumps(out, indent=1))
