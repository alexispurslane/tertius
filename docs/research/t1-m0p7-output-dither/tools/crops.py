"""Tool code of t1-m0p7's gate. crops.py <out folder> <name>=<still.png>:<x>:<y>:<w>:<h>...

For each still it cuts the 1:1 crop with no scaling and no change of the codes (<name>.png), checks that the crop's
bytes equal the still's at that place, and writes, for finding steps only, the crop stretched 12 times around each
channel's own blur, as the guide census's bands.py does (<name>-stretched.png: 128 + 12 (code - blur), clipped to
0..255, with blur the channel's codes under a Gaussian of 36 px, mirrored at the crop's edges). It prints the range
of 8-bit codes per channel, and the widths in pixels of the runs of one code of the G channel along the crop's steepest
direction: the direction of the mean gradient of the G channel blurred by a Gaussian of 8 px. Lines run along that
direction every 4 px across it, by nearest sampling at steps of 1 px. A run that touches a line's end is left out.
"""
import json
import sys
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter

out = sys.argv[1]
report = {}
for spec in sys.argv[2:]:
    name, rest = spec.split('=', 1)
    path, x, y, w, h = rest.rsplit(':', 4)
    x, y, w, h = int(x), int(y), int(w), int(h)
    still = Image.open(path)
    assert still.mode in ('RGB', 'RGBA'), still.mode
    full = np.asarray(still)
    crop = full[y:y + h, x:x + w].copy()
    Image.fromarray(crop).save(f'{out}/{name}.png')
    back = np.asarray(Image.open(f'{out}/{name}.png'))
    same = back.shape == crop.shape and np.array_equal(back, full[y:y + h, x:x + w])
    c = crop[..., :3].astype(float)
    blur = np.stack([gaussian_filter(c[..., i], 36, mode='mirror') for i in range(3)], -1)
    stretched = np.clip(128 + 12 * (c - blur), 0, 255).astype(np.uint8)
    Image.fromarray(stretched).save(f'{out}/{name}-stretched.png')
    g = crop[..., 1].astype(int)
    smooth = gaussian_filter(g.astype(float), 8)
    gy, gx = np.gradient(smooth)
    d = np.array([gx[8:-8, 8:-8].mean(), gy[8:-8, 8:-8].mean()])
    d /= np.linalg.norm(d)
    n = np.array([-d[1], d[0]])
    runs = []
    center = np.array([w / 2, h / 2])
    reach = int(np.hypot(w, h))
    for off in range(-reach, reach, 4):
        ts = np.arange(-reach, reach)
        pts = center + off * n + np.outer(ts, d)
        inside = (pts[:, 0] >= 0) & (pts[:, 0] < w) & (pts[:, 1] >= 0) & (pts[:, 1] < h)
        pts = pts[inside]
        if len(pts) < 20:
            continue
        codes = g[pts[:, 1].astype(int), pts[:, 0].astype(int)]
        edges = np.flatnonzero(np.diff(codes)) + 1
        bounds = np.concatenate(([0], edges, [len(codes)]))
        lengths = np.diff(bounds)[1:-1]
        runs.extend(lengths.tolist())
    runs = np.array(runs)
    ranges = {ch: [int(crop[..., i].min()), int(crop[..., i].max())] for i, ch in enumerate('RGB')}
    slope = float(np.hypot(gx, gy)[8:-8, 8:-8].mean())
    report[name] = {
        'still': path, 'x': x, 'y': y, 'w': w, 'h': h, 'bytesEqual': bool(same),
        'codeRange': ranges, 'steepestDirection': [round(float(d[0]), 3), round(float(d[1]), 3)],
        'meanSlopeCodesPerPx': round(slope, 4),
        'runsG': {'count': int(len(runs)), 'least': int(runs.min()), 'median': float(np.median(runs)),
                  'greatest': int(runs.max()), 'shareOfOnePx': round(float((runs == 1).mean()), 3)},
    }
    print(name, json.dumps(report[name]))
json.dump(report, open(f'{out}/numbers.json', 'w'), indent=1)
