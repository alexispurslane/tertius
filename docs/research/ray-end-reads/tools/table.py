# Tool code of ter-pdw8: the table of measures 1 to 5 per variant and region for one still, from each take's
# census-result.json (analyze.py) and lamp-result.json (lamp.py, takes with a keep rectangle). Per region:
#   var      = the mean over the region's pixels of the per-pixel variance of the diffuse sample X over the counted draws
#   far var  = var times the far hits' share of the variance (sum of Cov(X_far, X) over sum of Var(X))
#   factor   = V0's far var over this variant's (above 1: the variant lowers it)
#   shares   = miss, near, far shares of the variance
#   mean     = the region's mean of X; bias = mean / V0's mean - 1
#   check    = census mean / slow history mean (measure 4: 1.00 means the accumulator keeps what it receives)
#   rel factor = V0's var / mean^2 over this variant's: the factor of the relative variance, which no exposure moves
#   radiance bias = (mean / mean of 2^stops) over V0's - 1: the bias with each draw's pre-exposure undone (the
#                   automatic exposure differs between variants; with a fixed exposure it equals the bias)
# Per keep rectangle (lamp.py): the brightest 0.1 % of the diffuse and reflection samples (largest value, threshold,
# classes, hit distance), samples above 5 and their draws, speck pixels of the per-pixel mean, specks of the captures,
# and the guided share.
# Usage: python3 table.py <takes dir> <prefix> [variants...]   (default variants v0 v0b v1 v2 v3)
import json, sys
from pathlib import Path

takes, prefix = Path(sys.argv[1]), sys.argv[2]
variants = sys.argv[3:] or ['v0', 'v0b', 'v1', 'v2', 'v3']
variants = [v for v in variants if (takes / f'{prefix}-{v}' / 'census-result.json').exists()]
res = {v: json.loads((takes / f'{prefix}-{v}' / 'census-result.json').read_text()) for v in variants}
scale = {}
for v in variants:
    stops = [s for s in json.loads((takes / f'{prefix}-{v}' / 'census.json').read_text())['Stops'] if s is not None]
    scale[v] = sum(2.0 ** s for s in stops) / len(stops) if stops else 1.0
base = {r['name']: r for r in res['v0']['regions']}
print(f'### {prefix}: measures 1, 3 and 4 per region (diffuse samples, after the caps)\n')
print('| Region | Variant | Var | Far var | Factor vs V0 | Rel factor | Shares miss, near, far | Mean | Bias vs V0 | Radiance bias | Check |')
print('|---|---|---:|---:|---:|---:|---|---:|---:|---:|---:|')
for name in base:
    b = base[name]['after_caps']
    bfar = b['mean_variance'] * b['variance_share']['far']
    for v in variants:
        r = next((x for x in res[v]['regions'] if x['name'] == name), None)
        if r is None:  # a take from before the region
            continue
        a = r['after_caps']
        far = a['mean_variance'] * a['variance_share']['far']
        s = a['variance_share']
        rel = (b['mean_variance'] / b['mean_luminance'] ** 2) / (a['mean_variance'] / a['mean_luminance'] ** 2)
        radiance = (a['mean_luminance'] / scale[v]) / (b['mean_luminance'] / scale['v0']) - 1
        print(f"| {name} | {v} | {a['mean_variance']:.4g} | {far:.4g} | {bfar / far:.2f} | {rel:.2f} | "
              f"{s['miss']:.3f}, {s['near']:.3f}, {s['far']:.3f} | {a['mean_luminance']:.4f} | "
              f"{a['mean_luminance'] / b['mean_luminance'] - 1:+.3f} | {radiance:+.3f} | {r['check']['ratio']:.3f} |")
lamps = {v: takes / f'{prefix}-{v}' / 'lamp-result.json' for v in variants}
if all(p.exists() for p in lamps.values()):
    print(f'\n### {prefix}: measures 2 and 5 in the keep rectangle\n')
    print('| Variant | Diffuse: largest, threshold, distance | Diffuse above 5 (draws) | Reflection: largest, threshold, '
          'far share of light | Mean specks diffuse, reflection | Capture specks: median, largest | Guided share: all, '
          'brightest 0.1 %, above 5 |')
    print('|---|---|---|---|---|---|---|')
    for v in variants:
        d = json.loads(lamps[v].read_text())
        db, rb, g = d['diffuse_brightest'], d['reflection_brightest'], d['guided_share']
        caps = sorted(d['capture_specks'].values())
        dist = db.get('hit_distance_m', [0, 0])
        above = g['above_5'] if g['above_5'] is not None else float('nan')
        print(f"| {v} | {db['max']:.3g}, {db['threshold']:.3f}, {dist[0]:.1f} to {dist[1]:.0f} m | "
              f"{g['above_5_count']} | {rb['max']:.3g}, {rb['threshold']:.3f}, {rb['light_share']['far']:.2f} | "
              f"{d.get('mean_specks_x')}, {d.get('mean_specks_xs')} | {caps[len(caps) // 2]}, {caps[-1]} | "
              f"{g['all']:.3f}, {g['brightest_0.1pct']:.3f}, {above:.3f} |")
