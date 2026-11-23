"""Tool code of t58-3n6v. coldchart.py <job out/card folder> <out.png> [label]: the requested render scale per draw of the
cold run (orange) and the warm run (blue), draws 0 to 602, with the start and the three cuts back to the person camera
marked by red lines and the one chase-camera draw before each cut by a grey line. Below: the GPU span per draw, cut at
30 ms, with draw 0 of each run labeled. The label names the job and its code in the footer. Pillow only."""
import sys
from PIL import Image, ImageDraw, ImageFont
root, out = sys.argv[1], sys.argv[2]
JOB = sys.argv[3] if len(sys.argv) > 3 else 'cold1, main 2e8fdaed2'
def load(run):
    size = {int(l.split()[0]): float(l.split()[3]) for l in open(f'{root}/{run}/size-none.txt')}
    span = {}
    for l in open(f'{root}/{run}/time-none.txt'):
        f = l.split(); span.setdefault(int(f[0]), float(f[1]))
    return size, span
runs = {'cold': load('cold'), 'warm': load('warm')}
COL = {'cold': (214, 120, 20), 'warm': (40, 100, 200)}
W, PAD, L = 1400, 70, 603
try:
    font = ImageFont.load_default(size=18); small = ImageFont.load_default(size=15)
except TypeError:
    font = small = ImageFont.load_default()
H1, H2 = 460, 300
img = Image.new('RGB', (W, H1 + H2 + 40), 'white'); d = ImageDraw.Draw(img)
x = lambda k: PAD + k * (W - 2 * PAD) / (L - 1)
def panel(top, h, lo, hi, ticks, fmt, key, title):
    y = lambda v: top + 40 + (hi - min(max(v, lo), hi)) * (h - 80) / (hi - lo)
    d.text((PAD, top + 8), title, fill='black', font=font)
    for t in ticks:
        d.line([PAD, y(t), W - PAD, y(t)], fill=(228, 228, 228)); d.text((8, y(t) - 9), fmt(t), fill='black', font=small)
    for k, (c, lab) in {150: ((150,) * 3, ''), 301: ((150,) * 3, ''), 452: ((150,) * 3, ''),
                        0: ((220, 30, 30), 'start'), 151: ((220, 30, 30), 'cut 1'), 302: ((220, 30, 30), 'cut 2'),
                        453: ((220, 30, 30), 'cut 3')}.items():
        d.line([x(k), top + 40, x(k), top + h - 40], fill=c, width=1)
        if lab: d.text((x(k) + 4, top + 40), lab, fill=c, font=small)
    for run in ('warm', 'cold'):
        s = runs[run][key]
        pts = [(x(k), y(s[k])) for k in sorted(s) if k < L]
        d.line(pts, fill=COL[run], width=2)
    d.rectangle([PAD, top + 40, W - PAD, top + h - 40], outline=(160, 160, 160))
    for k in range(0, L, 50): d.text((x(k) - 8, top + h - 36), str(k), fill='black', font=small)
    return y
panel(0, H1, 0.5, 1.01, [0.5, 0.6, 0.7, 0.8, 0.9, 1.0], lambda t: f'{t:.1f}', 0,
      'Requested render scale per draw: cold run (first GPU run of the job) orange, warm run blue. Grey: the chase-camera draw.')
y = panel(H1, H2, 0, 30, [0, 10, 15, 20, 30], lambda t: f'{t:.0f} ms', 1,
          'GPU span per draw, cut at 30 ms (the budget is 15 ms, 90 % of 16.67)')
for run, dy in (('cold', 0), ('warm', 18)):
    d.text((x(0) + 60, H1 + 44 + dy), f'draw 0 {run}: {runs[run][1][0]:.0f} ms', fill=COL[run], font=small)
d.text((PAD, H1 + H2 + 10), 'x: draw since the run\'s start (t58-3n6v, card job ' + JOB + ', RTX 2000 Ada)',
       fill='black', font=small)
img.save(out)
