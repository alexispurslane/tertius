# Tool code of t0-rs55. cardwide.py <card job folder>: the render scale and the GPU span of cardwide.sh's runs on the card
# (out/card/<event>-<code>-r<round>/, size-*.txt "<draw> <w> <h> <scale>", time-*.txt "<frame> <GPU span ms> ..."; a draw
# logged twice counts once). The event's draw: 0 for the start, 134 for the cut. Per event, code and round it prints the
# scale and the span of draws 0 to 8 after the event, the lowest scale of the 60 draws from it, the rest (the median scale
# of the run's last 40 draws), the draws under the rest - 0.02 in those 60 draws (their count and the last one), and the
# sum of the spans of draws 1 and 2.
import statistics
import sys
from pathlib import Path

AT = {'start': 0, 'cut': 134}
root = Path(sys.argv[1]) / 'out' / 'card'
for event, at in AT.items():
    print(f'== {event} (draw {at})')
    for folder in sorted(root.glob(f'{event}-*-r*')):
        scale, span = {}, {}
        for line in open(next(folder.glob('size-*.txt'))):
            scale.setdefault(int(line.split()[0]), float(line.split()[3]))
        for line in open(next(folder.glob('time-*.txt'))):
            span.setdefault(int(line.split()[0]), float(line.split()[1]))
        rest = statistics.median(scale[d] for d in sorted(scale)[-40:])
        window = [d for d in range(at, at + 60) if d in scale]
        under = [d - at for d in window if scale[d] < rest - 0.02]
        code = folder.name[len(event) + 1:]
        print(f'  {code}: scales 0-8 ' + ' '.join(f'{scale.get(at + k, float("nan")):.3f}' for k in range(9))
              + ' | spans 0-8 ' + ' '.join(f'{span.get(at + k, float("nan")):.2f}' for k in range(9))
              + f' | lowest {min(scale[d] for d in window):.3f}, rest {rest:.3f}, under {len(under)} (last {max(under) if under else "-"})'
              + f', spans 1+2 {span.get(at + 1, 0) + span.get(at + 2, 0):.2f} ms')
