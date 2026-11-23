# Tool code of the region repair's replay (t0-qvm1). table.py <replay.json>: the replay's tables in Markdown.
#   1. The main table, one row per candidate. "Cp" and "ECp" are the continuous cap with each still's own median p
#      (pmeasure.py); the switch, the lag films and the cabin's drift have no measured p, so those cells are empty. The
#      cabin with its lamps off has no measured p either.
#   2. The tails: the anchors' 90th percentile lag in bay-sun and rect-bay, and the switch's model light 20 and 40 draws
#      after off and after on, as a share of the settled level.
import json
import sys

d = json.loads(open(sys.argv[1]).read())
stills = ['exit-cabin', 'lamp hall', 'bay', 'still cabin', 'cabin, lamps off']
pkeys = {}
for s in stills:
    for k in d['stills'][s]['rows']:
        if k[0] == 'C' and k[1:] not in ('1', '2', '4') and k not in ('CK3', 'CP'):
            pkeys.setdefault('Cp', {})[s] = k
        if k.startswith('EC') and k[2:] not in ('1', '2', '4'):
            pkeys.setdefault('ECp', {})[s] = k
rows = ['none', 'T', 'E4', 'E8', 'E16', "E'4", "E'8", 'C1', 'C2', 'C4', 'Cp', 'EC1', 'EC2', 'EC4', 'ECp', "E'C1-4",
        "E'C1-8", 'K6', 'CK3', 'CP']


def cell(row, still):
    key = pkeys.get(row, {}).get(still, row) if row in ('Cp', 'ECp') else row
    v = d['stills'][still]['rows'].get(key)
    return v


def f(v, nd=1):
    return 'none' if v is None else f'{v:.{nd}f}'


print('| Candidate | Short blocks: exit-cabin / lamp hall / bay / still cabin / cabin, lamps off | Added noise, mean of the '
      'first four / lamps off | Switch off: whole, slowest region | Switch on: whole, slowest region | bay-sun lag: '
      'median, largest | rect-bay lag: median, largest | Cabin drift, model light 100-199 / 200-299 |')
print('|' + '---|' * 8)
for r in rows:
    vals = [cell(r, s) for s in stills]
    shares = ' / '.join('' if v is None else f"{100 * v['share']:.1f} %" for v in vals)
    four = [v['noise'] for v in vals[:4] if v is not None]
    noise = (f'{sum(four) / len(four):.4f}' if len(four) == 4 else '') + \
        (f" / {vals[4]['noise']:.4f}" if vals[4] is not None else '')
    sw = d['switch']['rule'].get(r)
    bs = d['lag']['bay-sun'].get(r)
    rb = d['lag']['rect-bay'].get(r)
    dr = d['drift']['rule'].get(r)
    cells = [shares, noise,
             f"{f(sw['off']['whole'], 0)}, {f(sw['off']['region'], 0)}" if sw else '',
             f"{f(sw['on']['whole'], 0)}, {f(sw['on']['region'], 0)}" if sw else '',
             f"{f(bs['median'])}, {f(bs['largest'])}" if bs else '',
             f"{f(rb['median'])}, {f(rb['largest'])}" if rb else '',
             f"{100 * dr['light'][0]:+.2f} % / {100 * dr['light'][1]:+.2f} %" if dr else '']
    print(f'| {r} | ' + ' | '.join(cells) + ' |')

print()
print('| Candidate | bay-sun: anchors\' lag, median / 90 % | rect-bay: anchors\' lag, median / 90 % | Switch, light after '
      'off: +20 / +40 draws | Switch, light after on: +20 / +40 draws |')
print('|' + '---|' * 5)
for r in ['none', 'T', 'C1', 'K6', 'CK3', 'CP', "E'C1-4", "E'C1-8"]:
    bs, rb, sw = d['lag']['bay-sun'][r], d['lag']['rect-bay'][r], d['switch']['rule'][r]
    print(f"| {r} | {f(bs['anchorMedian'])} / {f(bs['anchorP90'])} | {f(rb['anchorMedian'])} / {f(rb['anchorP90'])} | "
          f"{100 * sw['off']['at20']:.1f} / {100 * sw['off']['at40']:.1f} % | {100 * sw['on']['at20']:.1f} / "
          f"{100 * sw['on']['at40']:.1f} % |")

print()
print('| Still | False runs under today\'s bar in the frozen part: 50 / 90 / 99 %, longest, runs |')
print('|---|---|')
for s_ in stills:
    q = d['stills'][s_].get('falseRuns')
    if q:
        print(f'| {s_} | {q[0]:.0f} / {q[1]:.0f} / {q[2]:.0f} draws, {q[3]}, {q[4]} |')
