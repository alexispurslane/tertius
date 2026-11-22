# Tool code of t0-gbgj. filmmotion.py <out.png> <title> <edges> <label=run>...: measures of motion in the frames that
# film.py kept (s/<run>/frames, in the survey's order), for a film whose camera stands still, and one chart of them.
# Per frame, in display-linear luminance (the 8-bit sRGB decoded, 0 to 1):
#   - light: the mean of the whole image and of each cell of the 3 x 3 grid (r00 at the top left), as in the survey's
#     region means, here from the 8-bit frames;
#   - specks: the survey's speck count (renderer-survey.py, specks: ter-9371's rule, groups per 10,000 visible pixels)
#     against a reference without specks, the frame's own 5 x 5 median;
#   - grain: the RMS of the relative fine detail, (y - g) / max(g, 0.01) with g a Gaussian blur of 2 pixels, over the
#     visible pixels where g is smooth (its relative gradient under 2 % per pixel, so that edges do not count);
#   - edges: the x of each fixed vertical edge, in output pixels: in each row of its box, the place of the largest
#     horizontal gradient of the frame blurred by 1 pixel, refined by a parabola through its neighbors, then a straight
#     line through the rows, read at the box's middle row. <edges> is "name:x0,y0,x1,y1;name:...". A still camera should
#     give the same x in every frame.
# Printed per run: each edge's lowest, highest and range of x over the frames, the mean and largest specks and grain,
# and the whole image's light at the first, middle and last frame. The chart has the whole image's light, the nine
# regions' light, the specks, the grain and the edges, one line per run. m.sh runs it in $W/w.
import glob, importlib.util, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, median_filter
spec = importlib.util.spec_from_file_location('survey', Path(__file__).resolve().parents[4] / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec); spec.loader.exec_module(survey)
COLORS = [(40, 40, 40), (200, 60, 60), (40, 110, 200), (230, 150, 20)]


def edge_x(y, box):
    """The x of a near-vertical edge in box (x0, y0, x1, y1) of the luminance image y, at the box's middle row."""
    x0, y0, x1, y1 = box
    d = np.abs(np.diff(gaussian_filter(y, 1)[y0:y1, x0:x1], axis=1))
    rows, xs = [], []
    for r in range(d.shape[0]):
        k = int(np.argmax(d[r]))
        if 0 < k < d.shape[1] - 1:
            a, b, c = d[r, k - 1], d[r, k], d[r, k + 1]
            shift = 0.5 * (a - c) / (a - 2 * b + c) if (a - 2 * b + c) != 0 else 0
            rows.append(y0 + r); xs.append(x0 + k + 0.5 + shift)
    fit = np.polyfit(rows, xs, 1)
    return float(np.polyval(fit, (y0 + y1) / 2))


def grain(y):
    g = gaussian_filter(y, 2)
    gy, gx = np.gradient(g)
    ok = (np.hypot(gx, gy) / np.maximum(g, 0.01) < 0.02) & (g > 0.002)
    return float(np.sqrt(np.mean(((y - g) / np.maximum(g, 0.01))[ok] ** 2)))


def regions(y):
    h, w = y.shape
    return [float(y.mean())] + [float(y[h * r // 3:h * (r + 1) // 3, w * c // 3:w * (c + 1) // 3].mean())
                                for r in range(3) for c in range(3)]


def measure(run, edges):
    rows = []
    for f in sorted(glob.glob(f's/{run}/frames/m*-*.png')):
        y = survey.luminance(Path(f))
        rows.append({'light': regions(y), 'specks': survey.specks(y, median_filter(y, 5)), 'grain': grain(y),
                     'edges': [edge_x(y, box) for _, box in edges]})
    return rows


def panel(d, box, title, series, labels):
    x0, y0, x1, y1 = box
    d.rectangle(box, outline=(160, 160, 160)); d.text((x0 + 4, y0 + 2), title, fill=(0, 0, 0))
    lo = min(min(s) for s in series); hi = max(max(s) for s in series)
    if hi - lo < 1e-9: hi = lo + 1e-9
    d.text((x1 - 150, y0 + 2), f'{lo:.4g} to {hi:.4g}', fill=(90, 90, 90))
    n = len(series[0])
    for i, s in enumerate(series):
        pts = [(x0 + 4 + (x1 - x0 - 8) * k / max(n - 1, 1), y1 - 6 - (y1 - y0 - 24) * (v - lo) / (hi - lo)) for k, v in enumerate(s)]
        d.line(pts, fill=COLORS[i % len(COLORS)], width=2)


def main():
    out, title, spec_edges = sys.argv[1], sys.argv[2], sys.argv[3]
    edges = [(e.split(':')[0], [int(c) for c in e.split(':')[1].split(',')]) for e in spec_edges.split(';')]
    runs = [a.split('=', 1) for a in sys.argv[4:]]
    data = {label: measure(run, edges) for label, run in runs}
    for label, rows in data.items():
        for i, (name, _) in enumerate(edges):
            xs = [r['edges'][i] for r in rows]
            print(f"{label} edge {name}: x {min(xs):.3f} to {max(xs):.3f}, range {max(xs) - min(xs):.3f} px")
        sp = [r['specks'] for r in rows]; gr = [r['grain'] for r in rows]; wh = [r['light'][0] for r in rows]
        print(f"{label} specks mean {np.mean(sp):.2f} largest {max(sp):.2f}; grain mean {np.mean(gr):.4f} largest "
              f"{max(gr):.4f}; whole light first / middle / last {wh[0]:.4f} / {wh[len(wh) // 2]:.4f} / {wh[-1]:.4f}; "
              f"frames {len(rows)}")
    W, PH = 1500, 150
    im = Image.new('RGB', (W, 60 + PH * 5 + 3 * 110 + 10), 'white'); d = ImageDraw.Draw(im)
    d.text((10, 8), f'{title}: one line per run, film frames 0 to {len(next(iter(data.values()))) - 1} left to right', fill=(0, 0, 0))
    for i, label in enumerate(data):
        d.rectangle((10 + 200 * i, 30, 30 + 200 * i, 42), fill=COLORS[i]); d.text((36 + 200 * i, 30), label, fill=(0, 0, 0))
    y = 52
    panel(d, (10, y, W - 10, y + PH - 8), 'whole image light (mean display-linear luminance)', [[r['light'][0] for r in rows] for rows in data.values()], list(data)); y += PH
    for rr in range(3):
        for cc in range(3):
            x = 10 + (W - 20) * cc // 3
            panel(d, (x, y, x + (W - 20) // 3 - 6, y + 104), f'r{rr}{cc} light', [[r['light'][1 + 3 * rr + cc] for r in rows] for rows in data.values()], list(data))
        y += 110
    panel(d, (10, y, W - 10, y + PH - 8), 'specks per 10,000 visible pixels', [[r['specks'] for r in rows] for rows in data.values()], list(data)); y += PH
    panel(d, (10, y, W - 10, y + PH - 8), 'grain (RMS relative fine detail on smooth surfaces)', [[r['grain'] for r in rows] for rows in data.values()], list(data)); y += PH
    for i, (name, _) in enumerate(edges):
        panel(d, (10, y, W - 10, y + PH - 8), f'edge {name}: x in output pixels', [[r['edges'][i] for r in rows] for rows in data.values()], list(data)); y += PH
    im.save(out); print(out)


if __name__ == '__main__':
    main()
