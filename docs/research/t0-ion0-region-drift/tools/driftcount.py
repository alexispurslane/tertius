# driftcount.py <mode> <variant...>: the count of the drift test (TERTIUS_X_DRIFT_COUNT of the scratch probe, see
# take.sh). t0-xlcu's count.py with the drift test's five channels. Each variant's folder s/<variant>/drift/<process id>/
# holds <light>.f16, the counted draws one after the other: per pixel of every second render pixel of every second row,
# the drift |Rnew - Rold| / Rold, Q1's tolerance, whether Q0 and whether Q1 gives the pixel's block evidence (1 or 0),
# and a flag (1 a surface pixel with a valid test, 0 else). draws.txt lists each draw's run number. A variant's count
# comes from the process that counted the most draws, and a process whose command line names a film's references or
# its exposure run is skipped. Regions: the whole image and the survey's 3 x 3 grid, r<row><column> from the top left,
# of the counted pixels.
#
# Per region it prints the median and 90th percentile of the drift and of Q1's tolerance, and these shares of the
# counted pixel draws: the pixel's own test fires (drift above 0.10 for Q0, above Q1's tolerance for Q1, above 1.5
# times Q1's tolerance for Q2, which is Q1 at 3 standard deviations); the block flags that the shader wrote ("shader",
# Q0 and Q1); and the block has evidence by the rule of step 2 (the test of the block's center pixel, local pixel (4, 4),
# fires, "center", Q0, Q1 and Q2). The first build's shader flagged a block when any of its 64 pixels fired. The second
# build's shader flags it by the center pixel, so there "shader" must equal "center". The center pixels are even, so the
# count holds them: in its grid of every second pixel, (4 bx + 2, 4 by + 2).
# Modes:
#   still: over every counted draw.
#   switch: in windows of the cabin switch's film frames (film frame k is run draw 33 + k): 140 to 159 before the lamps
#          go off, and four parts of the 60 draws after each switch by d = frame - switch frame.
#   film: over the last 180 counted draws of the film process, the film's 180 frames.
import sys, numpy as np
from pathlib import Path

STILL_DRAWS = 33
SWITCH_WINDOWS = {'before 140-159': (140, 159)}
for state, first in (('off', 160), ('on', 360)):
    for a, b in ((0, 5), (6, 17), (18, 35), (36, 59)):
        SWITCH_WINDOWS[f'{state} d {a}-{b}'] = (first + a, first + b)
Q0 = 0.10
Q2_OVER_Q1 = 1.5  # Q2's tolerance over Q1's: 3 standard deviations against 2
CHANNELS = 5  # drift, Q1's tolerance, block Q0, block Q1, flag
FILM_FRAMES = 180

def load(variant):
    """The counts of the process that counted the most draws: {light: (numbers, array of draws x h x w x CHANNELS)}."""
    best = None
    for folder in Path(f's/{variant}/drift').iterdir():
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
        out[light] = (np.array([int(r[0]) for r in mine]), with_center(raw.reshape(len(mine), h, w, CHANNELS)))
    return out

def with_center(data):
    """data with three more channels: whether the test of the pixel's block center gives Q0, Q1 and Q2 evidence."""
    center = data[:, 2::4, 2::4, :]
    valid = center[..., 4] >= 1
    bits = np.stack([valid & (center[..., 0] > Q0), valid & (center[..., 0] > center[..., 1]),
                     valid & (center[..., 0] > Q2_OVER_Q1 * center[..., 1])], axis=-1)
    bits = np.repeat(np.repeat(bits, 4, axis=1), 4, axis=2)[:, :data.shape[1], :data.shape[2]]
    return np.concatenate([data, bits.astype(np.float32)], axis=-1)

def regions(h, w):
    cells = {'whole': (slice(0, h), slice(0, w))}
    ys, xs = np.linspace(0, h, 4).astype(int), np.linspace(0, w, 4).astype(int)
    for r in range(3):
        for c in range(3):
            cells[f'r{r}{c}'] = (slice(ys[r], ys[r + 1]), slice(xs[c], xs[c + 1]))
    return cells

def line(name, block):
    """One region's numbers over some draws, from its pixel draws (n x CHANNELS)."""
    counted = block[block[:, 4] >= 1]
    if len(counted) == 0:
        return f'{name:6s} nothing counted'
    drift, tolerance = counted[:, 0], counted[:, 1]
    p = lambda a: '/'.join(f'{v:.3f}' for v in np.percentile(a, [50, 90]))
    share = lambda m: f'{100 * float(np.mean(m)):5.1f}%'
    return (f'{name:6s} drift {p(drift)}  Q1 tol {p(tolerance)}  pixel Q0 {share(drift > Q0)} Q1 {share(drift > tolerance)}'
            f' Q2 {share(drift > Q2_OVER_Q1 * tolerance)}  shader Q0 {share(counted[:, 2] > 0.5)} Q1 {share(counted[:, 3] > 0.5)}'
            f'  center Q0 {share(counted[:, 5] > 0.5)} Q1 {share(counted[:, 6] > 0.5)} Q2 {share(counted[:, 7] > 0.5)}')

if __name__ == '__main__':
    mode, variants = sys.argv[1], sys.argv[2:]
    print('# percentiles 50/90; shares of the counted pixel draws: the pixel\'s own test fires, the shader\'s block flag, the block\'s center pixel fires')
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
                        print('  ' + line(name, data[pick][:, ry, rx].reshape(-1, CHANNELS + 3)))
            else:
                if mode == 'film':
                    numbers, data = numbers[-FILM_FRAMES:], data[-FILM_FRAMES:]
                print(f'{v} {light} draws {numbers.min()}-{numbers.max()} ({len(numbers)})')
                for name, (ry, rx) in cells.items():
                    print('  ' + line(name, data[:, ry, rx].reshape(-1, CHANNELS + 3)))
