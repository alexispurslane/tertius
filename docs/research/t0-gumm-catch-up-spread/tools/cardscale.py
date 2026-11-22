# Tool code of t0-gumm. cardscale.py <card folder> [chart.png]: the card's scale and time logs of cardrun.sh (one folder
# per <event film>-<code>: size-*.txt "<frame> <width> <height> <scale>" per draw, time-*.txt "<frame> <GPU span> ..." in
# ms; a frame collected twice counts once). The target is 16.67 ms. Per code and event window it prints:
#   - the scale changes: the draws whose scale differs from the draw before;
#   - the largest fall: the scale of the draw before the event minus the lowest scale in the window, and the draws from
#     the event until the scale is back to at least the scale before the event (">60" if it does not come back);
#   - the scale at the window's last draw, and the scale lost: the sum over the window of (the scale before - the scale), in
#     draws, so 1 is one draw at a scale lower by 1, or ten draws lower by 0.1;
#   - the GPU span: its peak, and the draws above the largest span of the same code's idle span. The idle span itself runs
#     at about 17.2 to 17.5 ms, above the target, without a change of scale: the controller's dead band holds it.
# The windows: the start (switch film, draws 0 to 59; the scale before is 1), off (switch, 193 to 252), on (switch, 393 to
# 452), the cut (cut film, CUT to CUT + 59, CUT 134 by default; job shapes1 ran the old cut film,
# whose cut is at draw 133: CUT=133), and the idle span (switch, 133 to 192: lamps on, settled, nothing changes), which is
# the bar: an event's block moves the scale when its changes or its fall exceed the idle span's.
# The chart: the scale per draw in the four event windows, one line per code.
import os
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

root = Path(sys.argv[1])
WINDOWS = {'idle': ('switch', 133), 'start': ('switch', 0), 'off': ('switch', 193), 'on': ('switch', 393),
           'cut': ('cut', int(os.environ.get('CUT', '134')))}
N = 60


def load(folder):
    scale = {int(l.split()[0]): float(l.split()[3]) for l in open(next(folder.glob('size-*.txt')))}
    span = {}
    for l in open(next(folder.glob('time-*.txt'))):
        span.setdefault(int(l.split()[0]), float(l.split()[1]))
    return scale, span


codes = sorted({p.name.split('-', 1)[1] for p in root.iterdir() if p.is_dir()}, key=lambda c: (c == 'cap', c != '1,8,0', c))
data = {(e, c): load(root / f'{e}-{c}') for e in ('switch', 'cut') for c in codes}
print('code | window | scale changes | largest fall, draws back | scale at +59, scale lost | GPU span peak ms, draws above the idle span\'s largest')
for c in codes:
    idle_scale, idle_span = data[('switch', c)]
    idle_top = max(idle_span[d] for d in range(133, 193) if d in idle_span)
    for w, (film, at) in WINDOWS.items():
        scale, span = data[(film, c)]
        before = scale.get(at - 1, 1.0)
        s = [scale[d] for d in range(at, at + N)]
        changes = sum(scale[d] != scale.get(d - 1, 1.0) for d in range(at, at + N))
        fall = before - min(s)
        low = int(np.argmin(s))
        back = next((d - at for d in range(at + low, at + N) if scale[d] >= before - 1e-6), None)
        sp = [span[d] for d in range(at, at + N) if d in span]
        print(f'{c} | {w} | {changes} | {fall:.3f} (from {before:.3f} to {min(s):.3f} at +{low}), '
              f'{"none" if fall <= 1e-6 else (back if back is not None else ">60")} | {s[-1]:.3f}, '
              f'{sum(before - x for x in s):.2f} | {max(sp):.2f}, {sum(x > idle_top for x in sp)}')

if len(sys.argv) > 2:
    W, H, L = 1600, 420, 50
    im = Image.new('RGB', (W, H), 'white'); d = ImageDraw.Draw(im)
    events = ['start', 'off', 'on', 'cut']
    pw = (W - L - 60) // 4
    colours = [(30, 30, 30), (220, 60, 60), (40, 110, 200), (40, 160, 80), (200, 140, 20), (140, 60, 180)]
    lo = 0.5
    for i, w in enumerate(events):
        film, at = WINDOWS[w]
        x0 = L + i * (pw + 20)
        x = lambda k: x0 + (k + 5) / (N + 5) * pw
        y = lambda v: 30 + (1 - (v - lo) / (1 - lo)) * (H - 90)
        for g in (0.5, 0.6, 0.7, 0.8, 0.9, 1.0):
            d.line([(x0, y(g)), (x0 + pw, y(g))], fill=(225, 225, 225))
            if i == 0:
                d.text((6, y(g) - 6), f'{g:.1f}', fill='black')
        d.line([(x(0), 30), (x(0), H - 60)], fill=(180, 180, 180))
        for j, c in enumerate(codes):
            scale, _ = data[(film, c)]
            d.line([(x(k), y(scale.get(at + k, 1.0))) for k in range(-5, N)], fill=colours[j % 6], width=2)
        d.text((x0, H - 52), {'start': 'the start (switch film, draw 0)', 'off': 'lamps off (draw 193)',
                              'on': 'lamps on (draw 393)', 'cut': f'camera cut (cut film, draw {at})'}[w], fill='black')
    for j, c in enumerate(codes):
        d.text((L + 10 + 230 * j, 8), {'1,8,0': 'today (1, 8, 0)', 'cap': 'the cap'}.get(c, f'({c.replace(",", ", ")})'),
               fill=colours[j % 6])
    d.text((L, H - 30), 'render scale per draw on the card (RTX 2000 Ada, 1920 x 1080, dynamic scale at 16.67 ms), '
           '5 draws before each event to 59 after', fill='black')
    im.save(sys.argv[2]); print(sys.argv[2])
