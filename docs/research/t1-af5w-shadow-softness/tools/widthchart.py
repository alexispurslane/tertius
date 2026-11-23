"""Tool code of t1-af5w. widthchart.py <take folder> <out.png> [view...]: one panel per view, the rendered width (y) over
the true width (x) of every edge pixel of the probe take, both in m on log scales, as a pixel-count density: the sun's
edges in orange, the lamps' in blue. The grey diagonal is rendered = true. Pixels whose gap is under 5 cm (a receiver
that shadows itself, or a contact) are left out, and each panel counts them."""
import sys, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont
take, out = sys.argv[1:3]
views = sys.argv[3:] or sorted(v for v in os.listdir(take) if os.path.exists(f'{take}/{v}/edge.txt'))
X0, X1, Y0, Y1, N = -3.5, 1.5, -3.5, 1.5, 60  # log10 m, the same decades on both axes
S, PAD, COLS = 300, 64, 4
rows = (len(views) + COLS - 1) // COLS
try:
    font = ImageFont.load_default(size=15); big = ImageFont.load_default(size=18)
except TypeError:
    font = big = ImageFont.load_default()
W, H = COLS * (S + PAD) + PAD, rows * (S + PAD + 40) + 90
img = Image.new('RGB', (W, H), 'white'); d = ImageDraw.Draw(img)
d.text((PAD, 8), 'Rendered width (y) over true width (x) of each shadow-edge pixel, m, log scales. Orange: the sun. Blue: the lamps.'
       ' Grey: rendered = true.', fill='black', font=big)
d.text((PAD, 32), 'Above the diagonal today\'s ramp is wider than the penumbra (too soft); below, narrower (too sharp). Gaps under 5 cm left out.',
       fill='black', font=font)
for i, v in enumerate(views):
    w, h, _ = map(int, open(f'{take}/{v}/edge.txt').read().split())
    a = np.fromfile(f'{take}/{v}/edge-a.f32', np.float32).reshape(-1, 4)
    b = np.fromfile(f'{take}/{v}/edge-b.f32', np.float32).reshape(-1, 4)
    m = a[:, 3] != 0
    a, b = a[m], b[m]
    sun = a[:, 3] < 0
    angle = np.where(sun, b[:, 2], b[:, 2] / np.maximum(b[:, 1], 1e-6))
    gap, rendered = a[:, 0], a[:, 2]
    keep = gap >= 0.05
    true = gap * angle
    ox, oy = PAD + (i % COLS) * (S + PAD), 100 + (i // COLS) * (S + PAD + 40)
    panel = np.full((S, S, 3), 255.0)
    for group, color in ((sun & keep, (230, 120, 20)), (~sun & keep, (40, 90, 210))):
        if not group.any():
            continue
        hist, _, _ = np.histogram2d(np.log10(np.maximum(true[group], 1e-9)), np.log10(rendered[group]), bins=N,
                                    range=((X0, X1), (Y0, Y1)))
        k = np.log1p(hist) / np.log1p(hist.max())
        k = k.T[::-1]  # rows from the top: the largest rendered width first
        k = np.asarray(Image.fromarray((k * 255).astype(np.uint8)).resize((S, S), Image.NEAREST)) / 255.0
        for c in range(3):
            panel[..., c] = panel[..., c] * (1 - k) + color[c] * k
    img.paste(Image.fromarray(panel.astype(np.uint8)), (ox, oy))
    px = lambda lx: ox + (lx - X0) / (X1 - X0) * S
    py = lambda ly: oy + S - (ly - Y0) / (Y1 - Y0) * S
    d.line([px(Y0), py(Y0), px(Y1), py(Y1)], fill=(150, 150, 150), width=1)
    d.rectangle([ox, oy, ox + S, oy + S], outline=(120, 120, 120))
    for k in (-3, -2, -1, 0, 1):
        d.line([px(k), oy + S, px(k), oy + S + 4], fill='black'); d.line([ox - 4, py(k), ox, py(k)], fill='black')
        d.text((px(k) - 12, oy + S + 4), f'1e{k}', fill='black', font=font)
        d.text((ox - 40, py(k) - 8), f'1e{k}', fill='black', font=font)
    n_sun, n_lamp = int((sun & keep).sum()), int((~sun & keep).sum())
    d.text((ox, oy - 38), v, fill='black', font=big)
    d.text((ox, oy - 18), f'sun {n_sun}, lamps {n_lamp}, gap < 5 cm {int((~keep).sum())}', fill=(90, 90, 90), font=font)
img.save(out)
