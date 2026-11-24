# Tool code of t1-yi11's production check. timing.py <out> <rounds>: paired alternating timing of the clean production
# build (the worktree's Release Tertius.Demo at 8081c945d) against the clean baseline binary (t3-09c31e02's Release
# Tertius.Demo, review base 3cfcfd2b3, used read-only). Each round runs prod then base, or base then prod in odd rounds
# (ABBA). One take: demo-surface's bay-camera after 420 ticks, then a film of 600 frames one tick apart, 1920 x 1080 at
# the headless render scale 2/3, through tools/renderer-survey.py's timed render: no image (--video none), two frames in
# flight, gpu-clock beside it, and the GPU lock's exclusive mode. Before each take it waits until the 1-minute load is
# under 8 and records it. Writes <out>/<round>-<build>/gpu-timings.json, <out>/takes.log and <out>/summary.json.
import importlib.util
import json
import os
import shutil
import statistics
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path('/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144')
spec = importlib.util.spec_from_file_location('survey', ROOT / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)
BINARY = {'prod': ROOT / 'src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo',
          'base': Path('/Users/alexispurslane/.t3/worktrees/tertius/t3-09c31e02/src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo')}
out, rounds = Path(sys.argv[1]), int(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)
script = {'scene': 'demo-surface', 'window': {'width': 1920, 'height': 1080},
          'steps': [{'do': 'camera', 'entity': 'bay-camera'}, {'do': 'run', 'ticks': 420},
                    {'do': 'film', 'name': 't', 'frames': 600, 'every': 1}]}


def load():
    return float(subprocess.run(['sysctl', '-n', 'vm.loadavg'], capture_output=True, text=True).stdout.split()[1])


rows = []
with open(out / 'takes.log', 'a') as log:
    for r in range(rounds):
        for build in (('prod', 'base') if r % 2 == 0 else ('base', 'prod')):
            while load() >= 8:
                time.sleep(20)
            work = out / f'{r:02d}-{build}'
            before = load()
            survey.render(work, script, binary=BINARY[build], timed=True)
            t = json.loads((work / 'gpu-timings.json').read_text())
            passes = {p['pass']: p for p in t['passes']}
            row = {'take': work.name, 'build': build, 'loadBefore': before, 'loadAfter': load(),
                   'frames': t['frame']['frames'], 'frameMedianMs': t['frame']['medianMs'],
                   'frameMeanMs': t['frame']['meanMs'], 'frameP95Ms': t['frame']['p95Ms'],
                   'accumulatePlusFilterMedianMs': passes['accumulate indirect diffuse']['medianMs']
                   + passes['filter indirect diffuse']['medianMs'],
                   'clockMeanMhz': t.get('gpuClock', {}).get('meanMhz')}
            rows.append(row)
            print(json.dumps(row), file=log, flush=True)
            print(json.dumps(row), flush=True)
            for p in work.glob('*.png'):
                p.unlink()


def quartiles(values):
    q = statistics.quantiles(values, n=4)
    return {'q1': q[0], 'median': statistics.median(values), 'q3': q[2], 'n': len(values)}


summary = {b: {k: quartiles([r[k] for r in rows if r['build'] == b])
               for k in ('frameMedianMs', 'frameMeanMs', 'accumulatePlusFilterMedianMs')} for b in BINARY}
pairs = [(rows[i], rows[i + 1]) for i in range(0, len(rows), 2)]
diff = {k: quartiles([(a if a['build'] == 'prod' else b)[k] - (b if a['build'] == 'prod' else a)[k] for a, b in pairs])
        for k in ('frameMedianMs', 'accumulatePlusFilterMedianMs')}
loads = [r['loadBefore'] for r in rows]
result = {'takes': rows, 'summary': summary, 'prodMinusBaseByRound': diff, 'loadBefore': [min(loads), max(loads)]}
(out / 'summary.json').write_text(json.dumps(result, indent=1))
print(json.dumps({'summary': summary, 'diff': diff, 'loads': result['loadBefore']}, indent=1))
