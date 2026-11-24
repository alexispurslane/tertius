# Tool code of ter-fe63: the reflection kind's guided share by roughness, from the census's log of reflection samples
# (145-byte records with the reflection fields and layer 25; the build traces lobe draws only). For a sample X drawn with the lobe
# density q_l, the second moment under the mixture at share s is the mean of X^2 q_l / q_s, q_s = (1 - s) q_l + s q_g,
# q_g the ray's guided density along its direction. X is the reflection part's luminance as the sample takes it,
# uncapped (layer 8's z). Per roughness class (layer 7's w, sqrt of the floored alpha, in bins of 0.02) and share: the
# variance pooled over the class's pixels, as a factor against s = 0, and the samples above 16 times their pixel's mean
# per draw, and the relative variance (the pooled variance over the pooled squared mean). A pixel's samples are its rows in the keep file: record i belongs to pixel i mod (the region's pixel count).
# Usage: python3 -I ramp.py <take dir> <name=width x height in render pixels>...
import json, sys
from pathlib import Path
import numpy as np
take = Path(sys.argv[1])
fields = [('x', '<f4'), ('pre', '<f4'), ('bin', 'u1'), ('xs', '<f4'), ('ds', '<f4'),
          ('r6', '<f4', 4), ('r7', '<f4', 4), ('r8', '<f4', 4), ('r9', '<f4', 4),
          ('d14', '<f4', 4), ('d15', '<f4', 4), ('d22', '<f4', 4), ('d25', '<f4', 4)]
shares = (0.0, 1 / 32, 0.0625, 0.1, 0.125, 0.25, 0.5, 0.75)
out = {}
for k, arg in enumerate(sys.argv[2:]):
    name, size = arg.split('=')
    w, h = map(int, size.split('x')); npix = w * h
    rec = np.fromfile(take / f'census.keep{k}', dtype=np.dtype(fields))
    draws = len(rec) // npix
    rec = rec[:draws * npix]
    starts = rec['r6'][:, 0] != -1.0
    X = np.where(starts, rec['r8'][:, 2], 0.0).astype(np.float64).reshape(draws, npix)
    ql = np.exp2(rec['r6'][:, 2].astype(np.float64)).reshape(draws, npix)
    qg = np.where(rec['r6'][:, 3] <= -99, 0.0, np.exp2(rec['r6'][:, 3].astype(np.float64))).reshape(draws, npix)
    rough = rec['r7'][:, 3].reshape(draws, npix)[0]
    has = starts.reshape(draws, npix).all(0)
    mean = X.mean(0)
    classes = {}
    for lo in np.arange(0.0, 1.0, 0.02):
        sel = has & (rough >= lo) & (rough < lo + 0.02)
        if sel.sum() < 50:
            continue
        row = {'pixels': int(sel.sum())}
        base = None
        for s in shares:
            ratio = np.where(X[:, sel] > 0, ql[:, sel] / np.maximum((1 - s) * ql[:, sel] + s * qg[:, sel], 1e-30), 0.0)
            m2 = (X[:, sel] ** 2 * ratio).mean(0)
            var = float((m2 - mean[sel] ** 2).mean())
            base = base or var
            # A sample's value under the mixture is X q_l / q_s; its tail against the pixel's mean.
            tail = float(((X[:, sel] * ratio) > 16 * mean[sel]).sum() / draws)
            row[f's {s}'] = {'variance factor': var / base if base else None, 'above 16x mean per draw': tail,
                             'relative variance': var / float((mean[sel] ** 2).mean())}
        classes[f'{lo:.2f}-{lo + 0.02:.2f}'] = row
    out[name] = {'draws': draws, 'classes': classes}
(take / 'ramp-result.json').write_text(json.dumps(out, indent=1))
print(json.dumps(out, indent=1))
