# Tool code of the region repair's replay (t0-qvm1). table.py <replay.json>: the replay's main table in Markdown, one row
# per candidate. "Cp" and "ECp" are the continuous cap with each still's own median p (pmeasure.py); the switch, the lag
# films and the cabin's drift have no measured p, so those cells are empty.
import json
import sys

d = json.loads(open(sys.argv[1]).read())
stills = ['exit-cabin', 'lamp hall', 'bay', 'still cabin']
pkeys = {}
for s in stills:
    for k in d['stills'][s]['rows']:
        if k[0] == 'C' and k[1:] not in ('1', '2', '4'):
            pkeys.setdefault('Cp', {})[s] = k
        if k.startswith('EC') and k[2:] not in ('1', '2', '4'):
            pkeys.setdefault('ECp', {})[s] = k
rows = ['none', 'T', 'E4', 'E8', 'E16', 'C1', 'C2', 'C4', 'Cp', 'EC1', 'EC2', 'EC4', 'ECp', 'K6']


def key(row, still=None):
    return pkeys[row][still] if row in pkeys and still else row


def f(v, nd=1):
    return 'none' if v is None else f'{v:.{nd}f}'


head = ('| Candidate | Short blocks: exit-cabin / lamp hall / bay / still cabin | Added noise, mean of the four | '
        'Switch off: whole, slowest region | Switch on: whole, slowest region | bay-sun lag: median, largest | '
        'rect-bay lag: median, largest | Cabin drift, model light 100-199 / 200-299 |')
print(head)
print('|' + '---|' * 8)
for r in rows:
    shares = [d['stills'][s]['rows'][key(r, s)] for s in stills]
    noise = sum(v['noise'] for v in shares) / 4
    sw = d['switch']['rule'].get(r)
    bs = d['lag']['bay-sun'].get(r)
    rb = d['lag']['rect-bay'].get(r)
    dr = d['drift']['rule'].get(r)
    cells = [' / '.join(f"{100 * v['share']:.1f} %" for v in shares), f'{noise:.4f}',
             f"{f(sw['off']['whole'], 0)}, {f(sw['off']['region'], 0)}" if sw else '',
             f"{f(sw['on']['whole'], 0)}, {f(sw['on']['region'], 0)}" if sw else '',
             f"{f(bs['median'])}, {f(bs['largest'])}" if bs else '',
             f"{f(rb['median'])}, {f(rb['largest'])}" if rb else '',
             f"{100 * dr['light'][0]:+.2f} % / {100 * dr['light'][1]:+.2f} %" if dr else '']
    print(f'| {r} | ' + ' | '.join(cells) + ' |')
