# payschart.py <pays.json> <out.png> <title>: the chart of what each pass pays, by group, and the group sums on stdout.
import sys, json, subprocess, tempfile, os
d = json.load(open(sys.argv[1]))
GROUPS = {
 'lights': ['local shadows', 'direct light', 'transparent'],
 'indirect light': ['depth pyramid', 'light pyramid', 'ray plan', 'reflection rays', 'diffuse rays', 'screen trace resolve',
                    'accumulate indirect diffuse', 'filter indirect diffuse', 'accumulate indirect specular'],
 'output': ['exposure', 'FSR 3.1', 'exposure adapt', 'bloom', 'tone map', 'capture'],
 'geometry and shadow maps': ['shadow maps', 'environment faces', 'environment prefilter', 'G-buffer', 'surface test', 'composite',
                              'scatter rows', 'scatter columns', 'haze', 'outlines', 'deformation'],
 'measurement': ['(stamp)', '(before the first pass)', '(after the last pass)'],
}
rows = []; sums = {}
for g, names in GROUPS.items():
    for n in names:
        p = d['passes'].get(n)
        if not p or p['work'] + p['idle'] < 0.02: 
            sums[g] = sums.get(g, 0) + (p['work'] + p['idle'] if p else 0); continue
        rows.append(dict(label=n.replace('(stamp)', 'the per-pass time stamps'), value=p['work'], idle=p['idle'], group=g))
        sums[g] = sums.get(g, 0) + p['work'] + p['idle']
left = set(d['passes']) - {n for names in GROUPS.values() for n in names}
assert not left, left
f = tempfile.NamedTemporaryFile('w', suffix='.json', delete=False); json.dump(rows, f); f.close()
subprocess.run([sys.executable, '-I', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'bars.py'), f.name, sys.argv[2], sys.argv[3]], check=True)
print('span mean', round(d['span_mean'], 3), 'median', round(d['span_median'], 3))
for g, v in sums.items(): print(f'{g}: {v:.3f} ms')
print('sum', round(sum(sums.values()), 3), ' idle', round(sum(p['idle'] for p in d['passes'].values()), 3), ' encoders', sum(p['encoders'] for p in d['passes'].values()))
