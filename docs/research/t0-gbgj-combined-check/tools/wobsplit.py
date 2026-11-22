# Tool code of t0-gbgj. wobsplit.py <size log> <label|dump|model>...: splits the wobble's per-frame edges (wobble.py's
# WOBBLE_DUMP lines "<frame> <distance> <midpoint> <width> <height> <scale>", output px) by whether the draw's render
# size changed against the draw before. model "aspect" predicts the distance as D0 * (h / w) * 16/9 (the projection
# takes its aspect from the rounded render size), model "flat" predicts a constant distance (the projection keeps the
# output's aspect). The residual of the distance is the distance minus the model's prediction, with D0 fitted by least
# squares. The midpoint's residual is the midpoint minus its mean: neither model moves the midpoint.
# Film frame k shows draw 33 + k. A draw "changed" when its render size differs from the draw before's. A step is the
# change of a value from the frame before to this frame. "Draws since a change" is 0 on a changed draw, 1 on the draw
# after it, and so on. A run is a stretch of draws of one size; between-run and within-run rms split the residual into
# a fixed offset per run and the scatter inside a run.
import sys
import numpy as np

STILL, SCALE = 33, 1.5
log = [tuple(l.split()[1:3]) for l in open(sys.argv[1])]


def load(path, model):
    a = np.loadtxt(path)
    frame, dist, mid, w, h, scale = a.T
    if model == 'aspect':
        ratio = h / w * 16 / 9
        pred = np.dot(dist, ratio) / np.dot(ratio, ratio) * ratio
    else:
        pred = np.full_like(dist, dist.mean())
    draws = (frame + STILL).astype(int)
    changed = np.array([log[d] != log[d - 1] for d in draws])
    since, n = [], 99
    for d in range(draws[0] - 60, draws[-1] + 1):
        n = 0 if log[d] != log[d - 1] else n + 1
        if d >= draws[0]:
            since.append(n)
    run = np.cumsum(changed)
    return dict(res=dist - pred, mid=mid - mid.mean(), w=w, h=h, scale=scale, changed=changed, since=np.array(since), run=run)


def stats(v, sel):
    x = v[sel]
    return f'{np.sqrt(np.mean(x ** 2)):.3f} / {x.max() - x.min():.2f}'


def steps(v):
    s = np.abs(np.diff(v))
    return np.concatenate([[np.nan], s])


sides = []
for spec in sys.argv[2:]:
    label, path, model = spec.split('|')
    sides.append((label, load(path, model)))

cols = []
for label, s in sides:
    cols += [(f'{label} change', s, s['changed']), (f'{label} one size', s, ~s['changed'])]
print('measure | ' + ' | '.join(n for n, _, _ in cols))
print('draws | ' + ' | '.join(str(int(sel.sum())) for _, _, sel in cols))
for name, key in (('distance residual rms / range (px of 1920)', 'res'), ('midpoint rms / range (px of 1920)', 'mid')):
    print(f'{name} | ' + ' | '.join(stats(s[key], sel) for _, s, sel in cols))
for name, key in (('distance step mean abs / largest (px of 1920)', 'res'), ('midpoint step mean abs / largest (px of 1920)', 'mid')):
    row = []
    for _, s, sel in cols:
        st = steps(s[key])
        x = st[sel & ~np.isnan(st)]
        row.append(f'{x.mean():.3f} / {x.max():.2f}')
    print(f'{name} | ' + ' | '.join(row))
for label, s in sides:
    print(f'\n{label}:')
    for key, name in (('res', 'distance residual'), ('mid', 'midpoint')):
        v = s[key]
        st = steps(v)
        big = int(np.nanargmax(st))
        print(f'  largest step of the {name}: {st[big]:.2f} px of 1920 = {st[big] / SCALE:.2f} px of 1280, at film frame '
              f'{140 + big} ({"changed" if s["changed"][big] else "one size"}, scale {s["scale"][big - 1]:.4f} -> {s["scale"][big]:.4f})')
        c = lambda u: float(np.corrcoef(v, u)[0, 1])
        print(f'  {name}: correlation with render scale {c(s["scale"]):+.3f}, render width {c(s["w"]):+.3f}, '
              f'changed draw {c(s["changed"].astype(float)):+.3f}')
        means = np.array([v[s['run'] == r].mean() for r in np.unique(s['run'])])
        lens = np.array([(s['run'] == r).sum() for r in np.unique(s['run'])])
        within = v - means[np.searchsorted(np.unique(s['run']), s['run'])]
        print(f'  {name}: between-run rms {np.sqrt(np.average(means ** 2, weights=lens)):.3f} px over {len(means)} runs, '
              f'within-run rms {np.sqrt(np.mean(within ** 2)):.3f} px')
        prof = []
        for n in (0, 1, 2, 3, 4, 5):
            sel = (s['since'] == n) if n < 5 else (s['since'] >= 5)
            sel = sel & ~np.isnan(st)
            prof.append(f'{n}{"+" if n == 5 else ""}: {np.mean(np.abs(v[sel])):.3f}/{np.mean(st[sel]):.3f} ({int(sel.sum())})')
        print(f'  {name} by draws since a change, mean abs value / mean abs step (draws): ' + ', '.join(prof))

# The per-size split of t0-gbgj's second question. Per tracked edge (left = midpoint - distance / 2, right = midpoint +
# distance / 2, each minus the model's prediction of it, which stretches the distance about the midpoint's mean), the
# mean for each distinct render size, and the share of the residual's variance that those means explain: the variance
# of the per-size mean over the draws, divided by the whole variance. "No change draws" leaves out the draws whose size
# changed. The alignment check refits the aspect model with the size log moved by one draw either way.
print('\nper-size share of the variance (all draws / no change draws), distinct sizes; left-right correlation:')
for label, s in sides:
    left, right = s['mid'] - s['res'] / 2, s['mid'] + s['res'] / 2
    key = s['w'] * 10000 + s['h']  # one key per render size (width and height)
    out = []
    for name, v in (('left', left), ('right', right), ('distance', s['res']), ('midpoint', s['mid'])):
        shares = []
        for sel in (np.ones(len(v), bool), ~s['changed']):
            x, k = v[sel] - v[sel].mean(), key[sel]
            means = {u: x[k == u].mean() for u in np.unique(k)}
            fitted = np.array([means[u] for u in k])
            shares.append(np.var(fitted) / np.var(x))
        out.append(f'{name} {shares[0]:.2f} / {shares[1]:.2f}')
    print(f'  {label}: {len(np.unique(key))} sizes; ' + ', '.join(out) +
          f'; left-right correlation {np.corrcoef(left, right)[0, 1]:+.3f} (no change draws '
          f'{np.corrcoef(left[~s["changed"]], right[~s["changed"]])[0, 1]:+.3f})')
for spec in sys.argv[2:]:
    label, path, model = spec.split('|')
    if model != 'aspect':
        continue
    a = np.loadtxt(path)
    dist, draws = a[:, 1], (a[:, 0] + STILL).astype(int)
    for shift in (-1, 0, 1):
        if draws[-1] + shift >= len(log):
            draws = draws[:-1]
            dist = dist[:-1]
        w = np.array([int(log[d + shift][0]) for d in draws])
        h = np.array([int(log[d + shift][1]) for d in draws])
        ratio = h / w * 16 / 9
        pred = np.dot(dist, ratio) / np.dot(ratio, ratio) * ratio
        ch = np.array([log[d] != log[d - 1] for d in draws])
        r = dist - pred
        print(f'  alignment {label}: size log moved {shift:+d} draw: residual rms {np.sqrt(np.mean(r ** 2)):.3f} px, '
              f'on change draws {np.sqrt(np.mean(r[ch] ** 2)):.3f}, on one-size draws {np.sqrt(np.mean(r[~ch] ** 2)):.3f}')

# The lamps go off at film frame 160 (draw 193) and on at frame 360 (draw 393), and 79 of the 84 size changes fall in
# the 60 frames after a switch, while the light itself still settles. So the split above mixes the size change with the
# light's change. This split keeps only the frames 160 to 219 and 360 to 419, where both kinds of draw occur.
print('\ninside the 60 frames after each lamp switch: changed / one size, rms of the value and mean abs step (draws):')
for label, s in sides:
    frame = np.arange(140, 140 + len(s['res']))
    win = ((frame >= 160) & (frame < 220)) | ((frame >= 360) & (frame < 420))
    row = []
    for key, name in (('res', 'distance'), ('mid', 'midpoint')):
        v, st = s[key], steps(s[key])
        part = []
        for sel in (win & s['changed'], win & ~s['changed']):
            sel = sel & ~np.isnan(st)
            part.append(f'{np.sqrt(np.mean(v[sel] ** 2)):.3f}/{np.mean(st[sel]):.3f} ({int(sel.sum())})')
        row.append(f'{name} ' + ' against '.join(part))
    print(f'  {label}: ' + '; '.join(row))

print('\nper edge: correlation with draws since a change and with the render width:')
for label, s in sides:
    left, right = s['mid'] - s['res'] / 2, s['mid'] + s['res'] / 2
    c = lambda u, v: float(np.corrcoef(u, v)[0, 1])
    print(f'  {label}: ' + ', '.join(f'{n} {c(v, s["since"]):+.3f} / {c(v, s["w"]):+.3f}'
                                     for n, v in (('left', left), ('right', right), ('distance', s['res']), ('midpoint', s['mid']))))

# The residual against the render scale: a straight line res = a + b * scale by least squares, its slope b, and what the
# line leaves, split by changed and one-size draws, inside the 60 frames after each switch and in all draws.
print('\nthe distance residual against a straight line in the render scale:')
for label, s in sides:
    frame = np.arange(140, 140 + len(s['res']))
    win = ((frame >= 160) & (frame < 220)) | ((frame >= 360) & (frame < 420))
    for key, name in (('res', 'distance'), ('mid', 'midpoint')):
        v = s[key]
        b, a = np.polyfit(s['scale'], v, 1)
        left = v - (a + b * s['scale'])
        r = lambda sel: f'{np.sqrt(np.mean(left[sel] ** 2)):.3f}'
        print(f'  {label} {name}: slope {b / 10:+.3f} px per 0.1 of scale, scale from {s["scale"].min():.3f} to '
              f'{s["scale"].max():.3f}; rms left over {np.sqrt(np.mean(v ** 2)):.3f} -> {np.sqrt(np.mean(left ** 2)):.3f}; '
              f'changed / one size {r(s["changed"])} / {r(~s["changed"])}, inside the switch windows '
              f'{r(win & s["changed"])} / {r(win & ~s["changed"])}')
