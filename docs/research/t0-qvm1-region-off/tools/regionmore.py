# Tool code of the region take (t0-qvm1), its second step. regionmore.py <label|region log|kind[|process]>...: three more numbers
# from the region logs (the line orchestrator's order of 2026-11-22, 23:44), CPU only. The log and the kinds are
# regionstat.py's (a log may be gzipped: region-*.f32.gz beside region-*.txt). "Tested" and "fires" are regionstat.py's: an
# anchor with 36 valid draws in a row, and stat > 3. The windows: a still's draws (every draw of a still; the switch's
# draws 69 to 192), and the 40 draws from a real change (film:<draw>, switch: 193 and 393). Per run it prints:
#   1. the share of the tested anchor-draws that fire, in the still draws, and in a film from its change on;
#   2. the independence ratio, per anchor over the still draws where it is tested: the standard deviation of the means of
#      every 18-draw window, over (the standard deviation of the single draws / sqrt(18)). 1 for independent draws; above 1
#      where a region's light wanders slowly. Its quantiles (10, 50, 90 %) over the anchors;
#   3. the runs of consecutive firing draws per anchor (a run counts in the window where it starts): their quantiles (50,
#      90, 99 %) and largest, for the still's false fires, for each change's 40 draws, and in a film from its change on;
#      and the rule Kk (k = 3, 6, 9): a fire only where the draw and the k - 1 draws before it at the anchor all have
#      stat > 3. Per rule: the share of the still's fires it drops, the draws from each change to its first fire, and the
#      share of a film's fires (from its change on) that it keeps;
#   4. the still's false rate if the test's noise estimate were multiplied by the anchor's own independence ratio (an
#      oracle: the ratio comes from the whole run);
#   5. a test corrected from the state it already holds: rho = the lag-1 autocorrelation of the older window's 18 draws,
#      kept between 0 and 0.95, and the statistic divided by sqrt((1 + rho) / (1 - rho)), the factor by which an AR(1)
#      series' window mean moves more than independent draws' would. Its still's false rate, first fires and film share.
import gzip
import sys
from pathlib import Path
import numpy as np

W = 18


def floats(txt):
    raw = Path(str(txt)[:-4] + '.f32')
    if raw.exists():
        return np.fromfile(str(raw), dtype='<f4')
    return np.frombuffer(gzip.open(str(raw) + '.gz').read(), dtype='<f4')


def processes(path):
    lines = [l.split() for l in open(path)]
    raw = floats(path)
    blocks, at = [], 0
    for line in lines:
        anchors = int(line[2])
        frame = raw[at:at + 3 * anchors].reshape(anchors, 3)
        at += 3 * anchors
        if int(line[0]) == 0:
            blocks.append([])
        blocks[-1].append((int(line[0]), frame))
    return blocks


def windows(kind, n):
    """The still draws and the changes [(name, draw)]."""
    if kind == 'switch':
        return range(69, 193), [('off', 193), ('on', 393)]
    if kind.startswith('film:'):
        at = int(kind.split(':')[1])
        return range(0, at), [('change', at)]
    if kind == 'still':
        return range(0, n), []
    return range(0, 0), []


def runs_of(mask):
    """(start, length) of every run of True in a 1-D mask."""
    out, start = [], None
    for i, v in enumerate(list(mask) + [False]):
        if v and start is None:
            start = i
        elif not v and start is not None:
            out.append((start, i - start))
            start = None
    return out


q = lambda v, ps: ' '.join(f'{p}% {np.percentile(v, p):.2f}' for p in ps) if len(v) else 'none'
for spec in sys.argv[1:]:
    label, path, kind, *pick = spec.split('|')
    blocks = processes(Path(path))
    block = blocks[int(pick[0]) - 1] if pick else blocks[0]
    draws = np.array([d for d, _ in block])
    light = np.stack([f[:, 0] for _, f in block]).astype(float)
    valid = np.stack([f[:, 1] for _, f in block])
    n, anchors = light.shape
    tested = valid >= 2 * W
    stat = np.zeros_like(light)
    for t in range(2 * W - 1, n):
        newer, older = light[t - W + 1:t + 1], light[t - 2 * W + 1:t - W + 1]
        spread = older.std(axis=0, ddof=1)
        diff = np.abs(newer.mean(axis=0) - older.mean(axis=0))
        with np.errstate(divide='ignore', invalid='ignore'):
            stat[t] = np.where(spread > 0, diff / (spread * np.sqrt(2 / W)), np.where(diff > 0, np.inf, 0))
    fire = tested & (stat > 3)
    corrected = np.zeros_like(light)
    for t in range(2 * W - 1, n):
        older = light[t - 2 * W + 1:t - W + 1]
        dev = older - older.mean(axis=0)
        with np.errstate(divide='ignore', invalid='ignore'):
            rho = np.clip(np.nan_to_num((dev[:-1] * dev[1:]).sum(axis=0) / (dev * dev).sum(axis=0)), 0, 0.95)
        corrected[t] = stat[t] / np.sqrt((1 + rho) / (1 - rho))
    cfire = tested & (corrected > 3)
    still, changes = windows(kind, n)
    still = [t for t in still if t < n]
    print(f'== {label} ({kind}): {n} draws, {anchors} anchors')
    # 1. The share of tested anchor-draws that fire.
    if still:
        print(f'  1. still draws: {fire[still].sum()} fires of {tested[still].sum()} tested anchor-draws '
              f'({100 * fire[still].sum() / max(tested[still].sum(), 1):.2f} %)')
    for name, at in changes:
        if kind.startswith('film:'):
            print(f'  1. the film from its change on: {fire[at:].sum()} fires of {tested[at:].sum()} tested anchor-draws '
                  f'({100 * fire[at:].sum() / max(tested[at:].sum(), 1):.2f} %)')
    # 2. The independence ratio over the still draws where the anchor is tested.
    ratios = []
    for a in range(anchors):
        seg = [t for t in still if tested[t, a]]
        if len(seg) < 2 * W:
            continue
        x = light[seg[0] - 2 * W + 1:seg[-1] + 1, a]  # the tested draws' windows: from the older window's first draw
        if x.std(ddof=1) == 0:
            continue
        means = np.convolve(x, np.ones(W) / W, mode='valid')
        ratios.append(means.std(ddof=1) / (x.std(ddof=1) / np.sqrt(W)))
    if ratios:
        print(f'  2. independence ratio over {len(ratios)} anchors: {q(ratios, (10, 50, 90))}')
    if still:
        scaled = fire.copy()
        kept = 0
        for a in range(anchors):
            seg = [t for t in still if tested[t, a]]
            if len(seg) < 2 * W:
                continue
            x = light[seg[0] - 2 * W + 1:seg[-1] + 1, a]
            if x.std(ddof=1) == 0:
                continue
            r = np.convolve(x, np.ones(W) / W, mode='valid').std(ddof=1) / (x.std(ddof=1) / np.sqrt(W))
            kept += (stat[seg, a] / max(r, 1e-9) > 3).sum()
        print(f'  4. the still\'s false rate with the noise estimate times the anchor\'s ratio: '
              f'{100 * kept / max(tested[still].sum(), 1):.2f} % (today {100 * fire[still].sum() / max(tested[still].sum(), 1):.2f} %)')
    line = '  5. the AR(1)-corrected test:'
    if still:
        line += f' still false rate {100 * cfire[still].sum() / max(tested[still].sum(), 1):.2f} %;'
    for name, at in changes:
        first = [t - at for t in range(at, min(at + 40, n)) if cfire[t].any()]
        line += f' {name}: first fire {first[0] if first else "none in 40"} draws;'
        if kind.startswith('film:'):
            line += f' keeps {100 * cfire[at:].sum() / max(fire[at:].sum(), 1):.1f} % of the film\'s fires;'
    print(line)
    # 3. Runs of consecutive fires, and the rules Kk.
    groups = {'still': [], **{name: [] for name, _ in changes}, 'film': []}
    for a in range(anchors):
        for start, length in runs_of(fire[:, a]):
            if start in still:
                groups['still'].append(length)
            for name, at in changes:
                if at <= start < at + 40:
                    groups[name].append(length)
            if kind.startswith('film:') and start >= changes[0][1]:
                groups['film'].append(length)
    for g, v in groups.items():
        if v:
            print(f'  3. runs of fires, {g}: {len(v)} runs, {q(v, (50, 90, 99))}, largest {max(v)}')
    for k in (3, 6, 9):
        kfire = np.zeros_like(fire)
        for t in range(k - 1, n):
            kfire[t] = fire[t - k + 1:t + 1].all(axis=0)
        line = f'  3. K{k}:'
        if still and fire[still].sum():
            line += f' drops {100 * (1 - kfire[still].sum() / fire[still].sum()):.1f} % of the still\'s fires;'
        for name, at in changes:
            first = [t - at for t in range(at, min(at + 40, n)) if kfire[t].any()]
            line += f' {name}: first fire {first[0] if first else "none in 40"} draws;'
            if kind.startswith('film:'):
                line += f' keeps {100 * kfire[at:].sum() / max(fire[at:].sum(), 1):.1f} % of the film\'s fires;'
        print(line)
