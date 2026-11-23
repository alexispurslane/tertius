# Tool code of the region take (t0-qvm1), its second step. regionrules.py <regionstat json> [chart prefix]: what the log of
# regionstat.py says about candidate repairs of the region test. No rule is built; every number comes from the log.
# The windows (regionstat.py's kinds): the false fires are the fires in a still (a still run, the switch's draws 69 to
# 192, a film's draws before its change); the true fires are the fires in the 40 draws from a real change (the
# switch's off at 193 and on at 393, a film's first changing draw). The walk's fires after the stop are listed apart.
# It prints:
#   1. the quantiles (10, 25, 50, 75, 90, 99 %) of d (the drift's relative size) and of the statistic, for the false and
#      the true fires of each run;
#   2. per candidate rule, the share of the false fires that it drops, and at each real change the delay of the first
#      fire in draws (the earliest anchor), and over the anchors that fire under today's rule in the window, the median
#      and the largest extra delay of each anchor's first fire, and the share of them that never fire in the window:
#        today: stat > 3;  A4, A5: stat > 4, > 5;  B2%, B5%, B10%: stat > 3 and d >= 2 %, 5 %, 10 %;
#   3. design B2 of the line orchestrator, the cut in the action: n*(d) = 18 b / d with b = 5 %, kept between 8 and 32
#      (a drift under 2.8 % cuts nothing), for every tested anchor-draw ("alone") or only where stat > 3 ("after the
#      test", else 32). In the stills: the share of tested anchor-draws with n* under 32 and the mean n*, beside today's
#      cut to 8 at every fire. At each real change: the draws until n* first reaches 8 at any anchor, and per draw of the
#      first 40 the smallest n* over the anchors and the 10th percentile.
#   4. over the false fires: B2's mean n* after the test, and the share of them that B2 cuts to 8 (today cuts every one to 8);
#   5. in a film, over every fire from its change to its end (a moving scene's fires are true all through): the share of
#      them that each rule keeps.
# With a chart prefix it draws <prefix>-quantiles.png (the cumulative share of the false and the true fires against d
# and against the statistic, per run) and <prefix>-nstar.png (B2's smallest n* per draw after each real change).
import json
import sys
import numpy as np
from PIL import Image, ImageDraw

data = json.loads(open(sys.argv[1]).read())
prefix = sys.argv[2] if len(sys.argv) > 2 else None
RULES = {'today': lambda d, s: s > 3, 'A4': lambda d, s: s > 4, 'A5': lambda d, s: s > 5,
         'B2%': lambda d, s: s > 3 and d >= 0.02, 'B5%': lambda d, s: s > 3 and d >= 0.05,
         'B10%': lambda d, s: s > 3 and d >= 0.10}
Q = (10, 25, 50, 75, 90, 99)


def events_of(kind):
    if kind == 'switch':
        return [('off', 193), ('on', 393)], (69, 193)
    if kind.startswith('film:'):
        start = int(kind.split(':')[1])
        return [('change', start)], (0, start)
    if kind == 'still':
        return [], (0, 10 ** 9)
    return [], (0, 0)


def nstar(d):
    return float(np.clip(18 * 0.05 / d, 8, 32)) if d > 0 else 32.0


summary = {'false': {r: [0, 0] for r in RULES}}
curves = []
for label, run in data.items():
    events, still = events_of(run['kind'])
    rows = [r for rows in run['runs'] for r in rows]
    false = [r for r in rows if still[0] <= r[0] < still[1] and r[4]]
    stills = [r for r in rows if still[0] <= r[0] < still[1]]
    print(f'\n== {label} ({run["kind"]}): {len(rows)} tested anchor-draws, {sum(r[4] for r in rows)} fires; '
          f'{len(stills)} in still draws, {len(false)} false fires there')
    trues = {}
    for name, at in events:
        trues[name] = [r for r in rows if at <= r[0] < at + 40 and r[4]]
    if run['kind'] == 'walk':
        fires = [r for r in rows if r[4]]
        print(f'  the walk: {len(fires)} fires, first at draw {min((r[0] for r in fires), default=None)}; d quantiles '
              + ' '.join(f'{q}% {np.percentile([r[2] for r in fires], q):.4f}' for q in Q) if fires else '  no fires')
    for kind_name, group in [('false', false)] + [(f'true {n}', g) for n, g in trues.items()]:
        if not group:
            print(f'  {kind_name}: none')
            continue
        ds, ss = np.array([r[2] for r in group]), np.array([r[3] for r in group])
        print(f'  {kind_name} fires ({len(group)}): d ' + ' '.join(f'{q}% {np.percentile(ds, q):.4f}' for q in Q)
              + ' | stat ' + ' '.join(f'{q}% {np.percentile(ss, q):.2f}' for q in Q))
        curves.append((label, kind_name, np.sort(ds), np.sort(ss)))
    # The rules.
    for rule, test in RULES.items():
        kept = sum(test(r[2], r[3]) for r in false)
        summary['false'][rule][0] += kept
        summary['false'][rule][1] += len(false)
        line = f'  {rule:5s}: keeps {kept} of {len(false)} false fires ({100 * (1 - kept / len(false)) if false else 0:.1f} % dropped)'
        for name, at in events:
            window = [r for r in rows if at <= r[0] < at + 40]
            first = min((r[0] for r in window if test(r[2], r[3])), default=None)
            base_first = {}
            for r in window:
                if r[3] > 3 and r[1] not in base_first:
                    base_first[r[1]] = r[0]
            rule_first = {}
            for r in window:
                if test(r[2], r[3]) and r[1] not in rule_first:
                    rule_first[r[1]] = r[0]
            extra = [rule_first[a] - base_first[a] for a in base_first if a in rule_first]
            lost = sum(a not in rule_first for a in base_first)
            line += (f'; {name}: first fire {"none" if first is None else first - at} draws, anchors {len(base_first)}, '
                     f'extra delay median {np.median(extra) if extra else float("nan"):.0f} largest '
                     f'{max(extra) if extra else float("nan")}, never {lost} ({100 * lost / max(len(base_first), 1):.1f} %)')
        print(line)
    # B2.
    if stills:
        for variant in ('alone', 'after the test'):
            ns = [nstar(r[2]) if (variant == 'alone' or r[3] > 3) else 32.0 for r in stills]
            print(f'  B2 {variant}: in the stills {100 * np.mean(np.array(ns) < 32):.2f} % of the tested anchor-draws cut '
                  f'(n* under 32), mean n* {np.mean(ns):.2f}; today: {100 * np.mean([r[4] for r in stills]):.2f} % cut to 8, '
                  f'mean {np.mean([8 if r[4] else 32 for r in stills]):.2f}')
    if false:
        ns = [nstar(r[2]) for r in false]
        print(f'  B2 after the test over the false fires: mean n* {np.mean(ns):.1f}, cut to 8: {100 * np.mean(np.array(ns) <= 8):.1f} %')
    if run['kind'].startswith('film:'):
        film = [r for r in rows if r[0] >= events[0][1] and r[4]]
        print('  the film\'s fires from its change on (' + str(len(film)) + '): ' + ', '.join(
            f'{rule} keeps {100 * np.mean([test(r[2], r[3]) for r in film]):.1f} %' for rule, test in RULES.items()))
    for name, at in events:
        for variant in ('alone', 'after the test'):
            per = []
            for k in range(40):
                ns = [nstar(r[2]) if (variant == 'alone' or r[3] > 3) else 32.0 for r in rows if r[0] == at + k]
                per.append((min(ns) if ns else 32.0, float(np.percentile(ns, 10)) if ns else 32.0))
            reach = next((k for k, (m, _) in enumerate(per) if m <= 8), None)
            print(f'  B2 {variant}, {name}: n* first reaches 8 after {"none in 40" if reach is None else reach} draws; '
                  f'smallest n* per draw: ' + ' '.join(f'{m:.0f}' for m, _ in per))
            curves.append((label, f'B2 {variant} {name}', per, None))
print('\n== pooled false fires: ' + '; '.join(f'{r} keeps {k} of {n} ({100 * (1 - k / n) if n else 0:.1f} % dropped)'
                                         for r, (k, n) in summary['false'].items()))

if prefix:
    colours = [(30, 30, 30), (220, 60, 60), (40, 110, 200), (40, 160, 80), (200, 140, 20), (140, 60, 180), (90, 90, 90),
               (230, 120, 170), (0, 160, 160), (150, 110, 60)]
    dist = [c for c in curves if c[3] is not None]
    W_, H_, L_ = 1400, 520, 60
    im = Image.new('RGB', (W_, H_), 'white'); g = ImageDraw.Draw(im)
    for p, (title, lo, hi, idx) in enumerate([('d, the drift\'s relative size (log scale)', 1e-4, 1.0, 2),
                                             ('the statistic (log scale)', 1.0, 1e3, 3)]):
        x0 = L_ + p * 680; pw = 600
        x = lambda v: x0 + (np.log10(max(v, lo)) - np.log10(lo)) / (np.log10(hi) - np.log10(lo)) * pw
        y = lambda f: 40 + (1 - f) * 380
        for dec in range(int(np.log10(lo)), int(np.log10(hi)) + 1):
            g.line([(x(10 ** dec), 40), (x(10 ** dec), 420)], fill=(225, 225, 225))
            g.text((x(10 ** dec) - 10, 425), f'{10 ** dec:g}', fill='black')
        for f in (0, 0.5, 1):
            g.line([(x0, y(f)), (x0 + pw, y(f))], fill=(225, 225, 225))
            if p == 0:
                g.text((8, y(f) - 6), f'{f:.1f}', fill='black')
        if idx == 3:
            g.line([(x(3), 40), (x(3), 420)], fill=(120, 120, 120))
        for j, (label, kind_name, ds, ss) in enumerate(dist):
            v = ds if idx == 2 else ss
            step = max(1, len(v) // 400)
            pts = [(x(val), y((i * step + 1) / len(v))) for i, val in enumerate(v[::step])] + [(x(v[-1]), y(1.0))]
            if len(pts) > 1:
                g.line(pts, fill=colours[j % len(colours)], width=2 if kind_name == 'false' else 3)
        g.text((x0, 8), title + ': the cumulative share of the fires', fill='black')
    for j, (label, kind_name, _, _) in enumerate(dist):
        g.text((L_ + 10 + (j % 4) * 330, 450 + (j // 4) * 16), f'{label}: {kind_name}', fill=colours[j % len(colours)])
    im.save(f'{prefix}-quantiles.png')
    nst = [c for c in curves if c[3] is None and 'after the test' in c[1]]
    im = Image.new('RGB', (900, 420), 'white'); g = ImageDraw.Draw(im)
    x = lambda k: 60 + k / 39 * 800
    y = lambda n: 40 + (32 - n) / 24 * 320
    for n in (8, 16, 24, 32):
        g.line([(60, y(n)), (860, y(n))], fill=(225, 225, 225)); g.text((20, y(n) - 6), str(n), fill='black')
    for j, (label, name, per, _) in enumerate(nst):
        g.line([(x(k), y(m)) for k, (m, _) in enumerate(per)], fill=colours[j % len(colours)], width=2)
        g.text((70 + (j % 3) * 270, 380 + (j // 3) * 16), f'{label} {name.replace("B2 after the test ", "")}', fill=colours[j % len(colours)])
    g.text((60, 10), "B2 after the test: the smallest n* over the anchors, per draw after a real change", fill='black')
    im.save(f'{prefix}-nstar.png')
