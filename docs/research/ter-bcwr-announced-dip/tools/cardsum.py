# Tool code of ter-bcwr. cardsum.py <card job folder>...: the render scale of card.sh's runs on the card
# (out/card/<event>-<code>-r<round>/: size-*.txt "<draw> <w> <h> <requested scale>", time-*.txt "<frame> <GPU span ms> ...";
# a draw logged twice counts once). The event's draw: 0 for the start, 134 for the cut. The window: the 60 draws from the
# event. The rest: the median requested scale of the run's last 40 draws. Per event, code and round it prints:
#   - the requested scale of draws 0 to 20 after the event, and the GPU span of draws 0 to 8;
#   - back: the draws from the event until the scale is back within 0.02 of the rest and stays there to the window's end
#     (0 when it never left);
#   - under: the draws of the window more than 0.02 under the rest;
#   - the lowest scale of the window;
#   - the size changes, the draws whose render size differs from the draw before: in the window, and in the whole run.
# Then per event and code, the mean of back and of the size changes over the rounds.
import statistics
import sys
from collections import defaultdict
from pathlib import Path

AT = {'start': 0, 'cut': 134}
WINDOW = 60
for event, at in AT.items():
    print(f'== {event} (draw {at})')
    means = defaultdict(list)
    for root in sys.argv[1:]:
        for folder in sorted((Path(root) / 'out' / 'card').glob(f'{event}-*-r*')):
            scale, size, span = {}, {}, {}
            for line in open(next(folder.glob('size-*.txt'))):
                d, w, h, s = line.split()[:4]
                scale.setdefault(int(d), float(s))
                size.setdefault(int(d), (int(w), int(h)))
            for line in open(next(folder.glob('time-*.txt'))):
                span.setdefault(int(line.split()[0]), float(line.split()[1]))
            draws = sorted(scale)
            rest = statistics.median(scale[d] for d in draws[-40:])
            window = [d for d in range(at, at + WINDOW) if d in scale]
            low = [d - at for d in window if scale[d] < rest - 0.02]
            back = max(low) + 1 if low else 0
            changes = [d for d in draws[1:] if d - 1 in size and size[d] != size[d - 1]]
            in_window = [d for d in changes if at < d < at + WINDOW]
            code = folder.name[len(event) + 1:]
            means[code.rsplit('-', 1)[0]].append((back, len(in_window), len(changes)))
            print(f'  {Path(root).name} {code}: scales 0-20 ' + ' '.join(f'{scale.get(at + k, float("nan")):.3f}' for k in range(21))
                  + ' | spans 0-8 ' + ' '.join(f'{span.get(at + k, float("nan")):.2f}' for k in range(9))
                  + f' | rest {rest:.3f}, back {back}, under {len(low)}, lowest {min(scale[d] for d in window):.3f}'
                  + f', size changes {len(in_window)} in the window, {len(changes)} in the run')
    for code, rows in means.items():
        print(f'  mean {code}: back {statistics.mean(r[0] for r in rows):.1f}, size changes '
              f'{statistics.mean(r[1] for r in rows):.1f} in the window, {statistics.mean(r[2] for r in rows):.1f} in the run '
              f'({len(rows)} rounds)')
