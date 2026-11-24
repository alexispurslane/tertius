# Tool code of t1-yi11. yitime.py <out folder>: the summary of yitime.sh's takes. For each take <round>-<column> it reads
# gpu-timings.json and prints the frame span's median and mean over the take's draws, and the median of the sum of the
# passes "accumulate indirect diffuse" and "filter indirect diffuse" (the region test runs in the first; MoltenVK's single
# pass times are not valid alone, so the record reads the pair's sum). Medians are of the per-pass medians the demo
# reports. Then, per column, the mean of its two takes. Writes <out>/summary.json, and yi-time.json beside the record.
import json
import sys
from pathlib import Path

out = Path(sys.argv[1])
REC = Path(__file__).resolve().parent.parent
rows = []
for take in sorted(p for p in out.iterdir() if p.is_dir()):
    path = take / 'gpu-timings.json'
    if not path.exists():
        continue
    d = json.loads(path.read_text())
    passes = {p['pass']: p for p in d['passes']}
    pair = passes['accumulate indirect diffuse']['medianMs'] + passes['filter indirect diffuse']['medianMs']
    rows.append({'take': take.name, 'column': take.name.split('-', 1)[1], 'frames': d['frame']['frames'],
                 'frameMedianMs': d['frame']['medianMs'], 'frameMeanMs': d['frame']['meanMs'],
                 'accumulatePlusFilterMedianMs': pair,
                 'accumulateMedianMs': passes['accumulate indirect diffuse']['medianMs']})
columns = {}
for r in rows:
    columns.setdefault(r['column'], []).append(r)
summary = {c: {k: sum(r[k] for r in rs) / len(rs) for k in ('frameMedianMs', 'frameMeanMs', 'accumulatePlusFilterMedianMs',
                                                           'accumulateMedianMs')} | {'takes': len(rs)}
           for c, rs in columns.items()}
result = {'takes': rows, 'columns': summary, 'log': (out / 'takes.log').read_text().splitlines()}
(out / 'summary.json').write_text(json.dumps(result, indent=1))
(REC / 'yi-time.json').write_text(json.dumps(result, indent=1))
print('| take | frame median ms | frame mean ms | accumulate + filter median ms |')
for r in rows:
    print(f"| {r['take']} | {r['frameMedianMs']:.3f} | {r['frameMeanMs']:.3f} | {r['accumulatePlusFilterMedianMs']:.3f} |")
print('| column | frame median ms | frame mean ms | accumulate + filter median ms | takes |')
for c, s in summary.items():
    print(f"| {c} | {s['frameMedianMs']:.3f} | {s['frameMeanMs']:.3f} | {s['accumulatePlusFilterMedianMs']:.3f} | {s['takes']} |")
