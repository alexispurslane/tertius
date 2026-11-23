# Tool code of the region repair's replay (t0-qvm1), part 2. measure.py <out folder> [p of each still as name=value ...]:
# the candidates of replay.py on every case, CPU only. It prints the tables and writes <out>/replay.json.
# The cases and their logs:
#   stills, every fire false: exit-cabin (t0-qvm1-region-off/logs/g-exit, scale 2/3), and from queue.sh's runs on main
#     165a964e7: the lamp hall (1072 x 603, P = 25), the bay and the still cabin (scale 2/3). The surface blocks are the
#     blocks that hold a pixel with a history in the run's dump at draw 890 (exit-cabin: the dump of its bounce run).
#     The still measures are means over the draws from 400 to the end (part 1: the still cabin is frozen from about
#     draw 400 on).
#     The model check: T's share of short blocks at the dump draws against the real dump's, where a real block is short
#     when the median length of its surface pixels is under 31.5 draws.
#   the fair cabin switch (lamps off at draw 193, on at 393; t0-gbgj's map-xfd with the rule and map-xfn without it,
#     logs/switch-rule and logs/switch-none): the settle after off (200 draws) and after on (the 100 draws to the end), of
#     the whole picture, of the slowest of the 3 x 3 regions, of the slowest anchor and the mean over the anchors. The
#     settled level is the light of the switch's controls (t0-qvm1-region-off/logs/g-switch: lamps on through the film,
#     and lamps off through it), the mean of x over their draws 100 to the end. Only the parts whose settled light the
#     switch changes by more than 10 % count (the survey's REGION_CHANGE). Settle is the first draw after the switch from
#     which the model light stays within 5 % of the settled level to the window's end; "none" if the window's last draw
#     is outside.
#   bay-sun (g-lag, its first process: 33 still draws, then the film) and rect-bay (g-bay, its third process: the film):
#     the lag of the model light behind the logged light, the survey's lag(): (still - film) / slope, with the region
#     mean of the logged x over the draws t - 4 to t + 4 as the still, the model light as the film, and the model light's
#     slope over t - 5 to t + 5; at bay-sun's survey samples (film frames 60, 120, 180 and 230) and at rect-bay's film
#     draws 50, 90, 130 and 170, for the whole picture and the 3 x 3 regions; the median and the largest. The same per
#     anchor (the blocks nearest it), every anchor-sample pooled: the median and the 90th percentile.
#   the switch also: the model light 20 and 40 draws after off and after on, as a share of the settled level (the mean
#     over the anchors whose settled light the switch changes by more than 10 %).
#   the cabin with its lamps off (the switch's control g-switch/region-0-100000000, 493 draws): a fifth frozen still, its
#     frozen part from draw 100 on.
#   E' (replay.py): per still, the share of the frozen part's tested draws whose update it skipped, and the median of its m
#     over the frozen part against E's m of the same span.
#   the still cabin's draws 100 to 299, a weak true drift (part 1), on the log with the rule (r2-main-cabin-full) and on
#     the log without it (r2-noreg-cabin-full): the caps that each candidate gives at the anchor-draws where today's rule
#     fires in those draws (10, 50 and 90 %), and per anchor the mean of the model light over draws 100 to 199 and 200 to
#     299 against the anchor's logged light over the last 100 draws (the median over the anchors, as part 1 measured the
#     logged light: -0.54 % and -0.11 % with the rule, -1.44 % and -0.57 % without it).
# A region of the picture here is a set of render blocks, and its light is the mean of its blocks' model light.
import json
import math
import re
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import replay as R  # noqa: E402

out = Path(sys.argv[1])
out.mkdir(parents=True, exist_ok=True)
PS = {k: float(v) for k, v in (a.split('=') for a in sys.argv[2:])}
WINDOW = 0.05
CHANGE = 0.10
SWITCH_OFF, SWITCH_ON = 193, 393


def dump(run, draw):
    path = next((run / 'dump').glob(f'history-{draw}-*.f32'))
    w, h = map(int, re.search(r'-(\d+)x(\d+)\.f32$', path.name).groups())
    return np.fromfile(path, dtype=np.float32).reshape(h, w), (w, h)


def blockwise(L):
    """Per render block of a dump: whether it holds a surface pixel, and the median length of its surface pixels."""
    h, w = L.shape
    by, bx = (h + 7) // 8, (w + 7) // 8
    pad = np.full((by * 8, bx * 8), np.nan, np.float32)
    pad[:h, :w] = np.where(L > 0, L, np.nan)
    tiles = pad.reshape(by, 8, bx, 8).transpose(0, 2, 1, 3).reshape(by, bx, 64)
    surface = ~np.isnan(tiles).all(-1)
    with np.errstate(all='ignore'):
        median = np.nanmedian(np.where(surface[..., None], tiles, 0), -1)
    return surface, median


def regions(shape):
    """Masks of the whole picture and of the 3 x 3 regions, in blocks."""
    by, bx = shape
    ys, xs = np.mgrid[0:by, 0:bx]
    masks = {'whole': np.ones(shape, bool)}
    for r in range(3):
        for c in range(3):
            masks[f'r{r}{c}'] = (ys * 3 // by == r) & (xs * 3 // bx == c)
    return masks


def anchor_masks(near):
    return {int(a): near == a for a in np.unique(near)}


def settle(series, level, start, end):
    """The first draw from start after which |series / level - 1| <= WINDOW up to end (exclusive), or None."""
    bad = np.abs(series[start:end] / level - 1) > WINDOW
    if bad[-1]:
        return None
    idx = np.nonzero(bad)[0]
    return 0 if len(idx) == 0 else int(idx[-1] + 1)


STILLS = [
    # name, log, process, P values (the first the candidates' P), render size, the run whose dumps give the surface and
    # the model check (deleted after the first take), dump draws, the first draw of the frozen part
    ('exit-cabin', R.OLD / 'g-exit' / 'region-none.txt', None, (18,), (1280, 720), R.RUNS / 'r2-main-exit-bounce', (630, 890), 400),
    ('lamp hall', R.LOGS / 'r2-main-lamp-full' / 'region-none.txt', None, (25, 18), (1072, 603), R.RUNS / 'r2-main-lamp-full', (630, 890), 400),
    ('bay', R.LOGS / 'r2-main-bay-full' / 'region-none.txt', None, (18,), (1280, 720), R.RUNS / 'r2-main-bay-full', (630, 890), 400),
    ('still cabin', R.LOGS / 'r2-main-cabin-full' / 'region-none.txt', None, (18,), (1280, 720), R.RUNS / 'r2-main-cabin-full', (190, 890), 400),
    # The switch's lamps-off control: 493 draws, so its frozen part starts at draw 100 (its share from 400 is printed too).
    ('cabin, lamps off', R.OLD / 'g-switch' / 'region-0-100000000.txt', None, (18,), (1280, 720), None, (), 100),
]
results = {'stills': {}, 'switch': {}, 'lag': {}, 'caps': {}, 'fires': {}}
ORDER = None

for name, log, process, Ps, (w, h), run, dumps, start in STILLS:
    draws, x, g, moved = R.load(log, process)
    holders, near = R.block_grid(w, h, x.shape[1], x.shape[2])
    has_dumps = run is not None and (run / 'dump').exists()
    if has_dumps:
        L890, _ = dump(run, 890)
        surface, _ = blockwise(L890)
    else:
        # The surface from the log: the blocks whose nearest anchor is valid in at least half of the draws.
        surface = (g.reshape(len(g), -1)[:, near] > 0).mean(0) >= 0.5
        dumps = ()
    late = draws >= start
    for P in Ps:
        caps, st = R.candidates(x, g, P, extra_p=(PS[name],) if name in PS else ())
        ORDER = ORDER or list(caps)
        label = name if P == Ps[0] else f'{name}, P = {P}'
        print(f'\n== {label}: {x.shape[0]} draws, {int(st["tested"].sum())} tested anchor-draws, T fires at '
              f'{100 * st["today"].sum() / st["tested"].sum():.2f} %')
        if P == 18:
            agree = int((st['today'] == moved)[st['tested']].sum())
            print(f'  T at P = W = 18 against the logged answer: {agree} of {int(st["tested"].sum())} tested anchor-draws '
                  f'agree, {int((st["today"] != moved).sum())} draws disagree in all')
        row = {}
        for cand, cap in caps.items():
            Lm, _ = R.model(cap, x, g, holders, near)
            short = (Lm < 31.5) & surface
            share = short[late].sum((1, 2)) / surface.sum()
            noise = ((np.sqrt(32 / Lm) - 1) * surface)[late].sum((1, 2)) / surface.sum()
            at = {d: float(short[np.searchsorted(draws, d)].sum() / surface.sum()) for d in dumps}
            row[cand] = {'share': float(share.mean()), 'noise': float(noise.mean()), 'at': at,
                         'share400': float((short[draws >= 400].sum((1, 2)) / surface.sum()).mean()) if (draws >= 400).any() else None}
            if cand.startswith("E'") and not cand.startswith("E'C"):
                k = int(cand[2:])
                G = st['gated'][k]
                frozen = st['tested'] & late[:, None, None]
                row[cand]['skipped'] = float(G['skipped'][frozen].sum() / max(frozen.sum(), 1))
                row[cand]['skippedAll'] = float(G['skipped'][st['tested']].sum() / max(st['tested'].sum(), 1))
                row[cand]['mRatio'] = float(np.median(G['m'][frozen]) / np.median(st['measures'][k][frozen]))
            if cand.startswith('E') and cand[1:].isdigit():
                k = int(cand[1:])
                N = k * (P + R.W)
                fire = st['tested'] & (cap < 32)
                first = np.argmax(st['tested'].any((1, 2)))
                after = np.arange(len(draws)) >= first + N
                tested_after = st['tested'] & after[:, None, None]
                m = st['measures'][k]
                med = np.array([np.median(m[t][st['tested'][t]]) if st['tested'][t].any() else np.nan
                                for t in range(len(draws))])
                end = np.nanmedian(med[-len(draws) // 3:])
                fill = np.nonzero(med >= 0.9 * end)[0]
                row[cand]['false'] = float(fire[tested_after].sum() / max(tested_after.sum(), 1))
                row[cand]['fill'] = int(fill[0] - first) if len(fill) else None
            if cand in ('T', 'none'):
                pass
        real = {}
        for d in (dumps if has_dumps else ()):
            Ld, _ = dump(run, d)
            s, med = blockwise(Ld)
            real[d] = float(((med < 31.5) & surface).sum() / surface.sum())
        results['stills'][label] = {'rows': row, 'real': real, 'P': P,
                                    'tFireRate': float(st['today'].sum() / st['tested'].sum())}
        print(f'  real short blocks (dump): ' + ', '.join(f'draw {d} {100 * v:.2f} %' for d, v in real.items()))
        print(f"  {'candidate':10s} {'share':>7s} {'noise':>7s}  at the dump draws        E: false rate after the fill, fill")
        for cand, v in row.items():
            extra = f"  {100 * v['false']:.2f} %, {v['fill']} draws" if 'false' in v else ''
            if 'skipped' in v:
                extra = (f"  skipped {100 * v['skipped']:.2f} % of the frozen part's tested draws ({100 * v['skippedAll']:.2f} % "
                         f"of all); median m against E's: {v['mRatio']:.3f}")
            if v.get('share400') is not None and start != 400:
                extra += f"  (from draw 400: {100 * v['share400']:.2f} %)"
            print(f"  {cand:10s} {100 * v['share']:6.2f}% {v['noise']:7.4f}  " +
                  ', '.join(f'{d}: {100 * s:.2f} %' for d, s in v['at'].items()) + extra)
        if P == 18 and name != 'cabin, lamps off':
            results['fires'][name] = st['stat'][st['today']].tolist()
        # The false runs under today's bar in the frozen part: the lengths of the runs of tested draws in a row above 3 a,
        # each counted at its last draw.
        r = st['runs']
        ends = (r > 0) & np.concatenate([r[1:] == 0, np.ones((1, *r.shape[1:]), bool)]) & late[:, None, None]
        lengths = r[ends]
        if len(lengths):
            q = [float(v) for v in np.percentile(lengths, (50, 90, 99))]
            results['stills'][label]['falseRuns'] = q + [int(lengths.max()), int(len(lengths))]
            print(f'  false runs under today\'s bar in the frozen part: 50 / 90 / 99 % {q[0]:.0f} / {q[1]:.0f} / {q[2]:.0f} '
                  f'draws, longest {int(lengths.max())}, {len(lengths)} runs')
        if not has_dumps:
            print(f'  (the surface from the log: {int(surface.sum())} of {surface.size} blocks; the frozen part from draw {start})')

# The fair cabin switch, on the log with the rule and on the log without it.
on_draws, on_x, on_g, _ = R.load(R.OLD / 'g-switch' / 'region-none.txt')
off_draws, off_x, off_g, _ = R.load(R.OLD / 'g-switch' / 'region-0-100000000.txt')


def control_level(x, g, draws, rows, columns):
    """The control's settled light per anchor, placed on a ring of rows x columns."""
    keep = draws >= 100
    level = np.where((g[keep] > 0).sum(0) > 0, (x[keep] * (g[keep] > 0)).sum(0) / np.maximum((g[keep] > 0).sum(0), 1), np.nan)
    placed = np.full((rows, columns), np.nan)
    placed[:level.shape[0], :level.shape[1]] = level
    return placed.reshape(-1)


for which in ('rule', 'none'):
    log = R.LOGS / f'switch-{which}' / 'region-193-393.txt'
    draws, x, g, moved = R.load(log)
    rows, columns = x.shape[1:]
    holders, near = R.block_grid(1280, 720, rows, columns)
    seen = (g.reshape(len(g), -1)[:, near] > 0).mean(0) >= 0.5
    lev_on = control_level(on_x, on_g, on_draws, rows, columns)[near]
    lev_off = control_level(off_x, off_g, off_draws, rows, columns)[near]
    parts = {**regions(near.shape), **{f'a{a}': m for a, m in anchor_masks(near).items()}}
    caps, st = R.candidates(x, g, 18, extra_p=(PS['switch'],) if 'switch' in PS else ())
    if which == 'rule':
        agree = int((st['today'] == moved)[st['tested']].sum())
        print(f'\n== the switch with the rule: T against the logged answer: {agree} of {int(st["tested"].sum())} agree')
        results['fires']['switch off and on'] = st['stat'][st['today'] & (((draws >= 193) & (draws < 233)) | ((draws >= 393) & (draws < 433)))[:, None, None]].tolist()
    print(f'\n== the fair cabin switch, the log {"with" if which == "rule" else "without"} the rule: settle in draws '
          f'(off: whole, slowest region, slowest anchor, mean over the anchors | on: the same)')
    res = {}
    for cand, cap in caps.items():
        _, y = R.model(cap, x, g, holders, near)
        out_row = {}
        for event, start, end, level, before in (('off', SWITCH_OFF, SWITCH_ON, lev_off, lev_on),
                                                 ('on', SWITCH_ON, len(draws), lev_on, lev_off)):
            s0, s1_ = np.searchsorted(draws, start), np.searchsorted(draws, end)
            settles = {}
            for pname, mask in parts.items():
                m = mask & seen & ~np.isnan(level) & ~np.isnan(before)
                if not m.any():
                    continue
                lv, bf = level[m].mean(), before[m].mean()
                if pname != 'whole' and not (lv > 0 and abs(bf / lv - 1) > CHANGE):
                    continue
                series = (y[:, m]).mean(1)
                settles[pname] = settle(series, lv, s0, s1_)
            grid = [v for k, v in settles.items() if k.startswith('r')]
            anchors = [v for k, v in settles.items() if k.startswith('a')]
            slow = lambda vs: None if None in vs else max(vs, default=0)
            # The model light 20 and 40 draws after the switch, as a share of the settled level: the mean over the
            # anchors whose settled light the switch changes by more than 10 %.
            shares = {}
            for after in (20, 40):
                vals = []
                for pname, mask in parts.items():
                    if not pname.startswith('a') or pname not in settles:
                        continue
                    m = mask & seen & ~np.isnan(level) & ~np.isnan(before)
                    vals.append(float(y[s0 + after][m].mean() / level[m].mean()))
                shares[after] = float(np.mean(vals))
            out_row[event] = {'whole': settles.get('whole'), 'region': slow(grid), 'anchor': slow(anchors),
                              'at20': shares[20], 'at40': shares[40],
                              'anchorMean': float(np.mean([v for v in anchors if v is not None])) if any(v is not None for v in anchors) else None,
                              'anchorUnsettled': sum(v is None for v in anchors), 'anchors': len(anchors)}
        res[cand] = out_row
        f = lambda v: 'none' if v is None else (f'{v:.1f}' if isinstance(v, float) else str(v))
        print(f"  {cand:8s} off: {f(out_row['off']['whole'])}, {f(out_row['off']['region'])}, {f(out_row['off']['anchor'])}, "
              f"{f(out_row['off']['anchorMean'])} ({out_row['off']['anchorUnsettled']}/{out_row['off']['anchors']} none) | "
              f"on: {f(out_row['on']['whole'])}, {f(out_row['on']['region'])}, {f(out_row['on']['anchor'])}, "
              f"{f(out_row['on']['anchorMean'])} ({out_row['on']['anchorUnsettled']}/{out_row['on']['anchors']} none) | "
              f"light at +20, +40 after off: {100 * out_row['off']['at20']:.1f}, {100 * out_row['off']['at40']:.1f} %; "
              f"after on: {100 * out_row['on']['at20']:.1f}, {100 * out_row['on']['at40']:.1f} %")
    results['switch'][which] = res

# bay-sun and rect-bay: the lag of the model light.
for name, log, process, change, samples in (('bay-sun', R.OLD / 'g-lag' / 'region-none.txt', 1, 33, [33 + k for k in (60, 120, 180, 230)]),
                                             ('rect-bay', R.OLD / 'g-bay' / 'region-none.txt', 3, 0, [50, 90, 130, 170])):
    draws, x, g, moved = R.load(log, process)
    rows, columns = x.shape[1:]
    holders, near = R.block_grid(1280, 720, rows, columns)
    seen = (g.reshape(len(g), -1)[:, near] > 0).mean(0) >= 0.5
    parts = {**regions(near.shape), **{f'a{a}': m for a, m in anchor_masks(near).items()}}
    caps, st = R.candidates(x, g, 18, extra_p=(PS[name],) if name in PS else ())
    flatx = x.reshape(len(x), -1)[:, near]
    print(f'\n== {name}: the lag of the model light in draws (median, largest, count over the picture and its 3 x 3 '
          f'regions; and over the anchors, every anchor-sample pooled: median, 90th percentile, count)')
    res = {}
    for cand, cap in caps.items():
        _, y = R.model(cap, x, g, holders, near)
        lags, anchor_lags = [], []
        for pname, mask in parts.items():
            m = mask & seen
            if not m.any():
                continue
            film = y[:, m].mean(1)
            logged = flatx[:, m].mean(1)
            for t in samples:
                i = int(np.searchsorted(draws, t))
                if i + 5 >= len(draws) or i - 5 < 0:
                    continue
                still = logged[i - 4:i + 5].mean()
                slope = (film[i + 5] - film[i - 5]) / 10
                if film[i] <= 0 or abs(slope) * 60 / film[i] <= CHANGE:
                    continue
                (anchor_lags if pname.startswith('a') else lags).append(float((still - film[i]) / slope))
        res[cand] = {'median': float(np.median(lags)) if lags else None, 'largest': float(max(lags)) if lags else None,
                     'count': len(lags),
                     'anchorMedian': float(np.median(anchor_lags)) if anchor_lags else None,
                     'anchorP90': float(np.percentile(anchor_lags, 90)) if anchor_lags else None,
                     'anchorCount': len(anchor_lags)}
        f = lambda v: 'n/a' if v is None else f'{v:.1f}'
        print(f"  {cand:8s} {f(res[cand]['median'])}, {f(res[cand]['largest'])}, {len(lags)} | anchors: "
              f"{f(res[cand]['anchorMedian'])}, {f(res[cand]['anchorP90'])}, {len(anchor_lags)}")
    results['lag'][name] = res
    if name == 'rect-bay':
        fires = st['today']
        q = {}
        for cand in caps:
            if cand.startswith('C') or cand.startswith('EC'):
                q[cand] = [float(v) for v in np.percentile(caps[cand][fires], (10, 50, 90))]
        results['caps']['rect-bay'] = q
        results['fires']['rect-bay'] = st['stat'][fires].tolist()
        print('  the caps of rect-bay\'s fires under T (10, 50, 90 %): ' +
              '; '.join(f'{c}: {v[0]:.1f}, {v[1]:.1f}, {v[2]:.1f}' for c, v in q.items()))

# The still cabin's draws 100 to 299: a weak true drift.
results['drift'] = {}
for which, build in (('rule', 'r2-main'), ('none', 'r2-noreg')):
    draws, x, g, moved = R.load(R.LOGS / f'{build}-cabin-full' / 'region-none.txt')
    rows, columns = x.shape[1:]
    holders, near = R.block_grid(1280, 720, rows, columns)
    caps, st = R.candidates(x, g, 18, extra_p=(PS['still cabin'],) if 'still cabin' in PS else ())
    span = (draws >= 100) & (draws < 300)
    fires = st['today'] & span[:, None, None]
    late = draws > draws[-1] - 100
    ok = (g[late] > 0).all(0)
    end = x[late].mean(0)
    print(f'\n== the still cabin, draws 100 to 299, the log {"with" if which == "rule" else "without"} the rule: '
          f'{int(fires.sum())} fires of today\'s rule; caps at them (10, 50, 90 %); the model light against the end, '
          f'draws 100 to 199 and 200 to 299 (median over the anchors)')
    logged = [float(np.median((x[(draws >= lo) & (draws < lo + 100)].mean(0) / end - 1)[ok])) for lo in (100, 200)]
    print(f'  the logged light: {100 * logged[0]:+.2f} %, {100 * logged[1]:+.2f} %')
    res = {'logged': logged, 'fires': int(fires.sum())}
    for cand, cap in caps.items():
        _, y = R.model(cap, x, g, holders, near)
        light = []
        for lo in (100, 200):
            sel = (draws >= lo) & (draws < lo + 100)
            per = []
            for a in range(rows * columns):
                m = near == a
                ay, ax = divmod(a, columns)
                if not m.any() or not ok[ay, ax]:
                    continue
                per.append(y[sel][:, m].mean() / end[ay, ax] - 1)
            light.append(float(np.median(per)))
        q = [float(v) for v in np.percentile(cap[fires], (10, 50, 90))] if fires.any() else None
        res[cand] = {'caps': q, 'light': light}
        print(f"  {cand:8s} caps {', '.join(f'{v:.1f}' for v in q) if q else 'n/a'}; light {100 * light[0]:+.2f} %, "
              f"{100 * light[1]:+.2f} %")
    results['drift'][which] = res

(out / 'replay.json').write_text(json.dumps(results))
print(f'\nwrote {out / "replay.json"}')
