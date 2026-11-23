# Tool code of the line orchestrator (t1-yi11), never for src. period.py: the autocorrelation of an anchor's light at every
# lag from 1 to 140, as the mean over the anchors, in the frozen part of each still's log. Prints the lags whose mean is more
# than 4 standard errors from 0 (a standard error is 1 / sqrt(anchors * draws)), and the spectrum's largest lines.
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import effect as E  # noqa: E402

for name, log in (('bay, bounce alone', 'r2-main-bay-bounce'), ('exit-cabin, bounce alone', 'r2-main-exit-bounce'),
                  ('still cabin, bounce alone', 'r2-main-cabin-bounce')):
    draws, x, g, _ = E.replay.load(E.LOGS / log / 'region-none.txt')
    keep = draws >= 400
    x = x[keep][:, g[keep].min(0) > 0]
    T, A = x.shape
    med = np.median(x, 0)
    mad = 1.4826 * np.median(np.abs(x - med), 0)
    u = np.clip((x - med) / mad, -4, 4)
    c = u - u.mean(0)
    var = (c * c).mean(0)
    se = 1 / np.sqrt(A * T)
    out = []
    for lag in range(1, 141):
        r = ((c[lag:] * c[:-lag]).mean(0) / var).mean()
        if abs(r) > 4 * se:
            out.append(f'{lag}: {r:+.4f}')
    print(f'{name}: {A} anchors, {T} draws, standard error {se:.4f}; lags beyond 4 standard errors: {", ".join(out) or "none"}')
    power = (np.abs(np.fft.rfft(c, axis=0)) ** 2 / (T * var)).mean(1)
    order = np.argsort(power[1:])[::-1][:6] + 1
    print('  largest spectrum lines (period in draws: power, white noise gives 1): ' + ', '.join(f'{T / k:.1f}: {power[k]:.2f}' for k in order))
    # The first and second half of the frozen part: does the anchors' mean light step between captures of 33 draws?
    cap = draws[keep] // 33
    means = np.array([u[cap == k].mean(0) for k in np.unique(cap) if (cap == k).sum() == 33])
    z = means * np.sqrt(33)
    print(f'  capture means (33 draws) in standard errors: spread over the captures {z.std(0).mean():.3f} (white noise: about {np.sqrt(np.mean(u.var(0))):.3f}); '
          f'correlation of one capture\'s mean with the next: {np.mean([(np.corrcoef(z[:-1, a], z[1:, a])[0, 1]) for a in range(A)]):+.3f}')
