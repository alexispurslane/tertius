# count.py <mode> <variant...>: the count of the regional test (TERTIUS_X_REGION_COUNT of the scratch probe, see take.sh).
# Each variant's folder s/<variant>/count/<process id>/ holds <light>.f16, the counted draws one after the other: per
# pixel of every second render pixel of every second row, the lag |S - R| / R, the tolerances T1, T2 and T3, and a flag
# (1 the pixel's length before this draw's action is above the fast cap, 2 it is not, 0 sky). draws.txt lists each draw's run
# number. A variant's count comes from the process that counted the most draws (a film's stills count none of its film's
# draws, and a process whose command line names a film's references or its exposure run is skipped). Regions: the whole image and the survey's 3 x 3 grid, r<row><column> from the top left, of the surface pixels.
#
# Modes:
#   still: for each light, region and variant, over every counted draw: the median, 90th and 99th percentile of the lag,
#          of T1, T2 and T3, and the share of the surface pixel draws whose lag exceeds 0.10, T1, T2 and T3.
#   switch: the same shares in three windows of the cabin switch's film frames (film frame k is run draw 33 + k): 140 to
#          159 before the lamps go off, 160 to 219 after they go off, 360 to 419 after they come on, and each 60 draws
#          in four parts by the draws d since the switch.
#   film: as still, over the last 180 counted draws of the film process, the film's 180 frames.
import sys, numpy as np
from pathlib import Path

STILL_DRAWS = 33
SWITCH_WINDOWS = {'before 140-159': (140, 159), 'off 160-219': (160, 219), 'on 360-419': (360, 419)}
# The same 60 draws after each switch in four parts, by d = frame - switch frame: 0 to 5, 6 to 17, 18 to 35, 36 to 59.
for state, first in (('off', 160), ('on', 360)):
    for a, b in ((0, 5), (6, 17), (18, 35), (36, 59)):
        SWITCH_WINDOWS[f'{state} d {a}-{b}'] = (first + a, first + b)
T0 = 0.10
CHANNELS = 5  # lag, T1, T2, T3, flag
FILM_FRAMES = 180

def load(variant):
    """The counts of the process that counted the most draws: {light: (numbers, array of draws x h x w x CHANNELS)}."""
    best = None
    for folder in Path(f's/{variant}/count').iterdir():
        if any(word in (folder / 'command.txt').read_text() for word in ('references', '/work/exposure/')):
            continue
        rows = [l.split() for l in open(folder / 'draws.txt')]
        if best is None or len(rows) > len(best[1]):
            best = (folder, rows)
    folder, rows = best
    out = {}
    for light in ('diffuse', 'specular'):
        mine = [r for r in rows if r[2] == light]
        if not mine:
            continue
        w, h = int(mine[0][3]), int(mine[0][4])
        raw = np.fromfile(folder / f'{light}.f16', dtype='<f2').astype(np.float32)
        out[light] = (np.array([int(r[0]) for r in mine]), raw.reshape(len(mine), h, w, CHANNELS))
    return out

def regions(h, w):
    cells = {'whole': (slice(0, h), slice(0, w))}
    ys, xs = np.linspace(0, h, 4).astype(int), np.linspace(0, w, 4).astype(int)
    for r in range(3):
        for c in range(3):
            cells[f'r{r}{c}'] = (slice(ys[r], ys[r + 1]), slice(xs[c], xs[c + 1]))
    return cells

def stats(block):
    """The numbers of one region over some draws, from its pixel draws (n x CHANNELS)."""
    surface = block[block[:, 4] >= 1]
    if len(surface) == 0:
        return None
    lag, t1, t2, t3 = surface[:, 0], surface[:, 1], surface[:, 2], surface[:, 3]
    q = lambda a: np.percentile(a, [50, 90, 99])
    return {'pixels': len(surface), 'lag': q(lag), 't1': q(t1), 't2': q(t2), 't3': q(t3),
            'over': (float(np.mean(lag > T0)), float(np.mean(lag > t1)), float(np.mean(lag > t2)), float(np.mean(lag > t3)))}

def line(name, s, light):
    if s is None:
        return f'{name:6s} no surface'
    p = lambda a: '/'.join(f'{v:.3f}' for v in a)
    o = s['over']
    # The reflections keep no retained deviation, so their T2 does not exist (accumulate_spatial.comp, regionTestAt).
    t2 = p(s['t2']) if light == 'diffuse' else 'n/a'
    over2 = f"{100 * o[2]:5.1f}%" if light == 'diffuse' else '  n/a'
    return (f"{name:6s} lag {p(s['lag'])}  T1 {p(s['t1'])}  T2 {t2}  T3 {p(s['t3'])}  over T0 {100 * o[0]:5.1f}% "
            f"T1 {100 * o[1]:5.1f}% T2 {over2} T3 {100 * o[3]:5.1f}%")

if __name__ == '__main__':
    mode, variants = sys.argv[1], sys.argv[2:]
    print('# percentiles 50/90/99; "over" = share of surface pixel draws whose lag exceeds the tolerance')
    for v in variants:
        for light, (numbers, data) in load(v).items():
            h, w = data.shape[1:3]
            cells = regions(h, w)
            if mode == 'switch':
                frames = numbers - STILL_DRAWS
                for label, (a, b) in SWITCH_WINDOWS.items():
                    pick = (frames >= a) & (frames <= b)
                    print(f'{v} {light} {label} ({int(pick.sum())} draws)')
                    for name, (ry, rx) in cells.items():
                        print('  ' + line(name, stats(data[pick][:, ry, rx].reshape(-1, CHANNELS)), light))
            else:
                if mode == 'film':
                    numbers, data = numbers[-FILM_FRAMES:], data[-FILM_FRAMES:]
                print(f'{v} {light} draws {numbers.min()}-{numbers.max()} ({len(numbers)})')
                for name, (ry, rx) in cells.items():
                    print('  ' + line(name, stats(data[:, ry, rx].reshape(-1, CHANNELS)), light))
