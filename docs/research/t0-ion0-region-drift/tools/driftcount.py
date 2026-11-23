# driftcount.py <mode> <variant...>: the count of the drift test (TERTIUS_X_DRIFT_COUNT of the scratch probe, see
# take.sh). t0-xlcu's count.py with the drift test's channels. Each variant's folder s/<variant>/drift/<process id>/
# holds <light>.f16, the counted draws one after the other: per pixel of every second render pixel of every second row,
# the drift |Rnew - Rold| / Rold, Q1's tolerance, whether Q0 and whether Q1 gives the pixel's block evidence (1 or 0),
# and a flag (1 a surface pixel with a valid test, 0 else). The third build adds three channels: phase3 and phase6,
# |the mean of the last m differences F(t - k) - F(t - k - 18)| / Rold for m = 3 and 6, and whether Q2 gives the
# block evidence. A count of the earlier builds has no Q2 (its Q2 shares print as nan). draws.txt lists each draw's run number. A variant's count
# comes from the process that counted the most draws, and a process whose command line names a film's references or
# its exposure run is skipped. Regions: the whole image and the survey's 3 x 3 grid, r<row><column> from the top left,
# of the counted pixels.
#
# Per region it prints the median and 90th percentile of the drift and of Q1's tolerance, and these shares of the
# counted pixel draws: the pixel's own test fires (drift above 0.10 for Q0, above Q1's tolerance for Q1, above 1.5
# times Q1's tolerance for Q3, which is Q1 at 3 standard deviations, and Q2 at any of its lengths); the block flags
# that the shader wrote ("shader", Q0, Q1 and Q2); and the block has evidence by the rule of step 2 (the test of the
# block's center pixel, local pixel (4, 4), fires, "center", Q0, Q1, Q3 and Q2, then Q2's lengths m = 3, 6 and 18
# alone; m = 18 is Q1). The first build's shader flagged a block when any of its 64 pixels fired. The second
# build's shader flags it by the center pixel, so there "shader" must equal "center". The center pixels are even, so the
# count holds them: in its grid of every second pixel, (4 bx + 2, 4 by + 2).
# Modes:
#   still: over every counted draw.
#   switch: in windows of the cabin switch's film frames (film frame k is run draw 33 + k): 140 to 159 before the lamps
#          go off, and four parts of the 60 draws after each switch by d = frame - switch frame.
#   film: over the last 180 counted draws of the film process, the film's 180 frames.
#   wait: the wait before the first cut. For each switch of the cabin (variant <name>, the switch run) and for each d
#          from 0 to 59, and for the film (variant <name>:film) for each frame from 0 to 59: the share of the counted
#          pixel draws of the changed region whose block has evidence under Q0, Q1, Q3 and Q2. The changed region of the
#          cabin is the union of the regions that the survey counts as changed (the reference switch result's "regions",
#          REFERENCE below), and of the film the whole image. It prints the first d (or frame) at which each share
#          reaches one half, and the shares at a few d.
import sys, numpy as np
from pathlib import Path

STILL_DRAWS = 33
SWITCH_WINDOWS = {'before 140-159': (140, 159)}
for state, first in (('off', 160), ('on', 360)):
    for a, b in ((0, 5), (6, 17), (18, 35), (36, 59)):
        SWITCH_WINDOWS[f'{state} d {a}-{b}'] = (first + a, first + b)
Q0 = 0.10
Q3_OVER_Q1 = 1.5  # Q3's tolerance over Q1's: 3 standard deviations against 2
WINDOW = 18
CHANNELS = 8  # drift, Q1's tolerance, block Q0, block Q1, flag, phase3, phase6, block Q2
CENTER = 6  # center bits: Q0, Q1, Q3, Q2, Q2 at m = 3, Q2 at m = 6
REFERENCE = 's/t-A-sw/results/switch/cabin.json'
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
        channels = raw.size // (len(mine) * h * w)
        data = raw.reshape(len(mine), h, w, channels)
        if channels < CHANNELS:
            data = np.concatenate([data, np.full(data.shape[:3] + (CHANNELS - channels,), np.nan, np.float32)], axis=-1)
        out[light] = (np.array([int(r[0]) for r in mine]), with_center(data))
    return out

def with_center(data):
    """data with CENTER more channels: whether the test of the pixel's block center gives Q0, Q1, Q3, Q2, Q2 at m = 3
    and Q2 at m = 6 evidence (nan for Q2 in a count without its channels)."""
    center = data[:, 2::4, 2::4, :]
    valid = center[..., 4] >= 1
    drift, tolerance = center[..., 0], center[..., 1]
    m3 = valid & (center[..., 5] > tolerance * np.sqrt(WINDOW / 3))
    m6 = valid & (center[..., 6] > tolerance * np.sqrt(WINDOW / 6))
    q1 = valid & (drift > tolerance)
    bits = np.stack([valid & (drift > Q0), q1, valid & (drift > Q3_OVER_Q1 * tolerance), q1 | m3 | m6, m3, m6],
                    axis=-1).astype(np.float32)
    bits[..., 3:][np.isnan(center[..., 5])] = np.nan
    bits = np.repeat(np.repeat(bits, 4, axis=1), 4, axis=2)[:, :data.shape[1], :data.shape[2]]
    return np.concatenate([data, bits], axis=-1)

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
    bit = lambda c: share(counted[:, c] > 0.5) if not np.isnan(counted[:, c]).any() else '  nan'
    pixel2 = (drift > tolerance) | (counted[:, 5] > tolerance * np.sqrt(WINDOW / 3)) | (counted[:, 6] > tolerance * np.sqrt(WINDOW / 6))
    c = CHANNELS
    return (f'{name:6s} drift {p(drift)}  Q1 tol {p(tolerance)}  pixel Q0 {share(drift > Q0)} Q1 {share(drift > tolerance)}'
            f' Q3 {share(drift > Q3_OVER_Q1 * tolerance)} Q2 {share(pixel2) if not np.isnan(counted[:, 5]).any() else "  nan"}'
            f'  shader Q0 {bit(2)} Q1 {bit(3)} Q2 {bit(7)}'
            f'  center Q0 {bit(c)} Q1 {bit(c + 1)} Q3 {bit(c + 2)} Q2 {bit(c + 3)} [m3 {bit(c + 4)} m6 {bit(c + 5)} m18 {bit(c + 1)}]')

def wait(label, frames, data, mask, first):
    """The wait before the first cut over the frames first to first + 59 (d = frame - first), in the pixels of mask."""
    names = ('Q0', 'Q1', 'Q3', 'Q2')
    curves = {n: [] for n in names}
    for d in range(60):
        pick = frames == first + d
        block = data[pick][:, mask].reshape(-1, CHANNELS + CENTER)
        counted = block[block[:, 4] >= 1]
        for k, n in enumerate(names):
            bits = counted[:, CHANNELS + k]
            curves[n].append(float(np.mean(bits > 0.5)) if len(counted) and not np.isnan(bits).any() else float('nan'))
    cells = []
    for n in names:
        c = np.array(curves[n])
        hit = np.nonzero(c >= 0.5)[0]
        at = ' '.join(f'{100 * c[d]:3.0f}' for d in (0, 3, 5, 10, 15, 20, 30))
        cells.append(f"{n} first {('d ' + str(hit[0])) if len(hit) else 'never':8s} [{at}]")
    print(f'  {label:10s} ' + '  '.join(cells))

if __name__ == '__main__':
    mode, variants = sys.argv[1], sys.argv[2:]
    print('# percentiles 50/90; shares of the counted pixel draws: the pixel\'s own test fires, the shader\'s block flag, the block\'s center pixel fires')
    if mode == 'wait':
        import json
        print('# wait: first d with half of the changed region\'s pixel draws under block evidence; [shares in % at d 0 3 5 10 15 20 30]')
        changed = json.load(open(REFERENCE))['regions']
        for arg in variants:
            v, _, kind = arg.partition(':')
            numbers, data = load(v)['diffuse']
            h, w = data.shape[1:3]
            cells = regions(h, w)
            names = ['whole'] + [f'r{r}{c}' for r in range(3) for c in range(3)]
            mask = np.zeros((h, w), bool)
            if kind == 'film':
                mask[:] = True
                print(f'{v} diffuse film, whole image')
                wait('film', numbers - (numbers[-FILM_FRAMES:].min()), data, mask, 0)
            else:
                for index in changed:
                    if index > 0:
                        mask[cells[names[index]]] = True
                print(f'{v} diffuse, changed regions {" ".join(names[i] for i in changed if i > 0)}')
                for state, first in (('off', 160), ('on', 360)):
                    wait(state, numbers - STILL_DRAWS, data, mask, first)
        sys.exit(0)
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
                        print('  ' + line(name, data[pick][:, ry, rx].reshape(-1, CHANNELS + CENTER)))
            else:
                if mode == 'film':
                    numbers, data = numbers[-FILM_FRAMES:], data[-FILM_FRAMES:]
                print(f'{v} {light} draws {numbers.min()}-{numbers.max()} ({len(numbers)})')
                for name, (ry, rx) in cells.items():
                    print('  ' + line(name, data[:, ry, rx].reshape(-1, CHANNELS + CENTER)))
