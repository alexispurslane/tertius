# compare.py <baseline name> <variant names...>: what each pass pays in each variant trace, less the baseline. Reads
# trace/<name>/pays.json. A name "a+b" is the mean of the traces a and b (two baselines of one take).
import sys, json
S = '/private/tmp/tertius-sweep-20261111/trace/'
def load(name):
    ds = [json.load(open(S + n + '/pays.json')) for n in name.split('+')]
    passes = {p: sum(d['passes'].get(p, {'work': 0, 'idle': 0})['work'] + d['passes'].get(p, {'work': 0, 'idle': 0})['idle'] for d in ds) / len(ds) for p in ds[0]['passes']}
    return sum(d['span_mean'] for d in ds) / len(ds), passes
bspan, base = load(sys.argv[1])
vs = [(n, *load(n)) for n in sys.argv[2:]]
print('| pass | ' + sys.argv[1] + ' | ' + ' | '.join(n for n, _, _ in vs) + ' |'); print('|---|---:|' + '---:|' * len(vs))
for p, b in base.items():
    ds = [v[2].get(p, 0) - b for v in vs]
    if b < 0.02 and all(abs(d) < 0.02 for d in ds): continue
    print(f'| {p} | {b:.3f} | ' + ' | '.join(f'{d:+.3f}' if abs(d) >= 0.015 else '.' for d in ds) + ' |')
print(f'| frame span (mean) | {bspan:.3f} | ' + ' | '.join(f'{v[1] - bspan:+.3f}' for v in vs) + ' |')
