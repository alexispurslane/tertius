"""Prints the engine's pass timer medians of several runs side by side.

usage: passmed.py <trace folder>... [--min <ms>]

Each folder holds run/gpu-timings.json. A row is a pass, a column a run. On MoltenVK a pass's own time can hold part of
the next pass's work, so only sums of neighbouring passes and the span are safe (tools/README.md, "GPU trace").
"""
import json, os, sys
args = sys.argv[1:]
least = 0.0
if '--min' in args:
    i = args.index('--min'); least = float(args[i + 1]); del args[i:i + 2]
runs = [json.load(open(os.path.join(a, 'run', 'gpu-timings.json'))) for a in args]
names = []
for r in runs:
    for p in r['passes']:
        if p['pass'] not in names:
            names.append(p['pass'])
print('| Pass | ' + ' | '.join(os.path.basename(a.rstrip('/')) for a in args) + ' |')
print('|---|' + '---:|' * len(args))
for n in names:
    vals = [next((p['medianMs'] for p in r['passes'] if p['pass'] == n), None) for r in runs]
    if max(v or 0 for v in vals) < least:
        continue
    print(f'| {n} | ' + ' | '.join('' if v is None else f'{v:.3f}' for v in vals) + ' |')
print('| frame span, median | ' + ' | '.join(f"{r['frame']['medianMs']:.3f}" for r in runs) + ' |')
print('| GPU clock, MHz | ' + ' | '.join(f"{r.get('gpuClock', {}).get('meanMhz', 0):.1f}" for r in runs) + ' |')
