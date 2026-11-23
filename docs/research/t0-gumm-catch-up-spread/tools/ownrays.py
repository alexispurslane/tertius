# Tool code of t0-gumm. ownrays.py <chart.png> <label|ownrays log>...: the own-ray logs of scratch-ownray-log.patch
# ("<frame> <draw index> <w> <h> <reflection own rays> <in even columns> <in even rows> <diffuse own rays> <in even
# columns> <in even rows>" per draw) of runs of the switch film (the lamps off at draw 193, on at 393). The first label is
# the reference, today's code.
# It prints per run:
#   - whether every line equals the reference's (the check of the shape (1, 8, 0));
#   - the settled draws 150 to 192: the mean own rays per kind, the draws whose count differs from the reference, and the
#     largest difference;
#   - per event (the start, draws 0 to 59; off, 193 to 252; on, 393 to 452): the peak own rays per kind, the draws above
#     the settled mean + 10 % of the reference's peak, and the sum over the window above the settled mean (the catch-up
#     rays of the event, about);
#   - the parity check: over each event's window, the catch-up rays (the run's own rays minus the settled mean of its kind)
#     split by (column parity, draw-index parity) as four counts, and their largest share off a quarter; then the difference
#     between even and odd columns within each draw parity, the coupling of the column parity to the draw parity that
#     t2-8foa's fault would show.
# The chart: diffuse own rays per draw, in thousands, for draws 0 to 60, 183 to 263 and 383 to 463, one line per run.
import sys
import numpy as np
from PIL import Image, ImageDraw

out, specs = sys.argv[1], [s.split('|') for s in sys.argv[2:]]
runs = [(label, np.loadtxt(path, dtype=np.int64)) for label, path in specs]
ref = runs[0][1]
EVENTS = {'start': (0, 60), 'off': (193, 253), 'on': (393, 453)}
KINDS = {'reflection': (4, 5), 'diffuse': (7, 8)}
for label, a in runs:
    same = a.shape == ref.shape and bool((a == ref).all())
    print(f'== {label}: {len(a)} draws, every line equal to {runs[0][0]}: {same}')
    settled = (a[:, 0] >= 150) & (a[:, 0] < 193)
    for kind, (c, even) in KINDS.items():
        rs = ref[settled][:, c]; xs = a[settled][:, c]
        mean = xs.mean()
        print(f'  {kind}: settled {mean:.1f} own rays per draw (reference {rs.mean():.1f}), {int((xs != rs).sum())} of '
              f'{len(xs)} draws differ, largest difference {int(np.abs(xs - rs).max())}')
        peak_ref = {e: ref[(ref[:, 0] >= f) & (ref[:, 0] < t)][:, c].max() for e, (f, t) in EVENTS.items()}
        for e, (f, t) in EVENTS.items():
            w = a[(a[:, 0] >= f) & (a[:, 0] < t)]
            v = w[:, c]
            above = int((v > mean + 0.1 * (peak_ref[e] - mean)).sum())
            extra = np.clip(v - mean, 0, None)
            # the parity split: column parity from the even-column count, draw parity from the draw index
            cells = np.zeros((2, 2)); draws = np.zeros(2)
            for row, x in zip(w, extra):
                if x <= 0:
                    continue
                draws[row[1] & 1] += 1
                cells[0, row[1] & 1] += max(row[even] - mean / 2, 0)
                cells[1, row[1] & 1] += max(row[c] - row[even] - mean / 2, 0)
            q = cells / max(cells.sum(), 1)
            split = [100 * abs(cells[0, k] - cells[1, k]) / max(cells[:, k].sum(), 1) for k in (0, 1)]
            print(f'    {e}: peak {int(v.max())}, {above} draws above settled + 10 % of the reference peak, catch-up rays '
                  f'{int(extra.sum())}; by (even/odd column, even/odd draw) {int(cells[0, 0])} {int(cells[0, 1])} '
                  f'{int(cells[1, 0])} {int(cells[1, 1])}, largest share off 1/4: {100 * np.abs(q - 0.25).max() / 0.25:.2f} %; '
                  f'even against odd columns within the even draws {split[0]:.2f} %, within the odd draws {split[1]:.2f} %')

W, H, L = 1500, 420, 60
im = Image.new('RGB', (W, H), 'white'); d = ImageDraw.Draw(im)
spans = [(0, 60), (183, 263), (383, 463)]
pw = (W - L - 40) // 3
top = max(a[:, 7].max() for _, a in runs) / 1000
colours = [(30, 30, 30), (220, 60, 60), (40, 110, 200), (40, 160, 80), (200, 140, 20), (140, 60, 180)]
for i, (f, t) in enumerate(spans):
    x0 = L + i * (pw + 20)
    x = lambda k: x0 + (k - f) / (t - f) * pw
    y = lambda v: 30 + (1 - v / top) * (H - 80)
    for g in range(0, int(top) + 1, 100):
        d.line([(x0, y(g)), (x0 + pw, y(g))], fill=(225, 225, 225))
        if i == 0:
            d.text((8, y(g) - 6), f'{g}k', fill='black')
    for j, (label, a) in enumerate(runs):
        sel = a[(a[:, 0] >= f) & (a[:, 0] < t)]
        d.line([(x(r[0]), y(r[7] / 1000)) for r in sel], fill=colours[j % 6], width=2)
    d.text((x0, H - 40), ['the start: draws 0 to 60', 'lamps off at draw 193', 'lamps on at draw 393'][i], fill='black')
for j, (label, _) in enumerate(runs):
    d.text((L + 10 + 260 * j, 8), label, fill=colours[j % 6])
d.text((L, H - 22), 'diffuse own rays per draw, thousands (the switch film at the constant scale 2/3, 1280 x 720 = 921,600 pixels)', fill='black')
im.save(out)
print(out)
