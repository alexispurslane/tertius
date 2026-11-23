# Tool code of t0-rs55. band.py <turn|walk> <today run folder> <wide run folder> [chart.png]: the band of newly seen pixels
# of motionrun.sh's runs. Per draw of the window, a side's band is its pixels whose slow diffuse history is 1 to 3 draws long
# (history-<draw>-<w>x<h>.u8, at that draw's render size; a length of 0 is the sky), the pixels whose short history the
# fix replaces. The band is mapped onto the 1920 x 1080 PNG of the draw by nearest pixel. Per draw it measures, per side:
#   - the band's share of the picture (band pixels over all pixels of the render size);
#   - the band's grain: display-linear luminance minus its 5 x 5 box mean, rms over the band's pixels of the PNG, over the
#     band's mean luminance, in %. The band lies along the edges of newly seen surfaces, so the box mean also holds the edge;
#     both sides share that part, and the comparison is per draw.
# It prints the share and the grain per draw, and their means over the motion (turn: g000 to g059, walk: g 0 to 119) and
# over the 60 draws after it (h 0 to 59), with the wide fix's change per draw (its mean and its range). With a chart path it
# draws the grain and the share per draw.
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

film, today, wide = sys.argv[1:4]
chart = sys.argv[4] if len(sys.argv) > 4 else None
if film == 'turn':
    frames = [(83 + k, f'f-{50 + k:06d}') for k in range(10)] + [(93 + k, f'g{k:03d}-000000') for k in range(60)] \
        + [(153 + k, f'h-{k:06d}') for k in range(60)]
    motion, after = range(93, 153), range(153, 213)
else:
    frames = [(203 + k, f'f-{170 + k:06d}') for k in range(10)] + [(213 + k, f'g-{k:06d}') for k in range(120)] \
        + [(333 + k, f'h-{k:06d}') for k in range(60)]
    motion, after = range(213, 333), range(333, 393)


def lum(png):
    a = np.asarray(Image.open(png).convert('RGB')).astype(float) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]


def box5(y):
    p = np.pad(y, 2, mode='edge')
    c = p.cumsum(0).cumsum(1)
    c = np.pad(c, ((1, 0), (1, 0)))
    return (c[5:, 5:] - c[:-5, 5:] - c[5:, :-5] + c[:-5, :-5]) / 25


def measure(folder, draw, name):
    dumps = list(Path(folder).glob(f'history-{draw}-*.u8'))
    if not dumps:
        return None
    w, h = map(int, dumps[0].stem.split('-')[2].split('x'))
    length = np.fromfile(dumps[0], np.uint8).reshape(h, w)
    band = (length >= 1) & (length < 4)
    y = lum(Path(folder) / f'{name}.png')
    H, W = y.shape
    mask = band[(np.arange(H) * h // H)[:, None], (np.arange(W) * w // W)[None, :]]
    share = band.mean()
    if mask.sum() < 50:
        return share, float('nan')
    d = (y - box5(y))[mask]
    return share, 100 * np.sqrt(np.mean(d * d)) / max(y[mask].mean(), 1e-6)


rows = []
for draw, name in frames:
    a, b = measure(today, draw, name), measure(wide, draw, name)
    if a is None or b is None:
        continue
    rows.append((draw, a[0], b[0], a[1], b[1]))
    print(f'draw {draw} {name}: share today {100 * a[0]:.2f} %, wide {100 * b[0]:.2f} %; grain today {a[1]:.2f} %, wide {b[1]:.2f} %')
r = np.array(rows)
for label, window in (('the motion', motion), ('the 60 draws after it', after)):
    sel = np.isin(r[:, 0], list(window)) & ~np.isnan(r[:, 3]) & ~np.isnan(r[:, 4])
    if not sel.any():
        continue
    d = r[sel, 4] - r[sel, 3]
    print(f'{film}, {label} ({sel.sum()} draws with a band): share today {100 * r[sel, 1].mean():.2f} %, wide '
          f'{100 * r[sel, 2].mean():.2f} %; grain today {r[sel, 3].mean():.2f} %, wide {r[sel, 4].mean():.2f} %, '
          f'wide - today per draw: mean {d.mean():+.2f}, range {d.min():+.2f} to {d.max():+.2f}, '
          f'{(d < 0).sum()} of {len(d)} draws lower')
if chart:
    PW, PH, M = 700, 220, 60
    im = Image.new('RGB', (2 * (PW + M) + M, PH + 80), 'white'); g = ImageDraw.Draw(im)
    for j, (cols, title) in enumerate((((3, 4), 'grain of the band, %'), ((1, 2), 'share of the picture, %'))):
        x0, y0 = M + j * (PW + M), 30
        vals = r[:, cols[0]], r[:, cols[1]]
        scale = 100 if j == 1 else 1
        top = np.nanmax(np.concatenate(vals)) * scale * 1.1
        g.rectangle([x0, y0, x0 + PW, y0 + PH], outline=(200, 200, 200))
        g.text((x0, y0 - 18), f'{film}: {title} per draw ({int(r[0, 0])} to {int(r[-1, 0])})', fill='black')
        g.text((x0 - 50, y0 - 6), f'{top:.2f}', fill='black')
        for v, colour in zip(vals, ((30, 30, 30), (40, 110, 200))):
            pts = [(x0 + (dr - r[0, 0]) / (r[-1, 0] - r[0, 0]) * PW, y0 + PH * (1 - val * scale / top))
                   for dr, val in zip(r[:, 0], v) if not np.isnan(val)]
            g.line(pts, fill=colour, width=2)
        for edge in (motion.start, after.start):
            xx = x0 + (edge - r[0, 0]) / (r[-1, 0] - r[0, 0]) * PW
            g.line([(xx, y0), (xx, y0 + PH)], fill=(220, 120, 120))
    g.text((M, PH + 50), "black today's fix, blue the wide fix; red lines: the motion starts, the motion ends", fill='black')
    im.save(chart)
